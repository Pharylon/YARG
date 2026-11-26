using System;
using System.Collections.Generic;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Scores;
using YARG.Settings;

namespace YARG.Career
{
    /// <summary>
    /// Retail mode (Settings > Experimental): the game plays like one bought in a box. Songs start
    /// locked for quickplay and are unlocked by completing them in a career: a setlist's songs, a song
    /// played in place of a bonus gig's song, or the "closer" the players pick once every song of a gig
    /// has 3 stars. Game Modes becomes the main menu's first entry, with Quickplay inside it.
    ///
    /// Unlocks are not stored on their own: a song is unlocked when the career database holds a
    /// completion of it (any band, any save).
    /// </summary>
    public static class RetailMode
    {
        /// <summary>Stars every song of a gig needs before its closer can be picked.</summary>
        public const int CLOSER_MIN_STARS = 3;

        public static bool Enabled => SettingsManager.Settings?.RetailMode.Value ?? false;

        /// <summary>
        /// "Show all songs" in quickplay: the unlock filter is off for now, without leaving retail
        /// mode. Not saved; the music library turns it off again when it is left.
        /// </summary>
        public static bool ShowAllSongs { get; set; }

        /// <summary>Quickplay lists only unlocked songs right now.</summary>
        public static bool FilterActive => Enabled && !ShowAllSongs;

        /// <summary>
        /// Retail mode was turned on or off in the settings: menus that differ with it rebuild.
        /// </summary>
        public static event Action Changed;

        public static void NotifyChanged()
        {
            ShowAllSongs = false;
            Changed?.Invoke();
        }

        /// <summary>
        /// The closer the score path just recorded, for the score screen (null: the last career song
        /// was not a closer, or it did not count).
        /// </summary>
        public static CareerCloser LastCloser { get; set; }

        private static HashSet<HashWrapper> _unlocked;

        /// <summary>
        /// The songs career play has unlocked. Read from the database once and kept until
        /// <see cref="Invalidate"/> (called whenever a career result is recorded).
        /// </summary>
        public static IReadOnlyCollection<HashWrapper> UnlockedSongs => Unlocked();

        public static bool IsUnlocked(SongEntry song)
        {
            return song is not null && Unlocked().Contains(song.Hash);
        }

        /// <summary>
        /// Whether quickplay shows the song right now: always outside retail mode or with the filter
        /// off, else only once unlocked.
        /// </summary>
        public static bool IsVisible(SongEntry song)
        {
            return !FilterActive || IsUnlocked(song);
        }

        public static void Invalidate()
        {
            _unlocked = null;
        }

        private static HashSet<HashWrapper> Unlocked()
        {
            if (_unlocked is not null)
            {
                return _unlocked;
            }

            var unlocked = new HashSet<HashWrapper>();
            try
            {
                var careers = ScoreContainer.Careers;
                if (careers is null)
                {
                    // Not cached: the database is not open yet.
                    return unlocked;
                }

                foreach (var checksum in careers.GetCareerSongChecksums())
                {
                    unlocked.Add(HashWrapper.Create(checksum));
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Retail mode: failed to read the unlocked songs.");
            }

            _unlocked = unlocked;
            return unlocked;
        }
    }
}
