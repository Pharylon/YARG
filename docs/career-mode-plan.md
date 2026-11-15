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
| Progress evaluation | `Assets/Script/Career/CareerEvaluation.cs`, `CareerProgressSnapshot.cs` | Pure function over raw rows. Nothing derived is stored. *Fixed in step 4:* it used to set the sticky `CareerSong.IsCompleted` flag (strikethrough) on the shared content object; the strikethrough now comes from `SongResult.Done` of the evaluation being shown, and the (upstream) field is no longer written or read |
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
- **How saves are resolved.** No save id travels with a song. `ScoreContainer.CommitCareerCompletion` takes the distinct `PlayerId`s of the recorded `PlayerScoreRecord`s and calls `CareerDatabase.GetOrCreateSave(careerId, version, profileIds)`, which finds the active save whose `CareerSaveProfiles` member set *equals* that set, or creates one. The menu (`CareerMenu.FindSaveId`) resolves the same way from `PlayerContainer.Players` that are not sitting out and not bots. So the save is per exact player set: if one more person joins, it is a different (new) save. Reset (hold Yellow) soft-deletes the save. *Since step 3 (bands): with an active band (`CareerBands.ActiveBandId`, carried as `CareerContext.BandId`) the save is the band's one active save for the career instead; all resolution goes through `CareerBands` (see section 1).*
- **How the view list is built.** `CareerMenu.CreateViewList()`: for each tier, a `TierViewType` header; unlocked tiers list their songs (`SongViewType`) only when expanded (uncommitted collapse work: one expanded tier at a time, kept in a static `_expandedTier`). The first locked tier is shown with its lock hint and the list stops there; bonus tiers that are locked are skipped. Progress comes from a cached `CareerEvaluation`; `ApplyProgress()` + `RequestViewListUpdate()` redraw it. New public `CareerMenu.RefreshProgress()` reloads from the database and presents unlocks (skipped while a dialog is open, so it is deferred, not lost).
- **How the score screen gets its data.** `GameManager.EndSong()` fills `GlobalVariables.State.ScoreScreenStats` (`ScoreScreenStats`: per-player `PlayerScoreCard { Player, Stats (BaseStats), IsHighScore, IsReplay }`, `BandScore`, `BandStars`, `MeanAverageOffset`, `ReplayInfo`), records the score, then loads `SceneIndex.Score`. `ScoreScreenMenu.OnEnable` only reads that struct and `State.CurrentSong`; it does not read the database or the career context. History replays build the same struct (`ReplayViewType`). So "+fans" / contract lines need either new fields on `ScoreScreenStats` or a read of the committed completion (e.g. remember the commit's id in state). *Step 2 chose a static: `CareerSongOutcome.Last`, set by every career commit (cleared at the start of `CommitCareerCompletion`) and matched against `State.CurrentCareer` by `CareerScoreScreen.FanLine()`; contract lines can ride on the same outcome.* After the score screen the menu scene reopens the career menu (`MenuManager._lastOpenMenu`), which re-evaluates progress and shows unlock dialogs.
- **Contracts (step 4).** `CareerContract` on `CareerTier`/`CareerSong` (song overrides tier, a song contract of type `None` cancels the tier's). `ScoreContainer.CommitCareerCompletion` judges the play with `CareerContracts.IsMet(contract, gameRecord, playerEntries, noFailOff)` and stores `ContractType`/`ContractMet` on the completion row. The no-fail setting is read live from `SettingsManager.Settings.NoFail` at commit time: toggling it mid-song, or un-failing, invalidates the score, so the value at commit is the play's. Players judged = every human `PlayerScoreRecord` of the play (band members and sit-ins). `CareerSongOutcome.Last` now also carries `Contract`/`ContractMet` for the score screen.
- **What per-player stats are stored.** `PlayerScoreRecord` (table `PlayerScores`, linked by `GameRecordId`): `PlayerId`, `Instrument`, `Difficulty`, `EnginePresetId`, `Score`, `Stars` (`StarAmount`), `NotesHit`, `NotesMissed`, `IsFc`, `IsReplay`, `Percent` (nullable, older rows lack it; use `GetPercent()`). **Not stored:** max combo/best streak, star power phrases/activations/time, overstrums, solo bonuses, modifiers, song speed per player. Those exist in memory on `BaseStats` (and in the replay file's `ReplayStats`) only. `GameRecord` (table `GameRecords`): date, song checksum/name/artist/charter, replay file name/checksum, `BandScore`, `BandStars` (`StarAmount`), `SongSpeed`, `PlayedWithReplay`, `HasBots`. Not stored: no-fail setting, modifiers, player count other than by counting `PlayerScores` rows.
- **Gold stars.** `GameRecord.BandStars` and `PlayerScoreRecord.Stars` are `StarAmount`, where `StarGold` (6) is distinct from `Star5`. `CareerSongCompletions.BandStars` is an int written with `GetStarCount()`, which maps gold to 5, so the completion row loses gold; it is recoverable only by joining `GameRecords` on `GameRecordId` (0 for `DebugGrant` rows). Add a column (or store the raw `StarAmount`) if fans need gold.
- **Failed songs.** Nothing is recorded or committed unless `GameManager.EndSong` reaches `RecordScores` with a valid band score; quitting from the pause menu goes through `ForceQuitSong` and records nothing.
- **Dialogs.** `DialogManager` allows one dialog at a time and throws if a second is shown; queue or defer presentations.
- **Which row counts for fans (step 5).** `CareerEvaluation.SongResult.CountedCompletionId` is the id of the completion row whose fans count (first of equal rows). The gig history uses it for its "counts" mark. Text-heavy screens can use the stock list dialog with plain text lines (`CareerBandSheetDialogs.AddLine`: a copy of the dialog's title TMP + `ContentSizeFitter` + `RuntimeNavigatable`, and `FollowSelection` to scroll with the controller).

### 0.3 Testing

- **Pure-logic tests:** there is no test assembly for game code in the Unity project (only a transitive `com.unity.test-framework`), and `Assembly-CSharp` cannot be referenced from an asmdef. YARG.Core runs NUnit through a normal SDK project (`YARG.Core/YARG.Core.UnitTests`, `dotnet test`). `dotnet` 10 is installed here with the NUnit packages cached. Set up: `Tests/YARG.Career.Tests` (net10.0, NUnit) references the assemblies Unity last compiled into `Library/ScriptAssemblies` (`Assembly-CSharp.dll`, `YARG.Core.Package.dll`); any Unity compile refreshes them, including `yarg-remote.sh build`. Run `dotnet test Tests/YARG.Career.Tests` (seconds). Only code that does not call into UnityEngine can be tested this way (`CareerEvaluation`, future fan/contract math). Tests: star-sum unlock and best-of-replays (`CareerEvaluationTests`), fan formula, best-result-only, gold, bonus half rate, fan-count unlocks with several tiers open, existing-style career unchanged, outcome preview, tier JSON (`CareerFansTests`). The project also references `Newtonsoft.Json` (NuGet) and `ZString` (from `Assets/Packages`, needed by `YargLogger` calls in content callbacks).
- **In-game:** `scripts/dev/yarg-remote.sh` (see its header). `start` now runs with isolated data in `$XDG_RUNTIME_DIR/yarg-dev-remote/data` (seeded from the dev folder's profiles, settings and song cache; only default careers), `start --shared-data` uses the dev folder, `wipe` deletes the test data. Career commands (`Assets/Script/Dev/DevRemote.Career.cs`):
  - `simulate [career=<index|guid|name>] [tier=<n> song=<n|all>] [stars=<0..5|gold>] [score=<n>] [percent=<0..100>] [fc=<bool>] [difficulty=<name>] [path=<score|grant>]` commits through `ScoreContainer.RecordScore` (GameRecord + PlayerScores + `CommitCareerCompletion` + `SyncProgress`) as the current non-bot players; without `tier=` it uses the career menu's selection (a song, or every song of a tier header). It refreshes the open career menu and prints the career's progress.
  - `career [career=...]` prints per tier: open/locked (needs N), star sum, best stars per song.
  - `scorescreen <same song/result options> [commit=true]` opens the score screen with a made-up result. Default: commits nothing and shows what the result *would* earn (fan line via `CareerSongOutcome.Preview`). `commit=true`: commits through the score path first, so the screen shows the real outcome and the menu presents unlocks on Continue.
  - `career` / `simulate` also print the save's fans, per-tier and per-song fans for fan careers, `G` for gold, and (simulate) the last commit's outcome (`fans 175 -> 300 (+125), unlocked ...`).
  - `start` (isolated data) installs the dev careers from `scripts/dev/careers/` (e.g. `career="Fan Test"`).
  - Unlock dialogs do not react to `nav Green`; close them with `click 0.5 0.746` at 1600x900.
  - Bands and dialogs (`Assets/Script/Dev/DevRemote.Bands.cs`): `band list|create name=.. [hometown=..] [genre=..] [members=a,b] [select=false]|select <band|none>|rename <band> to=..|members <band> members=..|delete <band>|adopt <band> career=..`; `profile create name=.. [player=true]`, `player add|remove <profile>`, `player sitout <profile> <bool>`, `players`; `dialog` (title, text field, buttons of the open dialog), `type <text>` (sets the open dialog's text field), `press <label>` (clicks the dialog or list button whose label matches). `career`/`simulate` print `band=` and use the active band's save; `state` prints the current career context's song and band. Profiles made with `profile create` are saved with the profiles and survive restarts of the test data.
  - The career list's first row is now the band row, so the first career is one `nav Down` away. The list wraps around, so count `nav Up` carefully.
  - The menu's idle dimmer (3 minutes without real input) used to darken screenshots; the remote now resets it on every command.
  - NUnit: `CareerBandsTests` (credit rule, band fans, and band/profile-set save resolution against an in-memory SQLite database using the game's own sqlite-net code and `Assets/Plugins/Sqlite3/Linux/x86_64/libsqlite3.so`, which the test project copies to its output).
  - Contracts (step 4): `simulate`/`scorescreen` take `fc=true|false|"A,B"`, `difficulty=<d>|"A:Hard,B:Expert"`, `players=<n|"A,B">`, `nofail=on|off`, `failed=true`. `career`/`simulate` mark each song's best stars with `+` (counted result met the contract) or `x` (missed), and print `contract <Type>(<param>, +N%) met|missed` for the last commit. Dev career "Dev Contract Test" (`scripts/dev/careers/Dev_Contract_Test.34d8752f.json`) has every Phase A type on tiers and songs, a song override, a `None` exemption, a `Text` override and a song without contract; all tiers open at 0 fans. `players=` without a band commits to that player set's save (not the one the menu shows); use a band to keep one save.
  - `scorescreen` must be started from the menu scene: run from the score screen, the old screen's `OnDisable` resets the state and the new one comes up empty (dev tool limitation). Continue (`nav Green`) back to the menu first.
  - NUnit: `CareerContractsTests` (judge per type, which row counts, strikethrough rule, JSON, completion row columns on in-memory SQLite). 59 tests in total.
  - Band identity (step 5, `DevRemote.Bands.cs`): `band sheet [<band>] [open=true]` prints the sheet's raw numbers (`fans= rank= next= careers= gigs= stars= contracts=N/M members=[name:gigs,instruments,accuracy,fc] history=[#rowId:career/tNsN:stars:fans(* = counts):contract(+ met, x missed):rec=gameRecordId]`) and then the text lines the dialog shows; `band history ...` the same for the history; `open=true` also opens the dialog. Default: the active band. `band edit <band> [hometown=..] [genre=..]`. `simulate`/`scorescreen` now also take per-player `percent="A:90,B:75"` and `instrument=<Instrument>|"A:FiveFretGuitar,B:FourLaneDrums"`. The sheet/history dialogs are closed with `press Close` and switch with `press "Gig History"` / `press "Band Sheet"`; `nav Down` moves through the lines and scrolls.
  - NUnit: `CareerBandSheetTests` (ranks, hometown match, sheet totals and history, which row counts, member records, empty band, removed songs, DB load with reset/uninstalled careers, hometown/genre edits). 71 tests in total.
  - Shared code: `Assets/Script/Dev/CareerSimulator.cs` (`SimulatedResult`, `CareerSimulator.TryCommit`, `ScoreScreenStatsFor`). The career menu's debug Grant Stars uses the same code with `path=grant` (completion row with `Source = DebugGrant`, no score records). Add new result fields (player count, failed song, ...) to `SimulatedResult` and its parser.

---

## 1. Bands (the save owner)

Today a save belongs to "whichever profiles happened to play". A band makes that explicit and gives us something to hang a name, fans and history on.

### Data
- [x] `Bands` table (`CareerBand`, `Assets/Script/Scores/Career/CareerBand.cs`): `Id` (Guid), `Name`, `Hometown`, `Genre` (free text), `CreatedAt`, `IsDeleted`.
- [x] `BandMembers` table (`CareerBandMember`): `Id`, `BandId`, `ProfileId`, `JoinedAt`. Removing a member deletes the row.
- [x] `CareerSaves.BandId` (nullable Guid, indexed). Null = legacy profile-set save, which keeps working: sqlite-net adds the column on start, old rows read null (verified on the step-2 test database: saves 1 and 2 resolved and evaluated exactly as before). A band save has **no** `CareerSaveProfiles` rows; `FindActiveSaveForProfiles` also filters `BandId IS NULL`, so a band save (or an adopted one) is never picked up as a player set's save.
- [x] `CareerDatabase` (new partial `CareerDatabase.Bands.cs`): `CreateBand`, `RenameBand`, `SoftDeleteBand`, `GetBand`, `GetBands`, `GetBandsForProfile`, `GetBandMembers`, `AddBandMember` (idempotent), `RemoveBandMember`, `FindActiveSaveForBand`, `GetOrCreateSave(careerId, version, bandId)`, `GetActiveSavesForBand`, `AdoptSave`. The only edits to `CareerDatabase.cs` itself: `partial`, two `CreateTable` calls, the `BandId IS NULL` filter.
- [x] `CareerContext.BandId` (Guid?), set from the active band by `SongViewType.ViewClick` (and by the simulator's `ContextFor`). `ScoreContainer.CommitCareerCompletion` decides credit with `CareerBands.Credit(...)` and resolves the save with `CareerBands.GetOrCreateSaveId(...)`: the band's save when `BandId` is set, otherwise the profile-set lookup as before.
- **Save resolution** lives in `Assets/Script/Career/CareerBands.cs` (`Credit`, `CurrentCredit`, `FindSaveId`, `GetOrCreateSaveId`, `BandFans`, `ActiveBandId`); the rule itself is pure in `CareerCredit.cs`. `CareerMenu.FindSaveId`, the score path and `CareerSimulator` all go through it, so menu, score path and dev commands always agree.
- **Active band:** `CareerBands.ActiveBandId`. *Since step 4 it is remembered across starts:* stored by ID in the settings file (hidden setting `CareerActiveBandId`, saved whenever it changes) and restored on the first read once settings and the score database are loaded, only if that band still exists and is not deleted; otherwise it starts as "No band" and the setting is cleared. Verified: selected band survives a restart; a band soft-deleted behind the game's back comes back as "No band" with the setting nulled.

### Rules (proposed defaults, see open questions)
- [x] A gig counts for the band if at least one member is among the human players. Non-member humans may sit in; they do not change which save is used (`CareerCredit.Decide`, NUnit `BandCountsWhenOneMemberPlaysAndOthersMaySitIn`, `SitInDoesNotChangeTheBandSave`; in game: band with Bob only and New Profile playing committed nothing, after `player add Bob` the result landed on the band's save). With no member playing, the career menu still shows the band's progress, with "(no member is playing, results will not count)" after the band name.
- [x] Bots stay disallowed (any bot among the players blocks credit, band or not; `BotsBlockCreditWithOrWithoutBand`).
- [x] One active save per (band, career). "New career" (hold Yellow) soft-deletes the band's save like today and the next result creates a fresh one (`ResetGivesTheBandAFreshSave`; the in-game reset itself was not re-driven, it is a hold action the remote cannot send, and its code is unchanged: it soft-deletes whatever save the menu shows).
- [x] A deleted band (`IsDeleted`) blocks credit (`BandMissing`) and is cleared as the active band.

### Band fans
- [x] A band's fans = sum of the fan totals of its active saves, careers that use fans only (`CareerFans.BandFans`, pure; `CareerBands.BandFans` evaluates each save). Saves of careers no longer installed are skipped; reset (soft-deleted) saves no longer count. Derived, never stored. Shown on the career list's band row.

### UI
- [x] Band select / create, plain and built from stock pieces, reached from the career list: its first row is "Band: <name>" (or "No band: play as the current players"); the left panel shows hometown, genre, members and band fans. Clicking it opens a list dialog: every band (active one marked), "No band", "New band...", and for the active band "Rename", "Members of", "Delete". New band: rename dialog for name, hometown and genre (both optional), then a member picker (list dialog of `[x]`/`[  ]` toggles over the non-bot profiles, preset to the current human players). Delete uses the stock confirm-delete dialog (type the band's name). Code: `Menu/Career/CareerBandDialogs.cs`, `ViewTypes/BandViewType.cs`. The list dialog's list buttons get runtime navigatables, so a controller can reach them (the stock list dialog does not do this).
- [x] Active band in the career menu header: second line under the career name, "Band: The Testers · 613 fans" (fans only for fan careers). No band: header unchanged.
- [x] Migration path: after creating a band, if exactly its members already have profile-set saves, a dialog offers to move them to the band ("Bring It" / "Start Fresh"); `AdoptSave` sets `BandId` on the save. Verified in game (Carol's Rock save moved to "Carol Trio"; afterwards Carol without a band has no Rock save).
- Not done: band icon/art (the band row has an empty icon box). Editing hometown/genre and the band sheet: done in step 5 (section 4).
- ~~Known: the sticky `CareerSong.IsCompleted` strikethrough leaks between band and no-band views.~~ Fixed in step 4 (`SongResult.Done`, NUnit `DoneDoesNotCarryOverToAnotherSave`; in game: Rock solo save shows two struck songs, the same career as The Testers right after shows none).

---

## 2. Fans & Venues

### Test tooling (build first)
No controller is needed: fans, unlocks and contracts are all computed from saved completion rows, so tests write those rows directly.
- [x] Dev remote command `simulate`: commit a completion for the selected (or named) career song through the same path as a real score, with chosen stars and score. Also takes accuracy (`percent`), full combo (`fc=true|false|"Alice,Bob"` per player), `difficulty` (all, or `"Alice:Hard,Bob:Expert"`), player count (`players=<n|names>`), no-fail (`nofail=on|off`, for that commit only) and a failed/abandoned song (`failed=true`: nothing recorded or credited). Step 4.
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

- [x] Put the numbers in one static table (`Assets/Script/Career/CareerFans.cs`, pure) so tuning is a one-file change. Venue multipliers 1 / 2.5 / 6 / 15 / 40 (NotSpecified = Starter), star factors as above (0 stars = 0), bonus tiers x0.5, rounded half away from zero. `contractBonus` added in step 4 (`SongFans(tier, stars, bonusPercent)`, multiplied as `* (100 + bonus) / 100` so +10% gives exact halves).
- [x] `CareerEvaluation`: `SongResult.Fans` + `BestIsGold`, `TierResult.Fans`, `CareerEvaluation.TotalFans` (named `TotalFans`, not `BandFans`: it is the save's total) + `UsesFans`, and `FanCount` unlock handling + `RemainingToUnlock` (decided after all tiers are summed). Existing star/completion results are unchanged (NUnit `ExistingStyleCareerIsUnaffected`; in game the Rock save evaluated identically before and after). Strikethrough (now `SongResult.Done`, step 4): in a career that uses fans, a song is struck through only when its fans are at the most it can earn, i.e. gold and, if it has a contract with a bonus, that contract met; this applies to every tier of a fan career, including a star-count tier 0 (before step 4 those struck at 5 stars though gold still earned fans). Careers without fans keep the old rule (completion tiers: completed; star tiers: 5 stars).
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
- [x] `CareerContract` class (`Assets/Script/Career/CareerContract.cs`): `Type` (`CareerContractType`, string enum in JSON), `Parameter` (float), `BonusPercent` (int), optional `Text` override. Field `Contract` on `CareerTier` and `CareerSong` (song overrides tier; a song contract of type `None` exempts the song from its tier's). Rules in `CareerContracts` (pure).
- [x] JSON round-trip (omitted when unset, old content loads with no contract), copy constructors (deep copy). NUnit `ContractJsonRoundTripAndOldContent`.
- [ ] Preset editor support: not done (build order step 6). Dev content is hand-written JSON (`scripts/dev/careers/Dev_Contract_Test.34d8752f.json`).

Parameter meanings: `MinStars` 1..5, 6 = gold; `MinAccuracy` percent 0..100; `FullCombo` 0 = every player, N >= 1 = at least N players (1 = any one); `MinDifficulty` a `YARG.Core.Difficulty` value (3 = Hard, 4 = Expert); `NoFailOff` unused; `FullBand` minimum number of human players.

### Phase A: conditions judged from one song's result (no gameplay changes)
All judged by `CareerContracts.IsMet(contract, GameRecord, PlayerScoreRecords, bool? noFailOff)` over every human player of the play (members and sit-ins). Missing facts never meet a contract. NUnit `CareerContractsTests` (met and missed for each, solo, edge cases); in game each type was met and missed through `simulate` on "Dev Contract Test" (2026-11-15).
- [x] `MinStars` (e.g. 4 stars or better; 6 = gold, judged from `GameRecord.BandStars`)
- [x] `MinAccuracy` (average of the players' note percent; players with no notes are left out; nobody with notes = not met; old rows without `Percent` use hit/total)
- [x] `FullCombo` (parameter 0 = every player, N = at least N players; a player with no notes cannot full combo and does not hold back "every player")
- [x] `MinDifficulty` (every player at or above the parameter; no players = not met)
- [x] `NoFailOff` ("clean show": read live from `SettingsManager.Settings.NoFail` at commit, see 0.2; unknown = not met)
- [x] `FullBand` (at least N distinct human players)
- [x] Contract evaluator: pure, as above. Raw facts: no-fail is carried from the live setting at commit (not stored separately); everything else comes from the `GameRecord`/`PlayerScoreRecord`s being recorded.
- [x] Store raw outcome on the completion row: `CareerSongCompletions.ContractType` (nullable, null = no contract) and `ContractMet` (sqlite-net adds both columns on start; old rows read null/false; verified on the existing test database). Debug grants and `simulate path=grant` judge the made-up result the same way.
- [x] **Which row counts for fans:** per song, every completion row is valued at `SongFans(tier, its stars or gold, bonus if that row met the song's current contract)` and the highest value counts; on a tie the row that met the contract counts. So when the best-star row missed the contract and a lower-star row met it, the lower-star row counts if (and only if) its bonus makes it worth more (Large venue, +50%: 5 stars missed = 1,500, 4 stars met = 1,688, counts; at +25%: 1,406, the 5-star row counts). Stars for star unlocks still use the best stars. A row counts as having met the contract only if its stored `ContractType` equals the song's current contract type (content changed to another type: old results earn no bonus; a changed parameter is not re-judged). NUnit `TheResultWorthMoreFansCounts`, `ChangedContractTypeDropsTheOldResult`.
- [x] Failing a contract never blocks progress (it only withholds the bonus; NUnit `MissingAContractNeverBlocksProgress`).
- Solo play (open question 7): `FullBand` and `FullCombo` with N >= 2 cannot be met solo; nothing is blocked, the bonus is just out of reach. Authors can give a song its own contract or `None`. No automatic solo alternative.

### Phase B: conditions that change the session
- [ ] **Forced modifiers / settings** for the gig (e.g. a `Modifier` flag set, song speed). Needs a hook where modifiers are applied at song start, and must restore the player's own settings afterwards.
- [ ] **Playlist gigs**: honor `CareerCompletionMode.FullTierPlaylist` by queueing the tier through the Setlist owner (#1758) with `CareerContext` carried per song. Needed for the next three.
- [ ] **Mystery setlist**: hide title/artist in the career list and loading screen until the song starts.
- [ ] **Make your own setlist**: pick N songs from the tier's pool before starting; the tier completes on N completions rather than all songs.
- [ ] **Encore**: hidden final song, revealed if the average stars across the set meet the bar. Needs a between-songs decision point in the playlist flow.
- [ ] Decide what quitting mid-playlist does (proposed: finished songs still count as single completions, contract not met).

### UI
- [x] One-line text on rows (`Menu/Career/CareerContractText.cs`, lang `Menu.Career.Contract.*`): tier header secondary text "13 / 15 · 308 fans · Clean show: no-fail off, +25% fans"; a song with its own contract shows it after the artist; a song exempt from its tier's shows "no contract"; a song whose counted result met its contract shows "contract met". Long lines are cut with an ellipsis in the row, so the selected row's full contract is also shown bold in the left header ("Contract: Crowd pleaser: 4 stars or better, +50% fans"). The fan bonus part is left out for careers that do not use fans.
- [x] Contract shown on the difficulty (instrument) select, on the line above the song's loading phrase ("Contract: ..."); one guarded line in `DifficultySelectMenu.OnEnable`. Loading screen not touched.
- [x] Score screen: the existing status pill now reads "+313 fans · Contract met: Clean show: no-fail off (+25% fans)" or "+188 fans · Contract missed: Clean show: no-fail off" (`CareerScoreScreen.StatusLine`, was `FanLine`). Careers without fans but with a contract show only the contract part. A song without a contract shows the fan part only, as before; default careers show no pill.

---

## 4. Band identity

Step 5 (2026-11-15). All of it is derived on demand by `CareerBandSheet` (`Assets/Script/Career/CareerBandSheet.cs`, pure `Build` + a `Load` that only runs three thin queries: the band's active saves' snapshots, `CareerDatabase.GetBandGameRecords`, `GetBandPlayerScores`). No new table, no new column. **Scope:** the band's *active* saves of *installed* careers, the same saves its fans come from; a reset career (soft-deleted save) leaves the sheet and the history. NUnit `CareerBandSheetTests` (ranks, totals, which row counts, member records, empty band, removed songs, DB load against in-memory SQLite incl. reset/uninstalled, hometown/genre edits).

### Band sheet (new screen)
- [x] Name (dialog title), hometown, genre, founded date (`CreatedAt`), members. UI: `Menu/Career/CareerBandSheetDialogs.cs`, the stock list dialog with plain text lines in its scrolling list (copies of the dialog's title text, each with a `RuntimeNavigatable` so a controller can move through them; the dialog follows the selection, which the stock list dialog does not). Reached from the band dialog ("Band sheet: X", "Gig history of X" for the active band); the sheet has "Gig History" / the history has "Band Sheet" buttons to switch. No new scene or prefab.
- [x] Fan total and **rank title** derived from fans: `CareerRank` + `CareerFans.Rank/NextRank/RankFans` in `CareerFans.cs`. Thresholds 0 / 500 / 2,000 / 7,500 / 20,000 / 50,000 (Garage Band, Local Act, Club Regulars, Touring Band, Headliners, Arena Legends; a full gold run of "Dev Fan Test" is about 25,000). Sheet line: "Local Act · 600 fans · 1,400 more fans to Club Regulars". The band row in the career list also shows it: "... · 600 fans (Local Act)".
- [x] Careers played (saves with at least one gig), gigs played (every completion row, replays included), total stars (best stars per song summed over all careers, gold = 5, bonus tiers included), contracts met ("N of M": stored `ContractMet` over plays that had a contract; the raw outcome of each play, not re-judged against changed content).

### Per-member records
- [x] Derived from each current member's `PlayerScoreRecord`s on the game records of the band's completion rows: gigs played (gigs with a score row of theirs), instruments played (with counts, most played first), average accuracy (mean of `GetPercent()` over their gigs with notes), full combos (gigs with notes). Sit-ins are not listed; former members are not listed. Debug grants (`GameRecordId` 0) have no score rows, so they count as band gigs but not member gigs.
- **Best streak: left out.** Max combo is not stored per player (only in replays, see 0.2). Would need a new raw column on `PlayerScores` (upstream table) or reading replay files.
- [ ] "Showmanship" (star power use): deferred, not recorded per player in the database (only in replays).
- [x] Decided: on demand, no cached table. One sheet load = one snapshot per active save + two joins.

### Gig history log
- [x] List from `CareerSongCompletions` joined to `GameRecord`, **newest first** (as the History menu): song and artist (from the game record; from the library for grants), date, career, tier (venue, city), "Hometown show" tag, stars ("gold stars" for gold), the fans that result is worth (contract bonus included when the row met the song's current contract, same valuation as the evaluation), contract met/missed (stored outcome; condition text when the content still has that contract type).
- [x] Every play is listed; the row that counts for fans is marked "counts" (`SongResult.CountedCompletionId`, set by `CareerEvaluation.ApplyBestFans`; the first of equal rows), with a one-line note at the top. Fan careers only.

### Flavor
- [x] Hometown text hook: `CareerBandSheet.IsHometown(city, hometown)` (both set, trimmed, case-insensitive). Tier header rows get "Hometown show · ..." (any career with `City`), the selected tier's venue line in the career header gets "· Hometown show" (fan careers, where that line exists), and history rows show it. No bonus fans.
- [x] Editing hometown and genre after creation: "Hometown of X..." / "Genre of X..." in the band dialog (rename dialog prefilled with the current value; confirming an empty field clears it). `CareerDatabase.SetBandHometown/SetBandGenre`.

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
3. **Bands**: tables, save ownership, select/create UI. Done (2026-11-15), see section 1.
4. **Contracts Phase A** + score screen lines. Done (2026-11-15), see section 3; also fixed the strikethrough leak and made the active band persist.
5. **Band sheet, ranks, history log.** Done (2026-11-15), see section 4.
6. **Default career content + preset editor fields.**
7. **Contracts Phase B**: playlist gigs, then mystery, make-your-own, encore. Add the dev-only "bots count for career credit" switch here so a bot can play through playlist gigs.

Each step should leave existing careers working and be mergeable on its own.

---

## 7. Open questions

1. ~~Name.~~ **Decided:** it is called Career, front end and back end. New code goes under the existing `Career` names.
2. **Upstream or fork-only?** If we want this accepted upstream, steps 2 and 4 are the easiest to pitch as small PRs; bands are a bigger conversation with wyrdough.
3. **Who has to be present** for a band gig to count: any one member, a majority, or everyone? *Implemented default (step 3): any one member; non-members may sit in. One place to change: `CareerCredit.Decide`.*
4. **Bots as fill-in members?** Currently any bot blocks career credit.
5. ~~**Do bonus tiers earn fans?**~~ **Decided (step 2):** yes, at half rate (`CareerFans.BONUS_RATE`); their fans count toward the total like any other, and completing them is never required.
6. ~~**Do existing star-gated careers get fans too**~~ **Decided (step 2):** only careers that opt in (any `FanCount` tier). Others show no fan UI.
7. **Solo play**: is a one-person band fine, and do `FullBand`-style contracts need a solo-friendly alternative? *One-person bands work (step 3). Contracts (step 4): no automatic alternative; a solo player simply cannot earn a `FullBand`/multi-player `FullCombo` bonus, which never blocks anything. Still open whether content should provide solo variants.*

---

## Decided against

(nothing yet)
