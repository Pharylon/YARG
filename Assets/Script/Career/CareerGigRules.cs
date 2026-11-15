using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Game;

namespace YARG.Career
{
    /// <summary>
    /// Gig rules of a tier: modifiers every player gets (<see cref="CareerTier.ForcedModifiers"/>) and the
    /// slowest song speed allowed (<see cref="CareerTier.MinSongSpeed"/>). Pure (no UnityEngine), covered by
    /// Tests/YARG.Career.Tests; <c>CareerGigSession</c> applies them to the players for the play only.
    /// </summary>
    public static class CareerGigRules
    {
        public static bool HasRules(CareerTier tier)
        {
            return tier is not null && (ForcedModifiers(tier) != Modifier.None || tier.MinSongSpeed is > 100);
        }

        public static Modifier ForcedModifiers(CareerTier tier)
        {
            return tier?.ForcedModifiers ?? Modifier.None;
        }

        /// <summary>
        /// The song speed of the play: the chosen one, or the tier's minimum if that is faster. A minimum
        /// below 100% is ignored (no score below 100% is saved; CareerValidation warns about it).
        /// </summary>
        public static float SongSpeed(CareerTier tier, float chosen)
        {
            if (tier?.MinSongSpeed is not { } percent || percent < 100)
            {
                return chosen;
            }

            var minimum = Math.Min(percent, 5000) / 100f;
            return chosen < minimum ? minimum : chosen;
        }

        /// <summary>
        /// A player's modifiers for the play: their own, plus each forced modifier their game mode and
        /// instrument support (<paramref name="possible"/>). A forced modifier replaces own ones it conflicts
        /// with (e.g. forced All Strums drops the player's All HOPOs), as picking it in the menu would.
        /// </summary>
        public static Modifier PlayerModifiers(Modifier own, Modifier forced, Modifier possible)
        {
            var result = own;
            foreach (var modifier in Flags(forced & possible))
            {
                result &= ~ModifierConflicts.FromSingleModifier(modifier);
                result |= modifier;
            }

            return result;
        }

        /// <summary>The single flags of a modifier set, lowest first.</summary>
        public static IEnumerable<Modifier> Flags(Modifier modifiers)
        {
            return Enum.GetValues(typeof(Modifier)).Cast<Modifier>()
                .Where(modifier => modifier != Modifier.None && (modifiers & modifier) == modifier);
        }
    }
}
