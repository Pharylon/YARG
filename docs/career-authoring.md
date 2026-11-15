# Custom careers: fans, venues and contracts

This describes the parts of a career file that go beyond tiers of songs: fan-count unlocks, the fan
table, the venue fields of a tier and gig contracts. Everything here is optional. A career that uses
none of it works exactly as before.

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

A career **uses fans** as soon as any of its tiers has `"UnlockType": "FanCount"`. Only then does the game
show fan numbers (career header, tier rows, song rows, score screen, band sheet). There is no other
switch.

## How many fans a song earns

For each song only its **best** result counts: of all the plays of that song in the save, the one worth
the most fans. Replaying a song can raise its fans but never adds a second share.

```
fans = 100 x venue multiplier x star factor
       x 0.5                          (bonus tiers only)
       x (100 + contract bonus) / 100 (only if that result met the song's contract)
```

rounded to the nearest whole fan (halves round up).

| `VenueSize` | venue multiplier | fans at 5 stars |
|---|---|---|
| `NotSpecified`, `Starter` | 1 | 100 |
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
- two songs (or two tiers) with the same `Id`.

The same warnings show in the career preset editor: on the tier's summary row (`[!] ...`) and as the
first lines of the tier's editor. They are warnings only; the career still loads.

## Editing in the game

The career preset editor (Settings > Presets > Career Presets, on a copied career) has fields for all of
the above. On a tier: Unlock Criteria (including Fan Count) and Unlock Threshold, Venue Size, Fan
Multiplier (leave it empty to use the venue size), City, Venue Name, and Contract with its parameter, fan
bonus and text (leave the text empty for the generated description). On a song: Contract, where
"From tier" keeps the tier's contract and "No contract" exempts the song. The parameter field's label says
what the number means for the chosen type. A fan multiplier or parameter that is not a number is ignored
(the saved value stays).
