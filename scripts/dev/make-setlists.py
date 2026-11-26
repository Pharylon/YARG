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

# Themed bonus gigs: drawn from the game's DLC (and the game itself), placed after a regular gig.
# theme: the gig's CareerGigTheme (Sources are added: the game and its DLC). songs: how many of the
# fitting songs the gig lists, spread over their difficulty. picks: user picks instead of songs.
# from: the song folders to choose from ("DLC" = the game's DLC folder, "nested" = sub-folders of the
# game's own folder, e.g. The Beatles' DLC albums).
BONUS = {
    "Rock Band 1": [
        # Boston's whole DLC album; any '70s classic rock song can stand in for one of them.
        dict(key="boston", after=2, name="More Than a Feeling", city="Boston", from_="DLC", songs=6,
             only_artist="Boston", theme=dict(YearMin=1970, YearMax=1979, Genres=["Rock", "Classic Rock"]),
             text="Boston's debut album, live. Swap in any '70s rock or classic rock song you like."),
        dict(key="metal", after=4, name="Heavy Metal Thunder", city="Birmingham", from_="DLC", songs=5,
             theme=dict(Genres=["Metal"]),
             text="Leather, studs and screaming guitars. Metal only tonight."),
        dict(key="newwave", after=5, name="New Wave Dance Party", city="New York", from_="DLC", songs=5,
             theme=dict(YearMin=1977, YearMax=1989, Genres=["New Wave"], Artists=["The Cars", "The Police"]),
             text="Skinny ties and synths: The Cars, The Police and the rest of the new wave."),
        dict(key="pixies", after=7, name="Doolittle Tribute", city="Boston", from_="DLC", songs=5,
             theme=dict(Artists=["Pixies"]),
             text="A tribute night to Boston's own Pixies."),
    ],
    "Rock Band 2": [
        dict(key="grunge", after=2, name="Seattle Sound", city="Seattle", from_="DLC", songs=5,
             theme=dict(YearMin=1988, YearMax=1999, Genres=["Grunge"], Artists=["Pearl Jam", "Nirvana", "Soundgarden"]),
             text="Flannel required. The Seattle sound of the late '80s and '90s."),
        dict(key="hendrix", after=4, name="Experience Hendrix", city="London", from_="DLC", songs=5,
             theme=dict(Artists=["The Jimi Hendrix Experience"]),
             text="Are you experienced? A set of Jimi Hendrix."),
        dict(key="country", after=6, name="Roadhouse Country", city="Nashville", from_="DLC", songs=5,
             theme=dict(Genres=["Country"]),
             text="Boots on the floorboards: a country roadhouse set."),
        dict(key="tap", after=8, name="Turn It Up to Eleven", city="Stonehenge", from_="DLC", songs=5,
             theme=dict(Artists=["Spinal Tap"]),
             text="It's one louder. An evening with Spinal Tap."),
    ],
    "Rock Band 3": [
        dict(key="queen", after=2, name="Queen's Court", city="London", from_="DLC", songs=5,
             theme=dict(Artists=["Queen"]),
             text="A royal night of Queen."),
        dict(key="punk", after=4, name="London Calling", city="London", from_="DLC", songs=5,
             theme=dict(YearMin=1976, YearMax=1985, Genres=["Punk"], Artists=["The Clash"]),
             text="Safety pins and three chords: the first wave of punk."),
        dict(key="pianoman", after=6, name="Piano Bar", city="Long Island", from_="DLC", songs=5,
             theme=dict(Artists=["Billy Joel", "Elton John"]),
             text="Sing us a song, you're the piano man."),
        dict(key="country", after=8, name="Honky-Tonk Saturday", city="Austin", from_="DLC", songs=5,
             theme=dict(Genres=["Country"], Artists=["Johnny Cash"]),
             text="Country, outlaws and Johnny Cash."),
    ],
    # No DLC folder for Rock Band 4: its bonus gigs are user picks from any Rock Band game.
    "Rock Band 4": [
        dict(key="2010s", after=3, name="Modern Rock Request Hour", city="Los Angeles", picks=3,
             theme=dict(YearMin=2010, Sources=["rb1", "rb1dlc", "rb2", "rb2dlc", "rb3", "rb3dlc", "rb4"]),
             text="The crowd wants the new stuff: pick three songs from 2010 on."),
        dict(key="throwback", after=6, name="Throwback Thursday", city="Chicago", picks=3,
             theme=dict(YearMax=1979, Sources=["rb1", "rb1dlc", "rb2", "rb2dlc", "rb3", "rb3dlc", "rb4"]),
             text="Throwback night: pick three songs from before 1980."),
    ],
    "The Beatles Rock Band": [
        dict(key="rubbersoul", after=1, name="Rubber Soul Sessions", city="London", from_="nested", songs=5,
             theme=dict(YearMin=1965, YearMax=1965),
             text="December 1965: Rubber Soul."),
        dict(key="pepper", after=3, name="Summer of Love", city="London", from_="nested", songs=5,
             theme=dict(YearMin=1967, YearMax=1967),
             text="1967 and the Summer of Love: Sgt. Pepper and friends."),
        dict(key="abbeyroad", after=5, name="Abbey Road", city="London", from_="nested", songs=5,
             theme=dict(YearMin=1969, YearMax=1969),
             text="The last songs they recorded together, 1969."),
    ],
}

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


