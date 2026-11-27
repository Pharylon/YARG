# Custom careers: fans, venues, contracts and shows

This describes the parts of a career file that go beyond tiers of songs: fan-count unlocks, the fan
table, the venue fields of a tier, gig contracts, and gigs played as shows (setlists, encores, gig
rules). Everything here is optional. A career that uses none of it works exactly as before.

## Where career files live

Custom careers are JSON files in the `custom/careers` folder of the game's data folder. The game
renames each file to `<name, up to 20 characters>.<first 8 characters of its Id>.json` when it loads.
The easiest way to start is to copy a career in **Settings > Presets > Career Presets** (Copy) and edit
the copy, in the game or in the file.

Every tier and every song needs a stable `Id` (a GUID). Progress is stored against these ids, so do not
change them after people have played the career, and do not reuse one id for two songs.

## Fan-count unlocks

A tier has an `UnlockType` and an `UnlockCriteria`:

| `UnlockType` | The tier opens when ... |
|---|---|
| `StarCount` | the best stars of the songs in the regular (non-bonus) tiers *before it in the list* add up to `UnlockCriteria` |
| `CompletionCount` | that many songs of the regular tiers before it have been completed |
| `FanCount` | the save's total fans reach `UnlockCriteria` |

The first tier is always open, whatever its unlock type. A star or completion tier needs at least one
regular tier before it, otherwise it never opens.

Fan-count tiers count the save's **total** fans, earned anywhere in the career (bonus tiers included),
not the tiers before them. So several tiers with the same or nearby thresholds open together and the
players choose which gig to play. Mixing unlock types in one career works.

Every career earns and shows fans (career header, tier rows, song rows, score screen, band sheet),
whether or not it has fan-count unlocks.

## How many fans a song earns

For each song only its **best** result counts: of all the plays of that song in the save, the one worth
the most fans. Replaying a song can raise its fans but never adds a second share.

When another song is played in a song's place (a swap, or a user pick), the slot **trades** to it: only
results of the song last played in the slot count, even if an earlier song had done better. Playing the
earlier song again trades the slot back, and its earlier results count again.

```
fans = 100 x multiplier x star factor
       x 0.5                          (bonus tiers only)
       x (100 + contract bonus) / 100 (only if that result met the song's contract)
```

rounded to the nearest whole fan (halves round up).

The multiplier is the first of these that applies to the song's tier:

1. the tier's `FanMultiplier`, if set;
2. the tier's `VenueSize`, if set to anything but `NotSpecified` (table below);
3. otherwise the **song's difficulty**: harder songs earn more.

So a setlist that sets neither gets fans from how hard its songs are, and a setlist that wants its own
amounts sets a venue size or a multiplier on the tier.

| Song difficulty (band rating, 0 to 6) | multiplier | fans at 5 stars |
|---|---|---|
| 0 | 1 | 100 |
| 1 | 1.5 | 150 |
| 2 | 2.5 | 250 |
| 3 | 4 | 400 |
| 4 | 6 | 600 |
| 5 | 10 | 1,000 |
| 6 | 15 | 1,500 |

The rating is the chart's band difficulty; a chart without one uses the hardest of its guitar, bass,
drums, keys and vocals ratings, and a song with no rating at all (or not in the library) counts as 2.

| `VenueSize` | venue multiplier | fans at 5 stars |
|---|---|---|
| `Starter` | 1 | 100 |
| `Small` | 2.5 | 250 |
| `Medium` | 6 | 600 |
| `Large` | 15 | 1,500 |
| `Epic` | 40 | 4,000 |

| Best stars | star factor |
|---|---|
| 0 | 0 |
| 1 or 2 | 0.25 |
| 3 | 0.5 |
| 4 | 0.75 |
| 5 | 1 |
| gold | 1.25 |

Example: a 4-star result at a `Large` venue is 1,500 x 0.75 = 1,125 fans; the same result in a bonus tier
is 563.

Rule of thumb for thresholds: add up what the tiers that can be open before a tier give at the average
you have in mind (4 stars is 0.75 of the 5-star column), and set the threshold a bit below that.

## Tier fields

All optional. Leave a field out (or `null`) to use the default.

