#!/usr/bin/env python3
"""Build career setlists (custom career presets) from song folders in the user's library.

Each source folder (one game's songs, as extracted song.ini folders) becomes one setlist whose gigs
climb in difficulty: the songs are sorted by their band difficulty rating and cut into gigs, easiest
first. Nothing here is built into the game: the output is ordinary custom career files
("<data>/custom/careers/*.json", the same thing Settings > Presets > Career exports), and the songs
are only referenced (artist, title, source, charter), never copied.

    make-setlists.py [--songs DIR] OUT_DIR [OUT_DIR ...]

Ids are derived from names, so running it again rewrites the same setlists and keeps their progress.

Gig pictures: a PNG named "<gig number>.png" in "setlist-art/<source folder name>/" beside this script
(e.g. "setlist-art/Rock Band 1/3.png") becomes that gig's picture, shown in its stop on the gig map. It
is copied into the setlist's content folder (a folder named like the setlist file, beside it).
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import uuid

NAMESPACE = uuid.UUID("6f0e2f3a-52b7-4c8e-9d0b-1c5f6a7b8c9d")

# folder under the songs directory, setlist name, source icon, venues (smallest first)
SETLISTS = [
    ("Rock Band 1", "Rock Band 1", "rb1", [
        ("The Broken String", "Boston"), ("Lucky's Basement", "Providence"),
        ("Third Rail Tavern", "New York"), ("The Copper Kettle", "Philadelphia"),
        ("Harborlight Club", "Baltimore"), ("The Foundry", "Pittsburgh"),
        ("Majestic Theater", "Chicago"), ("Riverfront Pavilion", "St. Louis"),
        ("Summit Arena", "Denver"), ("Pacific Coast Stadium", "Los Angeles")]),
    ("Rock Band 2", "Rock Band 2", "rb2", [
        ("The Tin Roof Shack", "Austin"), ("Neon Cactus", "San Antonio"),
        ("Sidewinder Saloon", "Phoenix"), ("The Velvet Room", "Las Vegas"),
        ("Bayside Ballroom", "San Diego"), ("Fog City Hall", "San Francisco"),
        ("Evergreen Theater", "Portland"), ("Rainier Pavilion", "Seattle"),
        ("Northern Lights Arena", "Vancouver"), ("Rising Sun Dome", "Tokyo")]),
    ("Rock Band 3", "Rock Band 3", "rb3", [
        ("The Garage Door", "Detroit"), ("Lakeside Lounge", "Cleveland"),
        ("The Print Shop", "Toronto"), ("Maple Leaf Music Hall", "Montreal"),
        ("The Underground", "London"), ("Canal House", "Amsterdam"),
        ("Le Petit Palais", "Paris"), ("Spree Works", "Berlin"),
        ("Colosseo Arena", "Rome"), ("Midnight Sun Festival", "Stockholm")]),
    ("Rock Band 4", "Rock Band 4", "rb4", [
        ("The Corner Booth", "Nashville"), ("Bluebird Alley", "Memphis"),
        ("Crescent Club", "New Orleans"), ("Peachtree Hall", "Atlanta"),
        ("Gulfstream Room", "Miami"), ("Capital Theater", "Washington"),
        ("The Steel Yard", "Dallas"), ("Mile High Amphitheater", "Denver"),
        ("Golden Gate Arena", "San Francisco"), ("Desert Sky Festival", "Palm Springs")]),
    ("The Beatles Rock Band", "The Beatles: Rock Band", "tbrb", [
        ("The Cellar Club", "Liverpool"), ("Dockside Dance Hall", "Hamburg"),
        ("Kingsway Studio", "London"), ("Empire Theatre", "Manchester"),
        ("Garden Bowl", "New York"), ("Bayfront Park", "San Francisco"),
        ("Budo Hall", "Tokyo"), ("The Rooftop", "London")]),
]

ART = os.path.join(os.path.dirname(os.path.abspath(__file__)), "setlist-art")

SONGS_PER_GIG = 7
MIN_GIGS, MAX_GIGS = 5, 10
# Average stars per song, over every earlier gig, that opens the next gig.
UNLOCK_STARS_PER_SONG = 3
PARTS = ("diff_guitar", "diff_bass", "diff_drums", "diff_vocals", "diff_keys")
# Alternate charts of a song that is already there in its plain form.
VARIANT = re.compile(r"\s*\((2x Bass Pedal[^)]*|Vocal Version)\)\s*$", re.I)


def read_ini(path):
    for encoding in ("utf-8-sig", "latin-1"):
        try:
            with open(path, encoding=encoding) as handle:
                lines = handle.read().splitlines()
            break
        except UnicodeDecodeError:
            continue
    values = {}
    for line in lines:
        if "=" in line and not line.lstrip().startswith(("[", ";", "#")):
            key, value = line.split("=", 1)
            values[key.strip().lower()] = value.strip()
    return values


def number(values, key, default=-1):
    try:
        return int(values.get(key, default))
    except ValueError:
        return default


def load_songs(folder):
    """The songs directly in the folder (sub-collections such as DLC are left out)."""
    songs = []
    for entry in sorted(os.listdir(folder)):
        ini = os.path.join(folder, entry, "song.ini")
        if not os.path.isfile(ini):
            continue
        values = read_ini(ini)
        if not values.get("name") or not values.get("artist"):
            continue
        parts = [number(values, key) for key in PARTS]
        parts = [part for part in parts if part >= 0]
        band = number(values, "diff_band")
        if band < 0:
            band = max(parts) if parts else 2
        songs.append({
            "title": values["name"], "artist": values["artist"],
            "source": values.get("icon", ""), "charter": values.get("charter", ""),
            "band": band, "parts": sum(parts) / len(parts) if parts else band,
            "length": number(values, "song_length", 0),
        })

    plain = {(song["artist"], song["title"]) for song in songs}
    kept = []
    for song in songs:
        base = VARIANT.sub("", song["title"])
        if base != song["title"] and (song["artist"], base) in plain:
            continue
        kept.append(song)
    return kept


def guid_from_md5(text):
    """Guid(md5(text)) as the game derives a tuple song's id (CareerSong)."""
    return str(uuid.UUID(bytes_le=hashlib.md5(text.encode("utf-8")).digest()))


