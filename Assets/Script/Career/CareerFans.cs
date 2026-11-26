using System;
using System.Collections.Generic;

namespace YARG.Career
{
    /// <summary>
    /// A band's rank title, derived from its fans (thresholds in <see cref="CareerFans.RankFans"/>), never
    /// stored. Lowest first: the order is the rank order.
    /// </summary>
    public enum CareerRank
    {
        GarageBand,
        LocalAct,
        ClubRegulars,
        TouringBand,
        Headliners,
        ArenaLegends,
    }

    /// <summary>
    /// Fan tuning: every number that decides how many fans a song earns lives here, so tuning is a
    /// one-file change. Pure (no UnityEngine), so it is covered by Tests/YARG.Career.Tests.
    ///
    /// <code>
    /// songFans = BASE_FANS * multiplier * starFactor(stars) * (bonus tier ? BONUS_RATE : 1)
    ///            * (1 + contractBonusPercent / 100, if that result met the song's contract)
    /// </code>
    ///
    /// The multiplier comes from the song's difficulty (<see cref="DifficultyMultiplier"/>: harder songs
    /// earn more), unless the setlist sets its own amount for the tier: its
    /// <see cref="CareerTier.FanMultiplier"/>, or else its venue size (<see cref="VenueMultiplier"/>).
    ///
    /// Only the song's best result counts (see <see cref="CareerEvaluation"/>): the one result worth the
    /// most fans, contract bonus included. Replaying improves the total but cannot farm it. Every career
    /// earns fans.
    /// </summary>
    public static class CareerFans
    {
        /// <summary>Fans for a 5-star song at a Starter venue.</summary>
        public const int BASE_FANS = 100;

        /// <summary>Bonus tiers earn fans at this rate.</summary>
        public const float BONUS_RATE = 0.5f;

        /// <summary>Star value used for a gold result (one more than a full 5 stars).</summary>
        public const int GOLD_STARS = 6;

        /// <summary>
        /// How many times <see cref="BASE_FANS"/> a venue size is worth. A tier's
        /// <see cref="CareerTier.FanMultiplier"/> overrides this.
        /// </summary>
        public static float VenueMultiplier(VenueSize size)
        {
            return size switch
            {
                VenueSize.Small  => 2.5f,  //   250 fans
                VenueSize.Medium => 6f,    //   600 fans
                VenueSize.Large  => 15f,   // 1,500 fans
                VenueSize.Epic   => 40f,   // 4,000 fans
                _                => 1f,    //   100 fans (Starter, or not specified)
            };
        }

        /// <summary>
        /// Share of a song's fans earned for a result. <paramref name="stars"/> is 0 to 5, or
        /// <see cref="GOLD_STARS"/> for gold.
        /// </summary>
        public static float StarFactor(int stars)
        {
            return stars switch
            {
                >= GOLD_STARS => 1.25f,
                5             => 1f,
                4             => 0.75f,
                3             => 0.5f,
                1 or 2        => 0.25f,
                _             => 0f,
            };
        }

        public static float TierMultiplier(CareerTier tier)
        {
            return tier.FanMultiplier ?? VenueMultiplier(tier.VenueSize);
        }

        /// <summary>Intensity used for a song whose difficulty is not known (or not in the library).</summary>
        public const int DEFAULT_INTENSITY = 2;

        /// <summary>
        /// How many times <see cref="BASE_FANS"/> a song of this difficulty is worth.
        /// <paramref name="intensity"/> is the chart's difficulty rating, 0 (easiest) to 6 (hardest);
        /// anything outside is clamped.
        /// </summary>
        public static float DifficultyMultiplier(int intensity)
        {
            return Math.Clamp(intensity, 0, 6) switch
            {
                0 => 1f,    //   100 fans
                1 => 1.5f,  //   150 fans
                2 => 2.5f,  //   250 fans
                3 => 4f,    //   400 fans
                4 => 6f,    //   600 fans
                5 => 10f,   // 1,000 fans
                _ => 15f,   // 1,500 fans
            };
        }

        // Checked when the chart has no band difficulty.
        private static readonly YARG.Core.Instrument[] INTENSITY_FALLBACKS =
        {
            YARG.Core.Instrument.FiveFretGuitar,
            YARG.Core.Instrument.FiveFretBass,
            YARG.Core.Instrument.FourLaneDrums,
            YARG.Core.Instrument.Keys,
            YARG.Core.Instrument.Vocals,
        };

        /// <summary>
        /// The song's difficulty rating: its band difficulty, else the hardest of its main parts, else
        /// <see cref="DEFAULT_INTENSITY"/>.
        /// </summary>
        public static int Intensity(CareerSong song)
        {
            return Intensity(song?.SongEntry);
        }

        /// <summary>
        /// The rating of the song a result was played on: the career's song, or, when another song was
        /// played in its place (a bonus gig's substitute, see RetailMode), that song.
        /// </summary>
        public static int Intensity(CareerSong song, byte[] playedChecksum)
        {
            var entry = song?.SongEntry;
            if (playedChecksum is { Length: > 0 } &&
                (entry is null || !entry.Hash.HashBytes.AsSpan().SequenceEqual(playedChecksum)))
            {
                var played = YARG.Core.Song.HashWrapper.Create(playedChecksum);
                if (YARG.Song.SongContainer.SongsByHash.TryGetValue(played, out var songs) && songs.Count > 0)
                {
                    return Intensity(songs[0]);
                }
            }

            return Intensity(entry);
        }

