using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Playlists;
using YARG.Scores;

namespace YARG.Career
{
    /// <summary>
    /// A career gig played as a show: a tier with <see cref="CareerCompletionMode.FullTierPlaylist"/> queued
    /// through the setlist owner (<see cref="Setlist"/>), so it plays like a show from the music library:
    /// instrument select once, then song after song, with the score screen in between.
    ///
    /// The setlist's songs are the music library's queue; a show borrows it and gives the queued songs back
    /// when it ends. While the show plays, <c>GlobalVariables.State.CurrentCareer</c> follows the setlist's
    /// position (the score screen keeps the state between the songs of a show), so every song is committed
    /// on its own, with its own career song, when it ends (<see cref="CareerCompletionSource.FullTierPlaylist"/>).
    ///
    /// Leaving early (quitting from the pause menu, "End setlist early", backing out of the instrument
    /// select): the songs finished so far count like any completion; the rest are simply not played. The
    /// career menu ends a show that is still marked as playing when it opens again.
    /// </summary>
    public static class CareerShow
    {
        private sealed class Show
        {
            public CareerPreset Career;
            public int TierIndex;
            public Guid? BandId;

            // Indexes into the tier's songs, in play order; parallel to the setlist's songs.
            public readonly List<int> SongIndexes = new();

            // Stars of each song's committed result, in play order (null: no result yet, skipped or not saved).
            public readonly List<int?> Stars = new();

            // How many songs the set has; any after them are encores.
            public int SetCount;
            public bool EncoreQueued;

            // The library's queued setlist, given back when the show ends.
            public List<HashWrapper> SavedHashes;
            public string SavedName;
        }

        private static Show _show;

        public static bool IsActive => _show is not null;

        /// <summary>
        /// Start a show of these songs of the tier (indexes into its songs, in play order). Songs not in the
        /// library are left out. Sets the current song and career context for the first song; the caller
        /// opens the instrument select. False, changing nothing, if no song can be played.
        /// </summary>
        public static bool TryStart(CareerPreset career, int tierIndex, IReadOnlyList<int> songIndexes,
            Guid? bandId, out string error)
        {
            EndIfRunning();

            var tier = career.Tiers[tierIndex];
            var show = new Show { Career = career, TierIndex = tierIndex, BandId = bandId };
            var hashes = new List<HashWrapper>();
            foreach (var songIndex in songIndexes)
            {
                var entry = songIndex >= 0 && songIndex < tier.Songs.Length ? tier.Songs[songIndex]?.SongEntry : null;
                if (entry is null)
                {
                    YargLogger.LogFormatWarning<int, string>("Career show: song {0} of tier '{1}' is not in the library, left out.",
                        songIndex + 1, tier.Name);
                    continue;
                }

                // The setlist plays every song once.
                if (hashes.Contains(entry.Hash))
                {
                    continue;
                }

                hashes.Add(entry.Hash);
                show.SongIndexes.Add(songIndex);
                show.Stars.Add(null);
            }

            show.SetCount = show.SongIndexes.Count;

            if (hashes.Count == 0)
            {
                error = "none of the gig's songs are in the library";
                return false;
            }

            var playlist = Setlist.Playlist;
            show.SavedHashes = new List<HashWrapper>(playlist.SongHashes);
            show.SavedName = playlist.Name;

            Setlist.EndShow();
            playlist.SongHashes.Clear();
            playlist.SongHashes.AddRange(hashes);
            playlist.Name = tier.Name;

            _show = show;
            EncoreSongId = null;
            Setlist.Changed += OnSetlistChanged;
            CareerSongOutcome.Committed += OnCommitted;

            if (!Setlist.TryStart(out var first) || Setlist.ResolveSongs().Count != show.SongIndexes.Count)
            {
                // Should not happen (every hash came from a library entry); never play a show whose songs
                // and career songs could be out of step.
                Finish();
                error = "the setlist did not take the gig's songs";
                return false;
            }

            GlobalVariables.State.SetPlaybackSong(first, ContextFor(0));

            YargLogger.LogFormatInfo<string, int>("Career show started: '{0}', {1} songs.", tier.Name, show.SongIndexes.Count);
            error = null;
            return true;
        }

        /// <summary>
        /// Ends a show that is still running (the career menu calls this when it opens: a show never runs
        /// behind the menu, so one still marked as playing was left early).
        /// </summary>
        public static void EndIfRunning()
        {
            if (_show is null)
            {
                return;
            }

            if (Setlist.IsPlaying)
            {
                // Raises Setlist.Changed, which finishes the show.
                Setlist.EndShow();
            }

            Finish();
        }