def load_tree(folder):
    """Every song in the folder and its sub-folders."""
    songs = []
    for root, _, files in os.walk(folder):
        if "song.ini" not in files:
            continue
        values = read_ini(os.path.join(root, "song.ini"))
        if not values.get("name") or not values.get("artist") or not values.get("icon") or not values.get("charter"):
            continue
        parts = [number(values, key) for key in PARTS]
        parts = [part for part in parts if part >= 0]
        band = number(values, "diff_band")
        if band < 0:
            band = max(parts) if parts else 2
        year = re.search(r"\d{4}", values.get("year", ""))
        songs.append({
            "title": values["name"], "artist": values["artist"],
            "source": values["icon"], "charter": values["charter"],
            "genre": values.get("genre", ""), "year": int(year.group(0)) if year else 0,
            "band": band, "parts": sum(parts) / len(parts) if parts else band,
            "length": number(values, "song_length", 0),
        })
    return songs


def norm(text):
    return "".join(ch.lower() for ch in text if ch.isalnum())


def fits(theme, song):
    """CareerThemes.Matches (Assets/Script/Career/CareerGigTheme.cs), for the songs of a folder."""
    if (theme.get("YearMin") or theme.get("YearMax")) and not (
            song["year"] > 0 and theme.get("YearMin", 0) <= song["year"] <= theme.get("YearMax", 9999)):
        return False
    if theme.get("Sources") and song["source"].lower() not in [s.lower() for s in theme["Sources"]]:
        return False
    genres, artists = theme.get("Genres", []), theme.get("Artists", [])
    if not genres and not artists:
        return True
    return norm(song["genre"]) in [norm(g) for g in genres] or \
        song["artist"].strip().lower() in [a.strip().lower() for a in artists]


def pick_spread(songs, count):
    """count songs spread from the easiest to the hardest, one per artist where there is a choice."""
    if len(songs) <= count:
        return sorted(songs, key=lambda s: (s["band"], s["parts"]))
    songs = sorted(songs, key=lambda s: (s["band"], s["parts"], s["title"]))
    chosen, artists = [], set()
    for want in range(count):
        target = round(want * (len(songs) - 1) / max(1, count - 1)) if songs else 0
        order = sorted(range(len(songs)), key=lambda i: abs(i - target))
        for i in order:
            song = songs[i]
            if song in chosen:
                continue
            if song["artist"] in artists and any(s["artist"] not in artists and s not in chosen for s in songs):
                continue
            chosen.append(song)
            artists.add(song["artist"])
            break
    return sorted(chosen, key=lambda s: (s["band"], s["parts"]))