        private static int Intensity(YARG.Core.Song.SongEntry entry)
        {
            if (entry is null)
            {
                return DEFAULT_INTENSITY;
            }

            int intensity = entry.BandDifficulty;
            if (intensity < 0)
            {
                foreach (var instrument in INTENSITY_FALLBACKS)
                {
                    intensity = Math.Max(intensity, entry[instrument].Intensity);
                }
            }

            return intensity < 0 ? DEFAULT_INTENSITY : Math.Min(intensity, 6);
        }

        /// <summary>
        /// The multiplier for one song: the setlist's own amount if the tier sets one (its
        /// <see cref="CareerTier.FanMultiplier"/>, else its venue size when one is specified), else from
        /// the song's difficulty.
        /// </summary>
        public static float SongMultiplier(CareerTier tier, CareerSong song, byte[] playedChecksum = null)
        {
            if (tier.FanMultiplier is { } multiplier)
            {
                return multiplier;
            }

            return tier.VenueSize != VenueSize.NotSpecified
                ? VenueMultiplier(tier.VenueSize)
                : DifficultyMultiplier(Intensity(song, playedChecksum));
        }

        /// <summary>
        /// Fans <paramref name="song"/> of <paramref name="tier"/> earns for a result of
        /// <paramref name="stars"/> with a contract bonus of <paramref name="contractBonusPercent"/>
        /// (0 if it did not meet a contract).
        /// </summary>
        public static int SongFans(CareerTier tier, CareerSong song, int stars, int contractBonusPercent)
        {
            return Fans(SongMultiplier(tier, song), tier.IsBonus, stars, contractBonusPercent);
        }

        /// <summary>
        /// Fans for one stored result. <paramref name="playedChecksum"/> is the song the result was
        /// played on: when it is not the career's own song (a substitute), its difficulty decides.
        /// </summary>
        public static int SongFans(CareerTier tier, CareerSong song, int stars, int contractBonusPercent,
            byte[] playedChecksum)
        {
            return Fans(SongMultiplier(tier, song, playedChecksum), tier.IsBonus, stars, contractBonusPercent);
        }

        /// <summary>
        /// Fans one song of <paramref name="tier"/> earns with a best result of <paramref name="stars"/>,
        /// by the tier's own amount (multiplier or venue size), whatever the song.
        /// </summary>
        public static int SongFans(CareerTier tier, int stars)
        {
            return SongFans(tier, stars, 0);
        }

        /// <summary>
        /// Fans for a result of <paramref name="stars"/> that earns a contract bonus of
        /// <paramref name="contractBonusPercent"/> (0 if it did not meet a contract). Negative bonuses
        /// count as 0.
        /// </summary>
        public static int SongFans(CareerTier tier, int stars, int contractBonusPercent)
        {
            return Fans(TierMultiplier(tier), tier.IsBonus, stars, contractBonusPercent);
        }

        private static int Fans(float multiplier, bool bonusTier, int stars, int contractBonusPercent)
        {
            double fans = BASE_FANS * multiplier * StarFactor(stars);
            if (bonusTier)
            {
                fans *= BONUS_RATE;
            }

            // Multiplied before dividing so a round bonus (+10%) gives exact halves to round.
            fans = fans * (100 + Math.Max(0, contractBonusPercent)) / 100;

            return (int) Math.Round(fans, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// The career gates a tier on <see cref="UnlockType.FanCount"/>: there a song is done once it
        /// has earned its top fans.
        /// </summary>
        public static bool FanGated(CareerPreset career)
        {
            if (career?.Tiers is null)
            {
                return false;
            }

            foreach (var tier in career.Tiers)
            {
                if (tier.UnlockType == UnlockType.FanCount)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every career earns and shows fans.
        /// </summary>
        public static bool UsesFans(CareerPreset career)
        {
            return career?.Tiers is not null;
        }

        /// <summary>
        /// Fans a band needs for each <see cref="CareerRank"/>, indexed by the rank. A full run of the
        /// "Dev Fan Test" career (Starter to Epic, all gold) is worth about 25,000 fans, so the top rank
        /// takes more than one career.
        /// </summary>
        private static readonly int[] RANK_FANS =
        {
            0,      // Garage Band
            500,    // Local Act
            2_000,  // Club Regulars
            7_500,  // Touring Band
            20_000, // Headliners
            50_000, // Arena Legends
        };

        /// <summary>
        /// Fans needed for <paramref name="rank"/>.
        /// </summary>
        public static int RankFans(CareerRank rank)
        {
            return RANK_FANS[Math.Clamp((int) rank, 0, RANK_FANS.Length - 1)];
        }

        /// <summary>
        /// The band's rank title for its fan total: the highest rank whose threshold it has reached.
        /// </summary>
        public static CareerRank Rank(int fans)
        {
            var rank = CareerRank.GarageBand;
            for (var i = 1; i < RANK_FANS.Length; i++)
            {
                if (fans >= RANK_FANS[i])
                {
                    rank = (CareerRank) i;
                }
            }

            return rank;
        }

        /// <summary>
        /// The next rank and how many more fans it needs, or null at the top rank.
        /// </summary>
        public static (CareerRank rank, int fansNeeded)? NextRank(int fans)
        {
            var current = Rank(fans);
            if ((int) current >= RANK_FANS.Length - 1)
            {
                return null;
            }

            var next = current + 1;
            return (next, RankFans(next) - Math.Max(0, fans));
        }

        /// <summary>
        /// A band's fans: the sum of the fan totals of its active saves, one evaluation per save.
        /// Derived, never stored.
        /// </summary>
        public static int BandFans(IEnumerable<CareerEvaluation> saveEvaluations)
        {
            var total = 0;
            foreach (var evaluation in saveEvaluations)
            {
                if (evaluation is { UsesFans: true })
                {
                    total += evaluation.TotalFans;
                }
            }

            return total;
        }
    }
}