def build(folder, name, source, venues):
    songs = load_songs(folder)
    usable = [song for song in songs if song["source"] and song["charter"]]
    skipped = len(songs) - len(usable)
    usable.sort(key=lambda song: (song["band"], song["parts"], song["length"], song["title"]))

    gigs = max(MIN_GIGS, min(MAX_GIGS, len(venues), round(len(usable) / SONGS_PER_GIG)))
    gigs = max(1, min(gigs, len(usable)))
    tiers = []
    pictures = {}
    before = 0
    for index in range(gigs):
        chunk = usable[len(usable) * index // gigs:len(usable) * (index + 1) // gigs]
        venue, city = venues[round(index * (len(venues) - 1) / max(1, gigs - 1))]
        tiers.append({
            "Name": venue,
            "Id": str(uuid.uuid5(NAMESPACE, f"{name}/gig/{index}")),
            "Description": None,
            "Songs": [{
                "Id": guid_from_md5(f"{song['artist']} - {song['title']} - {song['source']} - {song['charter']}"),
                "SongTuple": {"Artist": song["artist"], "Title": song["title"],
                              "Source": song["source"], "Charter": song["charter"]},
                "Identifier": "SongTuple",
                "Description": f"{song['artist']} - {song['title']}",
            } for song in chunk],
            "VenueSize": "NotSpecified",
            "VenueHint": None,
            "UnlockType": "StarCount",
            "UnlockCriteria": UNLOCK_STARS_PER_SONG * before,
            "CompletionMode": "SingleSong",
            "IsBonus": False,
            "VenueName": venue,
            "City": city,
        })
        before += len(chunk)

        picture = os.path.join(ART, os.path.basename(folder), f"{index + 1}.png")
        if os.path.isfile(picture):
            tiers[-1]["CompletionBonus"] = "Image"
            tiers[-1]["MediaFilename"] = f"gig-{index + 1}.png"
            pictures[tiers[-1]["MediaFilename"]] = picture

    preset = {
        "Id": str(uuid.uuid5(NAMESPACE, name)),
        "Name": name,
        "Type": "Career",
        "Description": f"{len(usable)} songs over {gigs} gigs, from the easiest to the hardest. "
                       f"Built from the songs in your library; they are not part of YARG.",
        "Source": source,
        "Version": 1,
        "Tiers": tiers,
    }
    return preset, usable, skipped, pictures


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--songs", default=os.path.expanduser("~/songs/all_songs"))
    parser.add_argument("out", nargs="+", help="custom/careers folder(s) to write the setlists into")
    args = parser.parse_args()

    for folder, name, source, venues in SETLISTS:
        path = os.path.join(args.songs, folder)
        if not os.path.isdir(path):
            print(f"{name}: no folder {path}, skipped", file=sys.stderr)
            continue
        preset, songs, skipped, pictures = build(path, name, source, venues)
        # The game's own file name for the preset (first 20 characters of the name, then 8 of the id).
        file_name = re.sub(r"[^A-Za-z0-9]", "_", name[:20]) + f".{preset['Id'][:8]}.json"
        for out in args.out:
            os.makedirs(out, exist_ok=True)
            with open(os.path.join(out, file_name), "w", encoding="utf-8") as handle:
                json.dump(preset, handle, indent=2, ensure_ascii=False)
            if pictures:
                content = os.path.join(out, file_name[:-len(".json")])
                os.makedirs(content, exist_ok=True)
                for media, picture in pictures.items():
                    shutil.copyfile(picture, os.path.join(content, media))
        ratings = [f"{min(s['band'] for s in t)}-{max(s['band'] for s in t)}" for t in
                   [[song for song in songs if any(
                       entry["SongTuple"]["Title"] == song["title"] and entry["SongTuple"]["Artist"] == song["artist"]
                       for entry in tier["Songs"])] for tier in preset["Tiers"]]]
        print(f"{name}: {len(songs)} songs, {len(preset['Tiers'])} gigs "
              f"(sizes {[len(t['Songs']) for t in preset['Tiers']]}, band difficulty {ratings})"
              + (f", {skipped} left out (no source or charter)" if skipped else "")
              + (f", {len(pictures)} gig pictures" if pictures else "") + f" -> {file_name}")


if __name__ == "__main__":
    main()