def song_entry(song):
    return {
        "Id": guid_from_md5(f"{song['artist']} - {song['title']} - {song['source']} - {song['charter']}"),
        "SongTuple": {"Artist": song["artist"], "Title": song["title"],
                      "Source": song["source"], "Charter": song["charter"]},
        "Identifier": "SongTuple",
        "Description": f"{song['artist']} - {song['title']}",
    }


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
            "Songs": [song_entry(song) for song in chunk],
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

    # Themed bonus gigs, each after its regular gig; open once the gigs before it have 3 stars a song.
    base_source = usable[0]["source"] if usable else ""
    # A song is in one gig only (career song ids must be unique in a setlist).
    used = {(s["artist"], s["title"]) for s in usable}
    folder_name = os.path.basename(folder)
    # Chosen in listed order (so earlier themes get first pick), inserted from the last one back.
    bonus_gigs = []
    for bonus in BONUS.get(folder_name, []):
        after = min(bonus["after"], len(tiers) - 1)
        theme = dict(bonus["theme"])
        if "Sources" not in theme:
            theme["Sources"] = [base_source, base_source + "dlc"]
        gig_songs = []
        if bonus.get("songs"):
            pool_folder = os.path.join(os.path.dirname(folder), folder_name + " DLC") if bonus.get("from_") == "DLC" else folder
            pool = load_tree(pool_folder) if os.path.isdir(pool_folder) else []
            if bonus.get("from_") == "nested":
                own = {(s["artist"], s["title"]) for s in usable}
                pool = [s for s in pool if (s["artist"], s["title"]) not in own]
            # The gig's own songs: those of one artist (only_artist), or songs that fit the theme.
            if bonus.get("only_artist"):
                pool = [s for s in pool if s["artist"] == bonus["only_artist"]]
            else:
                pool = [s for s in pool if fits(theme, s)]
            pool = [s for s in pool if (s["artist"], s["title"]) not in used]
            gig_songs = pick_spread(pool, bonus["songs"])
            used.update((s["artist"], s["title"]) for s in gig_songs)
            if not gig_songs:
                print(f"{name}: no songs fit the bonus gig {bonus['name']}, skipped", file=sys.stderr)
                continue
        before_stars = sum(len(t["Songs"]) for t in tiers[:after + 1] if not t["IsBonus"])
        gig = {
            "Name": bonus["name"],
            "Id": str(uuid.uuid5(NAMESPACE, f"{name}/bonus/{bonus['key']}")),
            "Description": None,
            "Songs": [song_entry(song) for song in gig_songs],
            "VenueSize": "NotSpecified",
            "VenueHint": None,
            "UnlockType": "StarCount",
            "UnlockCriteria": UNLOCK_STARS_PER_SONG * before_stars,
            "CompletionMode": "SingleSong",
            "IsBonus": True,
            "VenueName": bonus["name"],
            "City": bonus["city"],
            "CustomUnlockText": bonus["text"],
            "Theme": theme,
        }
        if bonus.get("picks"):
            gig["UserPicks"] = bonus["picks"]
        bonus_gigs.append((after, gig))

    for after, gig in sorted(bonus_gigs, key=lambda pair: -pair[0]):
        tiers.insert(after + 1, gig)

    preset = {
        "Id": str(uuid.uuid5(NAMESPACE, name)),
        "Name": name,
        "Type": "Career",
        "Description": f"{len(usable)} songs over {gigs} gigs, from the easiest to the hardest, "
                       f"with themed bonus gigs. "
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
        regular = [t for t in preset["Tiers"] if not t["IsBonus"]]
        bonuses = [t for t in preset["Tiers"] if t["IsBonus"]]
        print(f"{name}: {len(songs)} songs, {len(regular)} gigs (sizes {[len(t['Songs']) for t in regular]})")
        for bonus in bonuses:
            what = f"{bonus.get('UserPicks')} picks" if bonus.get("UserPicks") else \
                "; ".join(entry["Description"] for entry in bonus["Songs"])
            print(f"    bonus '{bonus['Name']}': {what}")
        print(f"  {len(pictures)} gig pictures -> {file_name}" if pictures else f"  -> {file_name}")


if __name__ == "__main__":
    main()