| Field | Type | Meaning |
|---|---|---|
| `VenueSize` | `NotSpecified`, `Starter`, `Small`, `Medium`, `Large`, `Epic` | Sets the venue multiplier above. Also used to pick an installed venue. |
| `FanMultiplier` | number, e.g. `10.0` | Replaces the venue multiplier for this tier (fans at 5 stars = 100 x this). `null` = from `VenueSize`. |
| `City` | text | In a career that uses fans, the selected tier's venue and city are shown in the career menu header ("Westbridge Dome, Westbridge"); both also appear in the band's gig history. When the active band's hometown matches the city (ignoring case), the tier gets a "Hometown show" tag. Flavor only, no extra fans. |
| `VenueName` | text | The venue, shown with the city. |
| `Contract` | contract, see below | A contract for every song of the tier. |

`IsBonus: true` tiers earn fans at half rate. Their fans count towards fan-count unlocks like any
other, but bonus tiers never count for star or completion unlocks, and a locked bonus tier is not shown.

## Gig contracts

A contract is an optional condition with a fan bonus. It goes on a tier (every song of the tier) or on a
single song. A song's own contract replaces its tier's; a song contract of type `None` exempts that song
from its tier's contract. Missing a contract never blocks anything: the result just earns no bonus.

```json
"Contract": {
  "Type": "MinStars",
  "Parameter": 4,
  "BonusPercent": 50,
  "Text": "Play it like you mean it"
}
```

| Field | Meaning |
|---|---|
| `Type` | One of the types below. |
| `Parameter` | Depends on the type. |
| `BonusPercent` | Extra fans, in percent of the song's fans, for a result that met the contract (whole number; below 0 counts as 0). |
| `Text` | Optional. Replaces the generated description of the condition ("Crowd pleaser: 4 stars or better"). |

