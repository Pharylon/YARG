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
using YARG.Menu.ScoreScreen;
using YARG.Player;
using YARG.Scores;
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

        /// <summary>Every player's difficulty. Defaults to each profile's current difficulty.</summary>
        public Difficulty? Difficulty;

        public const int TOTAL_NOTES = 1000;

        public int BandScore => Score ?? Math.Max(0, Stars) * 50_000;
        public StarAmount BandStars => StarAmountHelper.GetStarsFromInt(Math.Clamp(Stars, 0, 6));
        public int NotesHit => (int) Math.Round(Math.Clamp(Percent, 0f, 1f) * TOTAL_NOTES);
        public int NotesMissed => TOTAL_NOTES - NotesHit;
        public bool IsFullCombo => FullCombo ?? NotesMissed == 0;
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
                .Where(player => !player.SittingOut && !player.Profile.IsBot)
                .ToList();
        }

        public static IEnumerable<CareerPreset> AllCareers()
        {
            return CustomContentManager.Careers.DefaultPresets.Concat(CustomContentManager.Careers.CustomPresets);
        }

        /// <summary>
        /// The context a song started from the career menu carries (see <c>SongViewType.ViewClick</c>).
        /// </summary>
        public static CareerContext ContextFor(CareerPreset career, int tierIndex, int songIndex)
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
            };
        }

        /// <summary>
        /// The active save of the current players for this career, or 0 if they have none yet.
        /// </summary>
        public static int FindSaveId(CareerPreset career)
        {
            var profiles = CareerPlayers().Select(player => player.Profile.Id).ToList();
            if (ScoreContainer.Careers is null || profiles.Count == 0)
            {
                return 0;
            }

            return ScoreContainer.Careers.FindActiveSaveForProfiles(career.Id, profiles)?.Id ?? 0;
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

            var players = CareerPlayers();
            if (players.Count == 0)
            {
                error = "no human players are taking part";
                return false;
            }

            return result.Path == SimulationPath.Grant
                ? Grant(careers, career, tierIndex, songIndex, result, players, out error)
                : RecordScore(careers, career, tierIndex, songIndex, result, players, out error);
        }

        private static bool Grant(CareerDatabase careers, CareerPreset career, int tierIndex, int songIndex,
            SimulatedResult result, List<YargPlayer> players, out string error)
        {
            var song = career.Tiers[tierIndex].Songs[songIndex];
            var saveId = careers.GetOrCreateSave(career.Id, career.Version,
                players.Select(player => player.Profile.Id).ToList());

            CareerSongOutcome.Commit(career, careers, new CareerSongCompletionCommit
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
            });

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

            var playerEntries = players.Select(player => new PlayerScoreRecord
            {
                PlayerId = player.Profile.Id,
                Instrument = player.Profile.CurrentInstrument,
                Difficulty = result.Difficulty ?? player.Profile.CurrentDifficulty,
                EnginePresetId = player.Profile.EnginePreset,
                Score = result.BandScore / players.Count,
                Stars = result.BandStars,
                NotesHit = result.NotesHit,
                NotesMissed = result.NotesMissed,
                IsFc = result.IsFullCombo,
                IsReplay = false,
                Percent = (float) result.NotesHit / SimulatedResult.TOTAL_NOTES,
            }).ToList();

            var gameRecord = new GameRecord
            {
                Date = DateTime.Now,
                SongChecksum = entry.Hash.HashBytes,
                SongName = entry.Name,
                SongArtist = entry.Artist,
                SongCharter = entry.Charter,
                BandScore = result.BandScore,
                BandStars = result.BandStars,
                SongSpeed = 1f,
                PlayedWithReplay = false,
                HasBots = false,
            };

            var before = CountCompletions(careers, career);

            // The score path reads the career context of the song that just ended.
            var previous = GlobalVariables.State.CurrentCareer;
            GlobalVariables.State.CurrentCareer = ContextFor(career, tierIndex, songIndex);
            try
            {
                ScoreContainer.RecordScore(gameRecord, playerEntries);
            }
            finally
            {
                GlobalVariables.State.CurrentCareer = previous;
            }

            // RecordScore logs and swallows its own failures, so check that a row actually landed.
            if (CountCompletions(careers, career) <= before)
            {
                error = "the score path committed no career completion (see the player log)";
                return false;
            }

            error = null;
            return true;
        }

        private static int CountCompletions(CareerDatabase careers, CareerPreset career)
        {
            var saveId = FindSaveId(career);
            return saveId > 0 ? careers.GetSongCompletions(saveId).Count : 0;
        }

        /// <summary>
        /// Score screen data for a made-up result, one card per player taking part (bots included,
        /// as after a real song).
        /// </summary>
        public static ScoreScreenStats ScoreScreenStatsFor(SimulatedResult result)
        {
            var players = PlayerContainer.Players.Where(player => !player.SittingOut).ToList();
            var cards = players.Select(player => new PlayerScoreCard
            {
                IsHighScore = false,
                IsReplay = false,
                Player = player,
                Stats = StatsFor(player.Profile.GameMode, result, Math.Max(1, players.Count)),
            }).ToArray();

            return new ScoreScreenStats
            {
                PlayerScores = cards,
                BandScore = result.BandScore,
                BandStars = (int) result.BandStars,
            };
        }

        private static BaseStats StatsFor(GameMode mode, SimulatedResult result, int playerCount)
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
            stats.MaxCombo = result.IsFullCombo ? SimulatedResult.TOTAL_NOTES : result.NotesHit / 2;
            stats.CommittedScore = result.BandScore / playerCount;
            stats.Stars = Math.Min(result.Stars, 6);
            return stats;
        }
    }
}
#endif
