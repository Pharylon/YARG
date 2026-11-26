using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Career
{
    /// <summary>
    /// Setlist rules of a gig: whether it is played as a show (a tier with
    /// <see cref="CareerCompletionMode.FullTierPlaylist"/>), which songs make up its set, how many completions
    /// finish the tier (make your own setlist, <see cref="CareerTier.SetlistSize"/>), when the encore is earned
    /// (<see cref="CareerTier.EncoreMinStars"/>, <see cref="CareerSong.IsEncore"/>) and which songs stay hidden
    /// (<see cref="CareerTier.Mystery"/>). Pure (no UnityEngine), covered by Tests/YARG.Career.Tests; the show
    /// itself is run by <c>CareerShow</c>.
    /// </summary>
    public static class CareerSetlists
    {
        private const float STARS_EPSILON = 0.0001f;

        /// <summary>
        /// The tier is played as one show through the setlist. <see cref="CareerCompletionMode.FullCareerPlaylist"/>
        /// is not supported: such tiers are played song by song (CareerValidation warns about it).
        /// </summary>
        public static bool IsShow(CareerTier tier)
        {
            // A gig with user picks is played song by song: its picks are chosen as they are played.
            return tier?.CompletionMode == CareerCompletionMode.FullTierPlaylist && !(tier.UserPicks > 0);
        }

        public static bool IsEncore(CareerSong song) => song?.IsEncore == true;

        /// <summary>The indexes of the tier's set: every song except encores, in list order.</summary>
        public static List<int> SetSongIndexes(CareerTier tier)
        {
            var songs = tier?.Songs ?? Array.Empty<CareerSong>();
            return Enumerable.Range(0, songs.Length).Where(i => songs[i] is not null && !songs[i].IsEncore).ToList();
        }

        /// <summary>The indexes of the tier's encore songs, in list order.</summary>
        public static List<int> EncoreSongIndexes(CareerTier tier)
        {
            var songs = tier?.Songs ?? Array.Empty<CareerSong>();
            return Enumerable.Range(0, songs.Length).Where(i => songs[i] is not null && songs[i].IsEncore).ToList();
        }

        /// <summary>
        /// The songs a full show plays, in order: the whole set. (With <see cref="PicksSongs"/>, the players'
        /// picks instead, see <see cref="ShowSongIndexes(CareerTier, IReadOnlyCollection{int})"/>.)
        /// </summary>
        public static List<int> ShowSongIndexes(CareerTier tier)
        {
            return SetSongIndexes(tier);
        }

        /// <summary>
        /// The songs a show of the players' picks plays: the picked songs of the set, in list order. Null if
        /// the picks are not a valid choice (<see cref="ValidPicks"/>).
        /// </summary>
        public static List<int> ShowSongIndexes(CareerTier tier, IReadOnlyCollection<int> picks)
        {
            return ValidPicks(tier, picks) ? SetSongIndexes(tier).Where(picks.Contains).ToList() : null;
        }

        /// <summary>
        /// How many of the set's songs must be completed for the tier to be complete:
        /// <see cref="CareerTier.SetlistSize"/> (at most the set), or the whole set.
        /// </summary>
        public static int RequiredSongs(CareerTier tier)
        {
            var set = SetSongIndexes(tier).Count;
            return tier?.SetlistSize is { } size && size >= 1 ? Math.Min(size, set) : set;
        }

        /// <summary>Make your own setlist: the players pick fewer songs than the set has.</summary>
        public static bool PicksSongs(CareerTier tier)
        {
            return RequiredSongs(tier) < SetSongIndexes(tier).Count;
        }

        /// <summary>Exactly <see cref="RequiredSongs"/> different songs of the set.</summary>
        public static bool ValidPicks(CareerTier tier, IReadOnlyCollection<int> picks)
        {
            if (picks is null)
            {
                return false;
            }

            var set = SetSongIndexes(tier);
            return picks.Count == RequiredSongs(tier) && picks.Distinct().Count() == picks.Count &&
                picks.All(set.Contains);
        }

        /// <summary>
        /// The tier is complete: at least <see cref="RequiredSongs"/> songs of its set are completed (encores
        /// never count towards it).
        /// </summary>
        public static bool IsComplete(CareerTier tier, Func<int, bool> songCompleted)
        {
            var required = RequiredSongs(tier);
            return required > 0 && SetSongIndexes(tier).Count(songCompleted) >= required;
        }

        /// <summary>
        /// The tier has an encore that can be earned: it is played as a show, has encore songs and a bar of
        /// at most 5 stars.
        /// </summary>
        public static bool HasEncore(CareerTier tier)
        {
            return IsShow(tier) && tier.EncoreMinStars is { } bar && bar <= 5f + STARS_EPSILON &&
                EncoreSongIndexes(tier).Count > 0;
        }

        /// <summary>
        /// The encore is earned: the average of the stars of the show's set songs (a song left without a
        /// result counts as 0; gold counts as 5) reaches the tier's bar.
        /// </summary>
        public static bool EncoreEarned(CareerTier tier, IReadOnlyCollection<int> setStars)
        {
            if (!HasEncore(tier) || setStars is null || setStars.Count == 0)
            {
                return false;
            }

            return setStars.Average(stars => Math.Clamp(stars, 0, 5)) >= tier.EncoreMinStars.Value - STARS_EPSILON;
        }

        /// <summary>
        /// Whether this song can ever be played: every set song can; an encore only if its tier's encore can
        /// be earned.
        /// </summary>
        public static bool CanBePlayed(CareerTier tier, CareerSong song)
        {
            return song is not null && (!song.IsEncore || HasEncore(tier));
        }

        /// <summary>
        /// The song's title and artist are hidden: a mystery setlist's song, or an encore, not yet completed in
        /// this save.
        /// </summary>
        public static bool IsHidden(CareerTier tier, CareerSong song, bool completed)
        {
            return !completed && (tier?.Mystery == true || song?.IsEncore == true);
        }
    }
}