| `Type` | `Parameter` | Met when ... |
|---|---|---|
| `None` | unused | No contract (on a song: exempt from the tier's contract). |
| `MinStars` | 1 to 5, or 6 for gold | the band's stars are at least that. |
| `MinAccuracy` | 0 to 100 (percent) | the average note accuracy of the players is at least that. Players without notes are left out. |
| `FullCombo` | 0 = every player; N = at least N players | full combos. A player without notes neither counts nor holds back "every player". |
| `MinDifficulty` | 0 Beginner, 1 Easy, 2 Medium, 3 Hard, 4 Expert, 5 Expert+ | every player is on that difficulty or above. |
| `NoFailOff` | unused | the no-fail setting is off for the play. |
| `FullBand` | 1 or more | at least that many human players played. |

The players judged are every human who played the song (band members and anyone sitting in). Solo
players cannot meet `FullBand` or `FullCombo` with N of 2 or more; give such songs another contract or
`None` if that matters to you.

Each play stores the contract type it was judged under. If you later change a song's contract to a
different type, earlier plays no longer earn its bonus; changing only the parameter does not re-judge
them.

## Shows, setlists and gig rules

These tier fields change how a gig is played. All are optional and can be combined.

| Field | Type | Meaning |
|---|---|---|
| `CompletionMode` | `SingleSong` (default), `FullTierPlaylist`, `FullCareerPlaylist` | `FullTierPlaylist`: the gig is played as one **show**. The tier's first row is "Play the full set" (clicking any of its songs does the same): the players pick instruments once, then the songs play one after another with the score screen in between, like a setlist from the music library. Every song counts on its own as soon as it ends, so leaving early (quitting, "End setlist early") keeps the songs already finished; the rest simply were not played. `FullCareerPlaylist` is not supported yet: such a tier is played song by song, and the game warns about it. |
| `SetlistSize` | whole number | **Make your own setlist**: the tier is complete after this many of its songs are completed, not all of them. In a show tier the players pick this many songs ("Pick your set") and the show plays those. In a single-song tier they just play any of them. Leave it out for "every song". |
| `Mystery` | `true` / `false` | **Mystery setlist**: each song's title and artist are hidden in the career list and on the instrument select ("Mystery song") until the band has played it once. |
| `EncoreMinStars` | number, 0 to 5 | **Encore** (show tiers only): when the band's average stars over the songs of the set reach this bar, the tier's encore song(s) are added after the last song of the show. A song that was skipped or not saved counts as 0 stars; gold counts as 5. Mark the encore songs with `"IsEncore": true` on the song. An encore is hidden until it has been played, is never part of the set (not picked, not needed to complete the tier) and cannot be played on its own; its fans count like any song's. |
| `ForcedModifiers` | modifier names, e.g. `"AllStrums"` or `"AllStrums, NoKicks"` | **Gig rules**: every player gets these modifiers for the gig's songs, on top of their own, as far as their instrument has them (e.g. `NoKicks` only for drums). A forced modifier replaces the player's own ones it conflicts with (All Strums replaces All HOPOs). Names: `AllStrums`, `AllHopos`, `AllTaps`, `HoposToTaps`, `TapsToHopos`, `NoKicks`, `NoDynamics`, `UnpitchedOnly`, `NoVocalPercussion`, `RangeCompress`, `OpensToGreens`. |
| `MinSongSpeed` | whole number, percent, 100 to 5000 | **Gig rules**: the gig's songs play at least this fast (a faster speed the players chose is kept). Below 100 is ignored: scores below 100% speed are never saved. |

Gig rules apply to that play only. They are never written to the players' profiles, so quitting the song or
closing the game in the middle of it leaves everyone's own settings as they were. The instrument select shows
the gig's contract and rules above the song's loading phrase.

```json
{
  "Name": "Encore Night",
  "Id": "5b0e7e57-0000-4000-8000-000000000006",
  "UnlockType": "FanCount", "UnlockCriteria": 1200, "VenueSize": "Medium",
  "CompletionMode": "FullTierPlaylist",
  "SetlistSize": 3,
  "EncoreMinStars": 4,
  "ForcedModifiers": "AllStrums",
  "Songs": [
    { "Id": "...", "Identifier": "SongId", "SongId": "..." },
    { "Id": "...", "Identifier": "SongId", "SongId": "..." },
    { "Id": "...", "Identifier": "SongId", "SongId": "..." },
    { "Id": "...", "Identifier": "SongId", "SongId": "..." },
    { "Id": "...", "Identifier": "SongId", "SongId": "...", "IsEncore": true }
  ]
}
```

Here the players pick 3 of the 4 set songs and play them as a show, everyone on All Strums; three 4-star
results bring out the encore. The tier is complete after 3 songs.

## Example

A career with two gigs that open together at 300 fans, one of them with a contract, and a song that is
exempt from it:

```json
{
  "Id": "0b7f2a8e-6a1c-4d1f-9c33-2f6f1e7d4a10",
  "Name": "My Career",
  "Type": "Career",
  "Description": "Start small, pick your gigs.",
  "Source": "yarg",
  "Version": 1,
  "Tiers": [
    {
      "Name": "Open Mic",
      "Id": "6b1d6d84-3f0e-4bb9-a5d4-0a3c2f9f0c01",
      "UnlockType": "FanCount",
      "UnlockCriteria": 0,
      "VenueSize": "Starter",
      "City": "Millbrook",
      "VenueName": "The Lantern Room",
      "Songs": [
        { "Id": "e2d1c7a0-1111-4c2e-8f1a-3b1d2c4e5f60", "Identifier": "SongId", "SongId": "a1a97c1d-19ea-48b4-aa2d-8e0bb69c9992" },
        { "Id": "e2d1c7a0-2222-4c2e-8f1a-3b1d2c4e5f60", "Identifier": "SongId", "SongId": "c0fddcf4-deeb-4b98-8d00-0b98f0ff3882" },
        { "Id": "e2d1c7a0-3333-4c2e-8f1a-3b1d2c4e5f60", "Identifier": "SongId", "SongId": "a1474ea6-ce86-4a86-85c4-8fb596dd8285" }
      ]
    },
    {
      "Name": "Coffee House",
      "Id": "6b1d6d84-3f0e-4bb9-a5d4-0a3c2f9f0c02",
      "UnlockType": "FanCount",
      "UnlockCriteria": 300,
      "VenueSize": "Small",
      "City": "Millbrook",
      "VenueName": "Grindstone Cafe",
      "Contract": { "Type": "MinAccuracy", "Parameter": 85, "BonusPercent": 20 },
      "Songs": [
        { "Id": "e2d1c7a0-4444-4c2e-8f1a-3b1d2c4e5f60", "Identifier": "SongId", "SongId": "4420cda8-b7cd-4f81-a104-3a587ea823ed" },
        {
          "Id": "e2d1c7a0-5555-4c2e-8f1a-3b1d2c4e5f60", "Identifier": "SongId", "SongId": "ef6121fa-9aee-4b96-a644-16fba205d4c8",
          "Contract": { "Type": "None", "Parameter": 0, "BonusPercent": 0 }
        }
      ]
    },
    {
      "Name": "Street Fair",
      "Id": "6b1d6d84-3f0e-4bb9-a5d4-0a3c2f9f0c03",
      "UnlockType": "FanCount",
      "UnlockCriteria": 300,
      "VenueSize": "Small",
      "FanMultiplier": 3.0,
      "City": "Port Ellis",
      "VenueName": "Harbor Street Fair",
      "Songs": [
        {
          "Id": "e2d1c7a0-6666-4c2e-8f1a-3b1d2c4e5f60", "Identifier": "SongId", "SongId": "06a92c7e-b1db-4159-9471-7c3bc6505155",
          "Contract": { "Type": "FullCombo", "Parameter": 1, "BonusPercent": 50, "Text": "One perfect performance" }
        }
      ]
    }
  ]
}
```

At 4 stars the three Open Mic songs give 3 x 75 = 225 fans, not enough for 300; at 5 stars they give 300
and both gigs open at once. The shipped career "YARG Setlist Headliner" is a full example (its source is
`Assets/Script/Career/CareerPreset.Defaults.cs`).

## Warnings

When careers load, the game checks each one and writes a warning to its log (the `logs` folder of the
data folder) for:

- a tier that can never open: a fan-count tier needing more fans than all tiers that can open before it
  could ever give (gold on every song and every contract bonus counted), a star or completion tier
  needing more than the regular tiers before it hold, or a tier only an unreachable tier could open;
- a fan-count tier that needs fans when no tier that can open before it has a song earning any (for
  example `FanMultiplier: 0`, or tiers without songs);
- a contract parameter outside the ranges in the table above, or a negative `BonusPercent`;
- a negative `FanMultiplier`;
- two songs (or two tiers) with the same `Id`;
- `CompletionMode: FullCareerPlaylist` (not supported yet);
- a `MinSongSpeed` outside 100 to 5000;
- a `SetlistSize` below 1 or above the number of set songs (encores not counted);
- an encore song that can never be played (its tier is not `FullTierPlaylist` or has no `EncoreMinStars`
  from 0 to 5), or an `EncoreMinStars` without encore songs or outside 0 to 5. Encores that can never be
  played earn no fans when the reachability of later tiers is checked.

The same warnings show in the career preset editor: on the tier's summary row (`[!] ...`) and as the
first lines of the tier's editor. They are warnings only; the career still loads.

## Editing in the game

The career preset editor (Settings > Presets > Career Presets, on a copied career) has fields for all of
the above. On a tier: Unlock Criteria (including Fan Count) and Unlock Threshold, Venue Size, Fan
Multiplier (leave it empty to use the venue size), City, Venue Name, and Contract with its parameter, fan
bonus and text (leave the text empty for the generated description), Session Length (single songs or a
full tier playlist), Forced Modifiers (names, comma separated; empty for none), Minimum Song Speed (0 for
none), Mystery Setlist, Setlist Size (0 for every song) and Encore Star Bar (empty for no encore). On a song:
Contract, where "From tier" keeps the tier's contract and "No contract" exempts the song, and Encore Song. The parameter field's label says
what the number means for the chosen type. A fan multiplier or parameter that is not a number is ignored
(the saved value stays).

## Themed gigs and user picks

A tier can have a **theme**: which songs of the player's library fit the gig. Every part is optional:

```json
"Theme": {
  "YearMin": 1980, "YearMax": 1989,
  "Genres": ["Metal", "Punk"],
  "Artists": ["Nirvana"],
  "Sources": ["rb1", "Rock Band 2"],
  "Songs": [ { "Id": "...", "SongTuple": { ... }, "Identifier": "SongTuple" } ]
}
```

A song fits when it is one of the theme's `Songs` (those always fit), or when it meets every part that is
set: its year is in the range, it is from one of the `Sources` (a source id such as `yarg` or `rb1`, or its
display name such as `Rock Band 1`), and it is of one of the `Genres` **or** by one of the `Artists`.
Genres match ignoring case, spaces and punctuation (`Pop Rock` = `Pop/Rock`); artists and sources ignoring
case. A song with no year never fits a year range.

What the theme does:

- **Swaps (retail mode).** In retail mode the songs of a bonus gig or a themed gig can be played as
  another song (Orange, "Play Another Song Instead"). In a themed gig the replacement must fit the theme;
  in an unthemed bonus gig it can be any song. The result counts for the song it replaced.
- **User picks.** `"UserPicks": 3` adds three more songs to the gig that the players choose from their
  library when they play them (in every mode). In a themed gig the picks must fit the theme. Picks count
  like the gig's own songs: stars, fans (by the difficulty of the song played) and unlocks. A gig can be
  nothing but picks (`"Songs": []`). A gig with picks is always played song by song, never as a show.

The career menu shows the theme on the gig ("Theme: 1980-1989, Metal or Punk, Rock Band 1"). In the preset
editor the theme and the number of picks are fields of the tier.
