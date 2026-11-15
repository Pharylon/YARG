#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Career;
using YARG.Core;
using YARG.Core.Engine;
using YARG.Core.Engine.Drums;
using YARG.Core.Engine.Guitar;
using YARG.Core.Engine.Keys;
using YARG.Core.Engine.Vocals;
using YARG.Core.Game;
using YARG.Core.Song;
using YARG.Gameplay.HUD;
using YARG.Menu.ScoreScreen;
using YARG.Player;
using YARG.Scores;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG.Dev
{
    /// <summary>
    /// How a simulated result reaches the database.
    /// </summary>
    public enum SimulationPath
    {
        /// <summary>
        /// As a finished song: <see cref="ScoreContainer.RecordScore"/> writes the GameRecord and
        /// PlayerScores rows, then commits the career completion and syncs progress exactly as after
        /// gameplay. Needs the song in the library.
        /// </summary>
        Score,

        /// <summary>
        /// A completion row only (<see cref="CareerCompletionSource.DebugGrant"/>, no score records),
        /// then the same progress sync. Leaves the score history and high scores alone.
        /// </summary>
        Grant,
    }

    /// <summary>
    /// A made-up song result. Add fields here as career features need them (failed song, player
    /// count, ...); every caller shares this one description.
    /// </summary>
    public class SimulatedResult
    {
        public SimulationPath Path = SimulationPath.Score;

        /// <summary>Band stars, 0 to 5, or 6 for gold.</summary>
        public int Stars = 5;

        /// <summary>Band score. Defaults to a made-up value that grows with the stars.</summary>
        public int? Score;

        /// <summary>Every player's accuracy, 0 to 1.</summary>
        public float Percent = 1f;

        /// <summary>Whether every player full-combos. Defaults to a perfect accuracy.</summary>
        public bool? FullCombo;

        /// <summary>
        /// Only these players (profile names) full-combo; the others do not. Overrides
        /// <see cref="FullCombo"/>.
        /// </summary>
        public HashSet<string> FullComboPlayers;

        /// <summary>Every player's difficulty. Defaults to each profile's current difficulty.</summary>
        public Difficulty? Difficulty;

        /// <summary>Per-player difficulty by profile name, over <see cref="Difficulty"/>.</summary>
        public Dictionary<string, Difficulty> PlayerDifficulty;

        /// <summary>
        /// Who plays: the first N current (non-bot) players, or the named profiles among them. Null:
        /// every current player.
        /// </summary>
        public int? PlayerCount;
        public List<string> PlayerNames;

        /// <summary>
        /// Whether no-fail is on for the play. Null: the game's current setting (the score path reads it
        /// when it commits). Set, the setting is changed for the commit only.
        /// </summary>
        public bool? NoFail;

        /// <summary>
        /// The song is failed or abandoned: like quitting from the pause menu (GameManager.ForceQuitSong),
        /// nothing is recorded or credited.
        /// </summary>
        public bool Failed;

        public const int TOTAL_NOTES = 1000;

        public int BandScore => Score ?? Math.Max(0, Stars) * 50_000;
        public StarAmount BandStars => StarAmountHelper.GetStarsFromInt(Math.Clamp(Stars, 0, 6));
        public int NotesHit => (int) Math.Round(Math.Clamp(Percent, 0f, 1f) * TOTAL_NOTES);
        public int NotesMissed => TOTAL_NOTES - NotesHit;
        public bool IsFullCombo => FullCombo ?? NotesMissed == 0;

        /// <summary>Per-player accuracy (0 to 1) by profile name, over <see cref="Percent"/>.</summary>
        public Dictionary<string, float> PlayerPercent;

        /// <summary>Every player's instrument. Defaults to each profile's current instrument.</summary>
        public Instrument? Instrument;

        /// <summary>Per-player instrument by profile name, over <see cref="Instrument"/>.</summary>
        public Dictionary<string, Instrument> PlayerInstrument;

        /// <summary>
        /// How the song is played (score path): on its own, or as a song of a show
        /// (<see cref="CareerCompletionSource.FullTierPlaylist"/>, see <see cref="CareerSimulator.TryCommitShow"/>).
        /// </summary>
        public CareerCompletionSource Source = CareerCompletionSource.SingleSong;

        public float PercentFor(YargPlayer player)
        {
            return PlayerPercent is not null && PlayerPercent.TryGetValue(player.Profile.Name, out var own)
                ? Math.Clamp(own, 0f, 1f)
                : Math.Clamp(Percent, 0f, 1f);
        }

        public int NotesHitFor(YargPlayer player) => (int) Math.Round(PercentFor(player) * TOTAL_NOTES);

        public Instrument InstrumentFor(YargPlayer player)
        {
            return PlayerInstrument is not null && PlayerInstrument.TryGetValue(player.Profile.Name, out var own)
                ? own
                : Instrument ?? player.Profile.CurrentInstrument;
        }

        public bool IsFullComboFor(YargPlayer player)
        {
            if (FullComboPlayers is not null)
            {
                return FullComboPlayers.Contains(player.Profile.Name);
            }

            return FullCombo ?? NotesHitFor(player) == TOTAL_NOTES;
        }

        public Difficulty DifficultyFor(YargPlayer player)
        {
            return PlayerDifficulty is not null && PlayerDifficulty.TryGetValue(player.Profile.Name, out var own)
                ? own
                : Difficulty ?? player.Profile.CurrentDifficulty;
        }

        /// <summary>
        /// The players of this result among <paramref name="current"/> (see <see cref="PlayerCount"/>).
        /// </summary>
        public List<YargPlayer> Choose(List<YargPlayer> current)
        {
            if (PlayerNames is not null)
            {
                return current.Where(player => PlayerNames.Contains(player.Profile.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            }

            return PlayerCount is { } count ? current.Take(Math.Max(0, count)).ToList() : current;
        }
    }

    /// <summary>
    /// Commits made-up career results through the same code a real score goes through, so career
    /// progress can be tested without playing. Used by the dev remote's <c>simulate</c> command and the
    /// career menu's debug "Grant Stars" action.
    /// </summary>
    public static class CareerSimulator
    {
        /// <summary>
        /// The players a career result is credited to: everyone taking part, minus bots. The same set
        /// the career menu resolves its save from.
        /// </summary>
        public static List<YargPlayer> CareerPlayers()
        {
            return PlayerContainer.Players
                .Where(player => !player.SittingOut && (!player.Profile.IsBot || CareerBotCredit.Enabled))
                .ToList();
        }

        public static IEnumerable<CareerPreset> AllCareers()
        {
            return CustomContentManager.Careers.DefaultPresets.Concat(CustomContentManager.Careers.CustomPresets);
        }

        /// <summary>
        /// The context a song started from the career menu carries (see <c>SongViewType.ViewClick</c>).
        /// </summary>
        public static CareerContext ContextFor(CareerPreset career, int tierIndex, int songIndex,
            CareerCompletionSource source = CareerCompletionSource.SingleSong)
        {
            var tier = career.Tiers[tierIndex];
            return new CareerContext
            {
                CareerId = career.Id,
                TierId = tier.Id,
                TierIndex = tierIndex,
                VenueHint = tier.VenueHint,
                CareerSongId = tier.Songs[songIndex].Id,
                SongIndex = songIndex,
                BandId = CareerBands.ActiveBandId,
                Source = source,
            };
        }

        /// <summary>
        /// A whole show of a tier played as a set (<see cref="CareerSetlists.IsShow"/>): the result for every
        /// song of the set in play order, each committed on its own as the score path does after each song
        /// of a real show (<see cref="CareerCompletionSource.FullTierPlaylist"/>). Returns the songs committed.
        /// </summary>
        public static bool TryCommitShow(CareerPreset career, int tierIndex, SimulatedResult result,
            IReadOnlyCollection<int> picks, out List<int> played, out bool encore, out string error)
        {
            played = new List<int>();
            encore = false;
            var tier = career.Tiers[tierIndex];
            if (!CareerSetlists.IsShow(tier))
            {
                error = $"tier {tierIndex} is not played as a show (CompletionMode {tier.CompletionMode})";
                return false;
            }

            List<int> set;
            if (picks is not null)
            {
                set = CareerSetlists.ShowSongIndexes(tier, picks);
                if (set is null)
                {
                    error = $"picks must be {CareerSetlists.RequiredSongs(tier)} different songs of the set " +
                        $"[{string.Join(",", CareerSetlists.SetSongIndexes(tier))}]";
                    return false;
                }
            }
            else if (CareerSetlists.PicksSongs(tier))
            {
                error = $"the tier is a make-your-own setlist: give picks= ({CareerSetlists.RequiredSongs(tier)} songs)";
                return false;
            }
            else
            {
                set = CareerSetlists.ShowSongIndexes(tier);
            }

            result.Source = CareerCompletionSource.FullTierPlaylist;
            var setStars = new List<int>();
            foreach (var songIndex in set)
            {
                if (!TryCommit(career, tierIndex, songIndex, result, out error))
                {
                    return false;
                }

                played.Add(songIndex);
                setStars.Add(result.Failed ? 0 : Math.Min(result.Stars, 5));
            }

            // The between-songs decision a real show makes after the set's last song (see CareerShow).
            encore = !result.Failed && CareerSetlists.EncoreEarned(tier, setStars);
            if (encore)
            {
                foreach (var songIndex in CareerSetlists.EncoreSongIndexes(tier))
                {
                    if (!TryCommit(career, tierIndex, songIndex, result, out error))
                    {
                        return false;
                    }

                    played.Add(songIndex);
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// The active save for this career of the active band, or (no band) of the current players; 0 if
        /// there is none yet. The same save the career menu shows.
        /// </summary>
        public static int FindSaveId(CareerPreset career)
        {
            return CareerBands.FindSaveId(ScoreContainer.Careers, career, CareerBands.CurrentCredit(ScoreContainer.Careers));
        }

        /// <summary>
        /// Commit one simulated result for a career song. Returns false with a reason if nothing was
        /// committed.
        /// </summary>
        public static bool TryCommit(CareerPreset career, int tierIndex, int songIndex, SimulatedResult result,
            out string error)
        {
            var careers = ScoreContainer.Careers;
            if (careers is null)
            {
                error = "the score database is not initialized";
                return false;
            }

            if (tierIndex < 0 || tierIndex >= career.Tiers.Count)
            {
                error = $"tier {tierIndex} is out of range (0..{career.Tiers.Count - 1})";
                return false;
            }

            var tier = career.Tiers[tierIndex];
            if (songIndex < 0 || songIndex >= tier.Songs.Length)
            {
                error = $"song {songIndex} is out of range (0..{tier.Songs.Length - 1}) in tier {tierIndex}";
                return false;
            }

            var players = result.Choose(CareerPlayers());
            if (players.Count == 0)
            {
                error = "no human players are taking part";
                return false;
            }

            if (result.Failed)
            {
                // Quitting a failed song records nothing (GameManager.ForceQuitSong), so nothing reaches
                // the career either.
                CareerSongOutcome.Last = null;
                error = null;
                return true;
            }

            // The score path decides credit itself (and is checked below); a grant has to decide here.
            var credit = CareerBands.Credit(careers, CareerBands.ActiveBand(careers)?.Id,
                players.Select(player => player.Profile.Id), false);
            if (result.Path == SimulationPath.Grant && !credit.Counts)
            {
                error = $"the result would not count ({credit.Block})";
                return false;
            }

            // The score path reads the no-fail setting when it commits.
            var noFail = SettingsManager.Settings.NoFail;
            var previousNoFail = noFail.Value;
            if (result.NoFail is { } on)
            {
                noFail.SetValueWithoutNotify(on ? NoFailMode.On : NoFailMode.Off);
            }

            try
            {
                return result.Path == SimulationPath.Grant
                    ? Grant(careers, career, tierIndex, songIndex, result, players, credit, out error)
                    : RecordScore(careers, career, tierIndex, songIndex, result, players, out error);
            }
            finally
            {
                noFail.SetValueWithoutNotify(previousNoFail);
            }
        }

        /// <summary>
        /// The score rows a played song would record for this result (see GameManager.RecordScores).
        /// </summary>
        public static (GameRecord record, List<PlayerScoreRecord> players) ScoreRecordsFor(SongEntry entry,
            SimulatedResult result, List<YargPlayer> players)
        {
            var playerEntries = players.Select(player => new PlayerScoreRecord
            {
                PlayerId = player.Profile.Id,
                Instrument = result.InstrumentFor(player),
                Difficulty = result.DifficultyFor(player),
                EnginePresetId = player.Profile.EnginePreset,
                Score = result.BandScore / Math.Max(1, players.Count),
                Stars = result.BandStars,
                NotesHit = result.NotesHitFor(player),
                NotesMissed = SimulatedResult.TOTAL_NOTES - result.NotesHitFor(player),
                IsFc = result.IsFullComboFor(player),
                IsReplay = false,
                Percent = (float) result.NotesHitFor(player) / SimulatedResult.TOTAL_NOTES,
            }).ToList();

            var gameRecord = new GameRecord
            {
                Date = DateTime.Now,
                SongChecksum = entry?.Hash.HashBytes ?? Array.Empty<byte>(),
                SongName = entry?.Name,
                SongArtist = entry?.Artist,
                SongCharter = entry?.Charter,
                BandScore = result.BandScore,
                BandStars = result.BandStars,
                SongSpeed = 1f,
                PlayedWithReplay = false,
                HasBots = false,
            };

            return (gameRecord, playerEntries);
        }

        /// <summary>
        /// Whether no-fail would be Off for this result: its own setting, else the game's.
        /// </summary>
        public static bool NoFailOff(SimulatedResult result)
        {
            return result.NoFail is { } on ? !on : SettingsManager.Settings.NoFail.Value == NoFailMode.Off;
        }

        /// <summary>
        /// The completion row this result would add, contract outcome included, without committing it (for
        /// the score screen preview).
        /// </summary>
        public static CareerSongCompletions PreviewRow(CareerPreset career, int tierIndex, int songIndex,
            SimulatedResult result)
        {
            var tier = career.Tiers[tierIndex];
            var song = tier.Songs[songIndex];
            var (record, players) = ScoreRecordsFor(song.SongEntry, result, result.Choose(CareerPlayers()));
            var commit = new CareerSongCompletionCommit();
            CareerContracts.Apply(commit, CareerContracts.For(tier, song), record, players, NoFailOff(result));

            return new CareerSongCompletions
            {
                TierId = tier.Id,
                TierIndex = tierIndex,
                CareerSongId = song.Id,
                SongIndex = songIndex,
                BandStars = result.BandStars.GetStarCount(),
                IsGold = result.BandStars == StarAmount.StarGold,
                ContractType = commit.ContractType,
                ContractMet = commit.ContractMet,
            };
        }

        private static bool Grant(CareerDatabase careers, CareerPreset career, int tierIndex, int songIndex,
            SimulatedResult result, List<YargPlayer> players, CareerCredit credit, out string error)
        {
            var song = career.Tiers[tierIndex].Songs[songIndex];
            var saveId = CareerBands.GetOrCreateSaveId(careers, career, credit);

            var commit = new CareerSongCompletionCommit
            {
                CareerSaveId = saveId,
                CareerId = career.Id,
                TierId = career.Tiers[tierIndex].Id,
                TierIndex = tierIndex,
                CareerSongId = song.Id,
                SongIndex = songIndex,
                SongChecksum = song.SongEntry?.Hash.HashBytes ?? Array.Empty<byte>(),
                BandStars = result.BandStars.GetStarCount(),
                IsGold = result.BandStars == StarAmount.StarGold,
                BandScore = result.BandScore,
                CompletedAt = DateTime.Now,
                Source = CareerCompletionSource.DebugGrant,
            };

            // Judged from the made-up result as the score path would judge a played one.
            var (record, playerEntries) = ScoreRecordsFor(song.SongEntry, result, players);
            CareerContracts.Apply(commit, CareerContracts.For(career.Tiers[tierIndex], song), record, playerEntries,
                NoFailOff(result));

            CareerSongOutcome.Commit(career, careers, commit);

            error = null;
            return true;
        }

        private static bool RecordScore(CareerDatabase careers, CareerPreset career, int tierIndex, int songIndex,
            SimulatedResult result, List<YargPlayer> players, out string error)
        {
            var entry = career.Tiers[tierIndex].Songs[songIndex].SongEntry;
            if (entry is null)
            {
                error = "the song is not in the library, so no score can be recorded for it";
                return false;
            }

            var (gameRecord, playerEntries) = ScoreRecordsFor(entry, result, players);

            // The score path reads the career context of the song that just ended.
            var previous = GlobalVariables.State.CurrentCareer;
            GlobalVariables.State.CurrentCareer = ContextFor(career, tierIndex, songIndex, result.Source);
            try
            {
                ScoreContainer.RecordScore(gameRecord, playerEntries);
            }
            finally
            {
                GlobalVariables.State.CurrentCareer = previous;
            }

            // RecordScore logs and swallows its own failures; a commit always leaves its outcome (and the
            // score path clears it first), so that tells whether a row actually landed, whichever save.
            if (CareerSongOutcome.Last is not { } outcome || outcome.CareerSongId != career.Tiers[tierIndex].Songs[songIndex].Id)
            {
                error = "the score path committed no career completion (see the player log)";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Score screen data for a made-up result, one card per player taking part (bots included,
        /// as after a real song).
        /// </summary>
        public static ScoreScreenStats ScoreScreenStatsFor(SimulatedResult result)
        {
            var players = PlayerContainer.Players.Where(player => !player.SittingOut).ToList();
            if (result.PlayerCount is not null || result.PlayerNames is not null)
            {
                players = result.Choose(players);
            }

            var cards = players.Select(player => new PlayerScoreCard
            {
                IsHighScore = false,
                IsReplay = false,
                Player = player,
                Stats = StatsFor(player.Profile.GameMode, result, result.IsFullComboFor(player),
                    Math.Max(1, players.Count)),
            }).ToArray();

            return new ScoreScreenStats
            {
                PlayerScores = cards,
                BandScore = result.BandScore,
                BandStars = (int) result.BandStars,
            };
        }

        private static BaseStats StatsFor(GameMode mode, SimulatedResult result, bool fullCombo, int playerCount)
        {
            BaseStats stats;
            switch (mode)
            {
                case GameMode.FourLaneDrums:
                case GameMode.FiveLaneDrums:
                case GameMode.EliteDrums:
                    stats = new DrumsStats();
                    break;
                case GameMode.Vocals:
                    stats = new VocalsStats
                    {
                        TicksHit = (uint) result.NotesHit,
                        TicksMissed = (uint) result.NotesMissed,
                    };
                    break;
                case GameMode.ProKeys:
                    stats = new KeysStats();
                    break;
                default:
                    stats = new GuitarStats();
                    break;
            }

            stats.TotalNotes = SimulatedResult.TOTAL_NOTES;
            stats.NotesHit = result.NotesHit;
            stats.MaxCombo = fullCombo ? SimulatedResult.TOTAL_NOTES : result.NotesHit / 2;
            stats.CommittedScore = result.BandScore / playerCount;
            stats.Stars = Math.Min(result.Stars, 6);
            return stats;
        }
    }
}
#endif