        private static void OnSetlistChanged()
        {
            if (_show is null)
            {
                return;
            }

            if (!Setlist.IsPlaying)
            {
                Finish();
                return;
            }

            // The setlist moved on (the score screen's Continue, or Skip in the pause menu): the next song
            // is played as the next career song. Fires before the caller sets the current song.
            if (Setlist.Index >= 0 && Setlist.Index < _show.SongIndexes.Count)
            {
                GlobalVariables.State.CurrentCareer = ContextFor(Setlist.Index);
            }
        }

        /// <summary>
        /// The career song after whose result the encore was queued (for the score screen), or null.
        /// </summary>
        public static Guid? EncoreSongId { get; private set; }

        /// <summary>
        /// The between-songs decision point: every song of the show is committed when it ends (before the
        /// score screen). After the set's last song, if the set's average stars reach the tier's bar, the
        /// encore songs are added to the setlist, so the score screen's Continue plays them.
        /// </summary>
        private static void OnCommitted(CareerSongCompletionCommit commit, CareerSongOutcome outcome)
        {
            var show = _show;
            if (show is null || !Setlist.IsPlaying || Setlist.Index < 0 || Setlist.Index >= show.SongIndexes.Count)
            {
                return;
            }

            var tier = show.Career.Tiers[show.TierIndex];
            var position = Setlist.Index;
            if (commit.CareerId != show.Career.Id || commit.CareerSongId != tier.Songs[show.SongIndexes[position]].Id)
            {
                return;
            }

            show.Stars[position] = commit.BandStars;

            if (show.EncoreQueued || position != show.SetCount - 1)
            {
                return;
            }

            var setStars = show.Stars.Take(show.SetCount).Select(stars => stars ?? 0).ToList();
            if (!CareerSetlists.EncoreEarned(tier, setStars))
            {
                return;
            }

            foreach (var songIndex in CareerSetlists.EncoreSongIndexes(tier))
            {
                var entry = tier.Songs[songIndex].SongEntry;
                if (entry is null || Setlist.Playlist.SongHashes.Contains(entry.Hash))
                {
                    continue;
                }

                // Straight into the list (no Edited event): the setlist's position stays where it is.
                Setlist.Playlist.SongHashes.Add(entry.Hash);
                show.SongIndexes.Add(songIndex);
                show.Stars.Add(null);
                show.EncoreQueued = true;
            }

            if (show.EncoreQueued)
            {
                EncoreSongId = commit.CareerSongId;
                YargLogger.LogFormatInfo<string, float>("Career show: encore earned at '{0}' (average {1} stars).",
                    tier.Name, (float) setStars.Average());
            }
        }

        /// <summary>
        /// The career context of the show's current song, or null when no show is running. Whoever moves
        /// the setlist on passes this to <c>SetPlaybackSong</c>, which otherwise clears the career context.
        /// </summary>
        public static CareerContext CurrentContext =>
            _show is not null && Setlist.IsPlaying && Setlist.Index >= 0 && Setlist.Index < _show.SongIndexes.Count
                ? ContextFor(Setlist.Index)
                : null;

        private static CareerContext ContextFor(int position)
        {
            var tier = _show.Career.Tiers[_show.TierIndex];
            var songIndex = _show.SongIndexes[position];
            return new CareerContext
            {
                CareerId = _show.Career.Id,
                TierId = tier.Id,
                TierIndex = _show.TierIndex,
                VenueHint = tier.VenueHint,
                CareerSongId = tier.Songs[songIndex].Id,
                SongIndex = songIndex,
                BandId = _show.BandId,
                Source = CareerCompletionSource.FullTierPlaylist,
            };
        }

        private static void Finish()
        {
            var show = _show;
            if (show is null)
            {
                return;
            }

            _show = null;
            EncoreSongId = null;
            Setlist.Changed -= OnSetlistChanged;
            CareerSongOutcome.Committed -= OnCommitted;
            CareerGigSession.Restore();

            // Give the library its queued songs back.
            var playlist = Setlist.Playlist;
            playlist.SongHashes.Clear();
            playlist.SongHashes.AddRange(show.SavedHashes);
            playlist.Name = show.SavedName;

            YargLogger.LogInfo("Career show ended.");
        }

        /// <summary>For the dev remote: the running show, if any.</summary>
        public static string Describe()
        {
            if (_show is null)
            {
                return "no career show";
            }

            var tier = _show.Career.Tiers[_show.TierIndex];
            return $"career show '{tier.Name}' songs=[{string.Join(",", _show.SongIndexes)}] set={_show.SetCount} " +
                $"stars=[{string.Join(",", _show.Stars.Select(stars => stars?.ToString() ?? "-"))}] encore={_show.EncoreQueued}";
        }
    }
}
