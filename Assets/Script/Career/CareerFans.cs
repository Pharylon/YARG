using System;

namespace YARG.Career
{
    /// <summary>
    /// Fan tuning: every number that decides how many fans a song earns lives here, so tuning is a
    /// one-file change. Pure (no UnityEngine), so it is covered by Tests/YARG.Career.Tests.
    ///
    /// <code>
    /// songFans = BASE_FANS * venueMultiplier * starFactor(bestStars) * (bonus tier ? BONUS_RATE : 1)
    /// </code>
    ///
    /// Only the song's best result counts (see <see cref="CareerEvaluation"/>), so replaying improves
    /// the total but cannot farm it. A career shows fans only when it opts in (<see cref="UsesFans"/>).
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

        /// <summary>
        /// Fans one song of <paramref name="tier"/> earns with a best result of <paramref name="stars"/>.
        /// </summary>
        public static int SongFans(CareerTier tier, int stars)
        {
            var fans = BASE_FANS * TierMultiplier(tier) * StarFactor(stars);
            if (tier.IsBonus)
            {
                fans *= BONUS_RATE;
            }

            return (int) Math.Round(fans, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// A career opts in to fans by gating any tier on <see cref="UnlockType.FanCount"/>. Careers
        /// that do not show no fan numbers at all.
        /// </summary>
        public static bool UsesFans(CareerPreset career)
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
    }
}
