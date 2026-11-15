# Career Mode: working plan

Working file for the band/fans/contracts career loop. Check items off as they land; move
anything we decide against to "Decided against" at the bottom instead of deleting it.

**Scope for now:** Fans & Venues (idea 1), Gig Contracts (idea 2), Band Identity (idea 4).
**Later:** procedural careers from the player's library (idea 3), text events, rival band.

---

## 0. Where we start from

What already exists (official career system, merged to `upstream/dev` as #1741):

| Piece | Where | Notes |
|---|---|---|
| Content model | `Assets/Script/Career/CareerPreset.cs`, `CareerTier.cs`, `CareerSong.cs`, `CareerPreset.Defaults.cs` | Tiers of songs; `UnlockType` = `StarCount` / `CompletionCount`; `IsBonus`; `VenueSize`, `VenueHint`; completion bonus media. Defaults: "YARG Setlist Rock", "YARG Setlist Pop" |
| Progress evaluation | `Assets/Script/Career/CareerEvaluation.cs`, `CareerProgressSnapshot.cs` | Pure function over raw rows. Nothing derived is stored. One side effect: it sets the sticky `CareerSong.IsCompleted` flag (strikethrough), which is never cleared, not even by a reset in the same session |
| Progress sync | `CareerPreset.SyncProgress(CareerDatabase, saveId)` (in `CareerPreset.cs`) | Re-evaluates and writes `CareerTierProgress` unlocked/completed timestamps. Called right after every completion commit |
| Persistence | `Assets/Script/Scores/Career/*` | `CareerDatabase` (queries + commits), `CareerSaves`, `CareerSaveProfiles`, `CareerSongCompletions`, `CareerTierProgress` (sqlite-net, in `scores/scores.db`), `CareerSongCompletionCommit`, `CareerCompletionSource` (`SingleSong`, playlist kinds, `DebugGrant`) |
| Song start | `Menu/Career/ViewTypes/SongViewType.cs` `ViewClick()` | Sets `GlobalVariables.State.CurrentSong` and `.CurrentCareer` (`CareerContext`), pushes DifficultySelect (which forces bots to sit out for careers) |
| Score commit | `Gameplay/GameManager.cs` `RecordScores()` -> `Scores/ScoreContainer.cs` `RecordScore()` -> private `CommitCareerCompletion()` | Runs only if the band score is valid (speed >= 100%, no cheats, bots only with the setting). Save is resolved from *the set of profiles who played*. Bots and replays are rejected |
| Menus | `Menu/Career/CareerListMenu.cs`, `CareerMenu.cs`, `SongView.cs`, `ViewTypes/*` | Career list, then tier/song list (one `ListMenu` row per header/song). Unlock dialog from `CareerMenu.CheckUnlocks()` via `CareerTierProgress.UnlockSeen` |
| Setlist owner | `Assets/Script/Playlists/Setlist.cs` (#1758) | Static owner of the live setlist: `Playlist`, `IsPlaying`, `Index`, `TryStart/TryAdvance/EndShow`, `Changed` event. Careers do not use it yet (single songs only) |
| Authoring | `Settings/Metadata/Tabs/PresetSubTab.Career.cs`, `Settings/Customization/CareerContainer.cs` | Custom careers as presets |
| Playlist completion | `CareerCompletionMode.FullTierPlaylist` / `FullCareerPlaylist` | Declared in content, **not honored by gameplay yet** |

Design principles to keep:
- **Extend the career system, don't fork it.** Bands, fans and contracts are optional additions to `CareerPreset`. Existing careers keep working unchanged.
- **Derive, don't store.** Fans, rank, band stats are computed in `CareerEvaluation` from completion rows, same as stars today. We only add raw facts to the rows.
- **No new art.** Rewards are access, numbers, titles and text. Venue visuals come from installed venues via `VenueSize` / `VenueHint`.
- **Minimal diffs against official files** so merges from `upstream/dev` stay cheap.

### 0.1 Groundwork
- [x] Base branch: `career-bands`, created 2026-11-15 from `upstream/dev` (f29ecb14), YARG.Core at the matching pointer.
- [x] Carried over the uncommitted work from `career` (tier collapse/expand animation, debug "Grant Stars", Linux toolchain packages). Still uncommitted.
- [x] Dev loop: `scripts/dev/yarg-remote.sh` (`build`, `start`, `do`, `shot`, `stop`) builds `Builds/DevLinux` as a test build and drives it through `Assets/Script/Dev/DevRemote.cs`. Verified career list, tier list, instrument select and gameplay.
- [x] Re-read `CareerMenu`, `CareerEvaluation` and the Setlist owner on dev; table above updated, facts in 0.2.
- [x] Check upstream PR #1669 (Profile Rework) status before touching profile code. 2026-11-15: still open, not merged ("attempt #2", 435 files, mergeable, review requested). It covers binding sets only (Reusable/Runtime binding sets, Bindings tab, `GameMode.Menu`); file list not audited for profile-id changes. Bands reference profiles by ID only, so we should not collide; re-check before band work.
- [x] Pure-logic tests: `Tests/YARG.Career.Tests` (`dotnet test`), see 0.3.
- [x] Clipped first tier header: not caused by our code. `ListMenu` (upstream, unchanged) pins the selected row near the top of the viewport and only clamps at the list ends; the career prefab's list viewport has no mask, so rows above the viewport draw under the top bar. With a list a little taller than the viewport, selecting row 1 scrolls to the bottom clamp and leaves part of row 0 under the bar; with a long list, row 0 disappears entirely once the selection moves past it, as upstream does. The collapse work makes short-but-overflowing lists common, which is why it shows up there. Not fixed (needs a `ListMenu` change or a viewport mask in the prefab, both upstream files). Possible small fix later: a `RectMask2D` on the career `ListView` so the cut is clean.

### 0.2 Code notes (as of f29ecb14 + uncommitted carry-over)

Facts later steps rely on. Re-check them after merging `upstream/dev`.

- **How a song start carries career context.** `SongViewType.ViewClick()` builds a `CareerContext` (career, tier and song ids and indexes, `VenueHint`) into `GlobalVariables.State.CurrentCareer`. `PersistentState` is a struct reset to `Default` by `ScoreScreenMenu.OnDisable` (unless `Setlist.IsPlaying`), `GameManager.ForceQuitSong` and the History replay paths. `BackgroundManager` reads `VenueHint`; DifficultySelect forces bots to sit out when a career context is set. Note the score screen does **not** reset state while a setlist show is playing, so a playlist gig must manage `CurrentCareer` per song itself.
- **How saves are resolved.** No save id travels with a song. `ScoreContainer.CommitCareerCompletion` takes the distinct `PlayerId`s of the recorded `PlayerScoreRecord`s and calls `CareerDatabase.GetOrCreateSave(careerId, version, profileIds)`, which finds the active save whose `CareerSaveProfiles` member set *equals* that set, or creates one. The menu (`CareerMenu.FindSaveId`) resolves the same way from `PlayerContainer.Players` that are not sitting out and not bots. So the save is per exact player set: if one more person joins, it is a different (new) save. Reset (hold Yellow) soft-deletes the save.
- **How the view list is built.** `CareerMenu.CreateViewList()`: for each tier, a `TierViewType` header; unlocked tiers list their songs (`SongViewType`) only when expanded (uncommitted collapse work: one expanded tier at a time, kept in a static `_expandedTier`). The first locked tier is shown with its lock hint and the list stops there; bonus tiers that are locked are skipped. Progress comes from a cached `CareerEvaluation`; `ApplyProgress()` + `RequestViewListUpdate()` redraw it. New public `CareerMenu.RefreshProgress()` reloads from the database and presents unlocks (skipped while a dialog is open, so it is deferred, not lost).
- **How the score screen gets its data.** `GameManager.EndSong()` fills `GlobalVariables.State.ScoreScreenStats` (`ScoreScreenStats`: per-player `PlayerScoreCard { Player, Stats (BaseStats), IsHighScore, IsReplay }`, `BandScore`, `BandStars`, `MeanAverageOffset`, `ReplayInfo`), records the score, then loads `SceneIndex.Score`. `ScoreScreenMenu.OnEnable` only reads that struct and `State.CurrentSong`; it does not read the database or the career context. History replays build the same struct (`ReplayViewType`). So "+fans" / contract lines need either new fields on `ScoreScreenStats` or a read of the committed completion (e.g. remember the commit's id in state). *Step 2 chose a static: `CareerSongOutcome.Last`, set by every career commit (cleared at the start of `CommitCareerCompletion`) and matched against `State.CurrentCareer` by `CareerScoreScreen.FanLine()`; contract lines can ride on the same outcome.* After the score screen the menu scene reopens the career menu (`MenuManager._lastOpenMenu`), which re-evaluates progress and shows unlock dialogs.
- **What per-player stats are stored.** `PlayerScoreRecord` (table `PlayerScores`, linked by `GameRecordId`): `PlayerId`, `Instrument`, `Difficulty`, `EnginePresetId`, `Score`, `Stars` (`StarAmount`), `NotesHit`, `NotesMissed`, `IsFc`, `IsReplay`, `Percent` (nullable, older rows lack it; use `GetPercent()`). **Not stored:** max combo/best streak, star power phrases/activations/time, overstrums, solo bonuses, modifiers, song speed per player. Those exist in memory on `BaseStats` (and in the replay file's `ReplayStats`) only. `GameRecord` (table `GameRecords`): date, song checksum/name/artist/charter, replay file name/checksum, `BandScore`, `BandStars` (`StarAmount`), `SongSpeed`, `PlayedWithReplay`, `HasBots`. Not stored: no-fail setting, modifiers, player count other than by counting `PlayerScores` rows.
- **Gold stars.** `GameRecord.BandStars` and `PlayerScoreRecord.Stars` are `StarAmount`, where `StarGold` (6) is distinct from `Star5`. `CareerSongCompletions.BandStars` is an int written with `GetStarCount()`, which maps gold to 5, so the completion row loses gold; it is recoverable only by joining `GameRecords` on `GameRecordId` (0 for `DebugGrant` rows). Add a column (or store the raw `StarAmount`) if fans need gold.
- **Failed songs.** Nothing is recorded or committed unless `GameManager.EndSong` reaches `RecordScores` with a valid band score; quitting from the pause menu goes through `ForceQuitSong` and records nothing.
- **Dialogs.** `DialogManager` allows one dialog at a time and throws if a second is shown; queue or defer presentations.

### 0.3 Testing

- **Pure-logic tests:** there is no test assembly for game code in the Unity project (only a transitive `com.unity.test-framework`), and `Assembly-CSharp` cannot be referenced from an asmdef. YARG.Core runs NUnit through a normal SDK project (`YARG.Core/YARG.Core.UnitTests`, `dotnet test`). `dotnet` 10 is installed here with the NUnit packages cached. Set up: `Tests/YARG.Career.Tests` (net10.0, NUnit) references the assemblies Unity last compiled into `Library/ScriptAssemblies` (`Assembly-CSharp.dll`, `YARG.Core.Package.dll`); any Unity compile refreshes them, including `yarg-remote.sh build`. Run `dotnet test Tests/YARG.Career.Tests` (seconds). Only code that does not call into UnityEngine can be tested this way (`CareerEvaluation`, future fan/contract math). Tests: star-sum unlock and best-of-replays (`CareerEvaluationTests`), fan formula, best-result-only, gold, bonus half rate, fan-count unlocks with several tiers open, existing-style career unchanged, outcome preview, tier JSON (`CareerFansTests`). The project also references `Newtonsoft.Json` (NuGet) and `ZString` (from `Assets/Packages`, needed by `YargLogger` calls in content callbacks).
- **In-game:** `scripts/dev/yarg-remote.sh` (see its header). `start` now runs with isolated data in `$XDG_RUNTIME_DIR/yarg-dev-remote/data` (seeded from the dev folder's profiles, settings and song cache; only default careers), `start --shared-data` uses the dev folder, `wipe` deletes the test data. Career commands (`Assets/Script/Dev/DevRemote.Career.cs`):
  - `simulate [career=<index|guid|name>] [tier=<n> song=<n|all>] [stars=<0..5|gold>] [score=<n>] [percent=<0..100>] [fc=<bool>] [difficulty=<name>] [path=<score|grant>]` commits through `ScoreContainer.RecordScore` (GameRecord + PlayerScores + `CommitCareerCompletion` + `SyncProgress`) as the current non-bot players; without `tier=` it uses the career menu's selection (a song, or every song of a tier header). It refreshes the open career menu and prints the career's progress.
  - `career [career=...]` prints per tier: open/locked (needs N), star sum, best stars per song.
  - `scorescreen <same song/result options> [commit=true]` opens the score screen with a made-up result. Default: commits nothing and shows what the result *would* earn (fan line via `CareerSongOutcome.Preview`). `commit=true`: commits through the score path first, so the screen shows the real outcome and the menu presents unlocks on Continue.
  - `career` / `simulate` also print the save's fans, per-tier and per-song fans for fan careers, `G` for gold, and (simulate) the last commit's outcome (`fans 175 -> 300 (+125), unlocked ...`).
  - `start` (isolated data) installs the dev careers from `scripts/dev/careers/` (e.g. `career="Fan Test"`).
  - Unlock dialogs do not react to `nav Green`; close them with `click 0.5 0.746` at 1600x900.
  - Shared code: `Assets/Script/Dev/CareerSimulator.cs` (`SimulatedResult`, `CareerSimulator.TryCommit`, `ScoreScreenStatsFor`). The career menu's debug Grant Stars uses the same code with `path=grant` (completion row with `Source = DebugGrant`, no score records). Add new result fields (player count, failed song, ...) to `SimulatedResult` and its parser.

---

## 1. Bands (the save owner)

Today a save belongs to "whichever profiles happened to play". A band makes that explicit and gives us something to hang a name, fans and history on.

### Data
- [ ] `Bands` table: `Id` (Guid), `Name`, `Hometown`, `Genre` (free text or `Genrelizer.BaseGenre`), `CreatedAt`, `IsDeleted`.
- [ ] `BandMembers` table: `BandId`, `ProfileId`, `JoinedAt`.
- [ ] `CareerSaves.BandId` (nullable Guid). Null = legacy profile-set save, which keeps working.
- [ ] `CareerDatabase`: create/rename/soft-delete band, add/remove member, `GetBandsForProfile`, `GetOrCreateSave(careerId, version, bandId)`.
- [ ] `CareerContext`: add `BandId`. `CommitCareerCompletion` uses the band's save when set, otherwise the current profile-set lookup.

### Rules (proposed defaults, see open questions)
- [ ] A gig counts for the band if at least one member is playing. Non-member humans may sit in.
- [ ] Bots stay disallowed for now (matches the current career rule).
- [ ] One active save per (band, career). "New career" soft-deletes like today.

### UI
- [ ] Band select / create screen before the career list (name, hometown, genre, pick members from profiles).
- [ ] Show the active band in the career menu header.
- [ ] Migration path: offer "create a band from this save" for existing profile-set saves (optional, low priority).

---

## 2. Fans & Venues

### Test tooling (build first)
No controller is needed: fans, unlocks and contracts are all computed from saved completion rows, so tests write those rows directly.
- [x] Dev remote command `simulate`: commit a completion for the selected (or named) career song through the same path as a real score, with chosen stars and score. Also takes accuracy (`percent`), full combo (`fc`) and `difficulty` already. Still to add when needed: player count (contracts; today every current non-bot player is credited) and a failed/abandoned song (nothing credited).
- [x] Generalize the existing debug "Grant Stars" action to share that code (`CareerSimulator`, `path=grant`).
- [x] Score screen with made-up results: yes, `scorescreen` command. The score screen reads only `State.ScoreScreenStats` + `CurrentSong`, so "+fans"/contract lines must come through new fields there or a lookup of the committed completion (see 0.2).

### Content model
- [x] `UnlockType.FanCount`: tier unlocks when the save's fans >= `UnlockCriteria` (the save is the profile-set save today; the same evaluation serves a band-owned save later, since it only reads that save's completion rows).
- [x] Unlock against **total** fans rather than "previous regular tier", so several gigs can be open at once and the player picks a path. Tier 0 is always open; star/completion tiers keep their old rule.
- [x] Optional per-tier `FanMultiplier` (nullable float, times `BASE_FANS` = 100) overriding the venue's multiplier; default comes from `VenueSize`.
- [x] Optional per-tier `City` / `VenueName` strings for flavor (text only; shown above the description in the career header for the selected tier).
- [x] JSON: new fields are omitted when unset, old content loads unchanged, copy constructor copies them (NUnit `TierJsonRoundTripAndOldContent`).
- [x] `CareerPreset.Version` handling: not needed. Everything is derived from completion rows keyed by song id at evaluation time, so edited content just re-evaluates.
- **Opt-in:** a career uses fans when any tier uses `FanCount` (`CareerFans.UsesFans`); no explicit flag. Careers that don't opt in show no fan UI (fans are still computed in the evaluation, just never shown).

### Fan formula (first draft, tune in playtests)
Per song, take the **best** result only, so replaying improves your total but can't be farmed:

```
songFans = baseFans(VenueSize) * starFactor(bestStars) * (1 + contractBonus)
bandFans = sum of songFans over all completed songs (bonus tiers included? see open questions)
```

| VenueSize | baseFans |   | Stars | starFactor |
|---|---|---|---|---|
| Starter | 100 | | 1-2 | 0.25 |
| Small | 250 | | 3 | 0.5 |
| Medium | 600 | | 4 | 0.75 |
| Large | 1,500 | | 5 | 1.0 |
| Epic | 4,000 | | gold | 1.25 |

- [x] Put the numbers in one static table (`Assets/Script/Career/CareerFans.cs`, pure) so tuning is a one-file change. Venue multipliers 1 / 2.5 / 6 / 15 / 40 (NotSpecified = Starter), star factors as above (0 stars = 0), bonus tiers x0.5, rounded half away from zero. `contractBonus` not added yet (contracts step).
- [x] `CareerEvaluation`: `SongResult.Fans` + `BestIsGold`, `TierResult.Fans`, `CareerEvaluation.TotalFans` (named `TotalFans`, not `BandFans`: it is the save's total) + `UsesFans`, and `FanCount` unlock handling + `RemainingToUnlock` (decided after all tiers are summed). Existing star/completion results are unchanged (NUnit `ExistingStyleCareerIsUnaffected`; in game the Rock save evaluated identically before and after). Sticky strikethrough for songs in `FanCount` tiers: only at gold (the song can still earn fans until then).
- [x] Gold stars: new raw column `CareerSongCompletions.IsGold` (bool; `BandStars` still stores 5 for gold, so star unlocks are untouched). Written by `ScoreContainer.CommitCareerCompletion` (`gameRecord.BandStars == StarGold`) and the simulator; sqlite-net adds the column on start, old rows read as false. Verified against an existing test database.
- [x] `CareerSongOutcome` (pure + one commit helper): evaluation before/after a commit gives fans gained and newly unlocked tiers; `CareerSongOutcome.Last` carries it to the score screen. `SyncProgress` now returns its evaluation.

### UI
- [x] Fan total in the career header (second line under the career name); fans per tier (header: "14 / 15 · 300 fans") and per completed song ("Artist · 125 fans") in the list. Fan careers list every locked `FanCount` tier with its hint instead of stopping at the first locked tier (locked bonus tiers still hidden).
- [x] Locked tier text: "Needs 2,400 more fans".
- [x] Score screen: minimal version, reusing the existing status pill next to the band score: "+188 fans · Unlocked: Club Night", or "No new fans: only your best result counts". Shown only for a fan career's song when the score was saved. The full "New venue unlocked" presentation is the existing career-menu unlock dialog (`UnlockSeen`), which fires for fan-gated tiers on return to the menu. A richer score-screen panel would need prefab work (not done).
- [x] Lang strings in `en-US.json` (`Menu.Career.UnlockFans`, `HeaderFans`, `TierFans`, `SongFans`, `Venue`, `FansGained`, `FansNoGain`, `FansUnlocked`, editor enum `FanCount`).
- [x] Dev/test content: `scripts/dev/careers/Dev_Fan_Test.85c07510.json` ("Dev Fan Test": official setlist songs, Starter to Epic venues, one `FanMultiplier` override, a fan-gated bonus tier, two tiers that open together at 300 fans). `yarg-remote.sh start` copies it into the isolated test data only; it is not in the game's content or builds.

---

## 3. Gig contracts

A contract is an optional condition attached to a tier (the gig) or a single song, with a fan bonus when met. Failing a contract never blocks progress; you just miss the bonus.

### Content model
- [ ] `CareerContract` class: `Type`, `Parameter` (number), `BonusPercent`, optional `Text` override. Field `Contract` on `CareerTier` and `CareerSong` (song overrides tier).
- [ ] JSON round-trip, copy constructor, preset editor support.

### Phase A: conditions judged from one song's result (no gameplay changes)
- [ ] `MinStars` (e.g. 4 stars or better)
- [ ] `MinAccuracy` (band average note %)
- [ ] `FullCombo` (any one member / all members)
- [ ] `MinDifficulty` (every member on Hard or above)
- [ ] `NoFailOff` ("clean show": no-fail setting must be Off; read `SettingsManager.Settings.NoFail`)
- [ ] `FullBand` (at least N members playing)
- [ ] Contract evaluator: pure function `(contract, GameRecord, PlayerScoreRecords) -> met?`. Confirm which stats `PlayerScoreRecord` already carries (percent, notes hit/missed, combo). *Checked: percent, notes hit/missed, `IsFc`, difficulty, instrument; no max combo, no star power, no no-fail flag (see 0.2).*
- [ ] Store raw outcome on the completion row: `ContractType`, `ContractMet`. Fans use the best row per song, counting the bonus only if that row met the contract.

### Phase B: conditions that change the session
- [ ] **Forced modifiers / settings** for the gig (e.g. a `Modifier` flag set, song speed). Needs a hook where modifiers are applied at song start, and must restore the player's own settings afterwards.
- [ ] **Playlist gigs**: honor `CareerCompletionMode.FullTierPlaylist` by queueing the tier through the Setlist owner (#1758) with `CareerContext` carried per song. Needed for the next three.
- [ ] **Mystery setlist**: hide title/artist in the career list and loading screen until the song starts.
- [ ] **Make your own setlist**: pick N songs from the tier's pool before starting; the tier completes on N completions rather than all songs.
- [ ] **Encore**: hidden final song, revealed if the average stars across the set meet the bar. Needs a between-songs decision point in the playlist flow.
- [ ] Decide what quitting mid-playlist does (proposed: finished songs still count as single completions, contract not met).

### UI
- [ ] Contract badge + one-line text on tier/song rows ("Clean show: no-fail off, +25% fans").
- [ ] Contract shown on difficulty select / loading screen so nobody is surprised.
- [ ] Score screen: contract met / missed line.

---

## 4. Band identity

### Band sheet (new screen)
- [ ] Name, hometown, genre, founded date, members.
- [ ] Fan total and **rank title** derived from fans (Garage Band, Local Act, Club Regulars, Touring Band, Headliners, Arena Legends). Thresholds in the same tuning file as the fan table.
- [ ] Careers played, gigs played, total stars, contracts met.

### Per-member records
- [ ] Derived from each member's `PlayerScoreRecord`s linked to the band's completion rows (via `GameRecordId`): gigs played, instruments played, average accuracy, best streak, full combos.
- [ ] "Showmanship" (star power use) only if that stat is already recorded per player; otherwise defer. *Not recorded per player in the database (only in replays), so defer.*
- [ ] Decide whether stats need their own cached table or can be queried on demand (start with on-demand).

### Gig history log
- [ ] Chronological list from `CareerSongCompletions` joined to `GameRecord`: date, venue/tier, song, stars, fans gained, contract result.
- [ ] Note that only the **best** row counts for fans, but history shows every play.

### Flavor
- [ ] Hometown text hook: tiers whose `City` matches the band's hometown show a "Hometown show" tag (text only; bonus fans optional).

### Deferred within this section
- Convenience perks (roadie skip token, manager reveals a mystery song). Revisit once playlist gigs exist.

---

## 5. Content & authoring

- [ ] One default career built from the official setlist songs that uses fans, mixed venue sizes, and a handful of Phase A contracts. Keep existing default careers untouched.
- [ ] Preset editor: fields for `FanCount` unlock, venue/city text, contract type/parameter/bonus.
- [ ] Validation warnings for unreachable tiers (fan requirement higher than the fans obtainable before it).
- [ ] Document the JSON additions for custom career authors.

---

## 6. Suggested build order

1. **Groundwork** (section 0.1): branch, rebase, carry over debug grant. Done; test tooling (section 2) done too.
2. **Fans, no bands yet**: first the dev remote's `simulate` command (section 2, "Test tooling"), then `FanCount` unlock + evaluation + header/locked-tier text. Smallest playable slice; works with today's profile-set saves. Done (2026-11-15), see section 2.
3. **Bands**: tables, save ownership, select/create UI.
4. **Contracts Phase A** + score screen lines.
5. **Band sheet, ranks, history log.**
6. **Default career content + preset editor fields.**
7. **Contracts Phase B**: playlist gigs, then mystery, make-your-own, encore. Add the dev-only "bots count for career credit" switch here so a bot can play through playlist gigs.

Each step should leave existing careers working and be mergeable on its own.

---

## 7. Open questions

1. ~~Name.~~ **Decided:** it is called Career, front end and back end. New code goes under the existing `Career` names.
2. **Upstream or fork-only?** If we want this accepted upstream, steps 2 and 4 are the easiest to pitch as small PRs; bands are a bigger conversation with wyrdough.
3. **Who has to be present** for a band gig to count: any one member, a majority, or everyone?
4. **Bots as fill-in members?** Currently any bot blocks career credit.
5. ~~**Do bonus tiers earn fans?**~~ **Decided (step 2):** yes, at half rate (`CareerFans.BONUS_RATE`); their fans count toward the total like any other, and completing them is never required.
6. ~~**Do existing star-gated careers get fans too**~~ **Decided (step 2):** only careers that opt in (any `FanCount` tier). Others show no fan UI.
7. **Solo play**: is a one-person band fine, and do `FullBand`-style contracts need a solo-friendly alternative?

---

## Decided against

(nothing yet)
