using System.Linq;
using YARG.Career;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// Career lines for the score screen, from the outcome the score path left in
    /// <see cref="CareerSongOutcome.Last"/>.
    /// </summary>
    public static class CareerScoreScreen
    {
        /// <summary>
        /// "+350 fans", plus any tiers the result unlocked, for the career song just shown. Null if the
        /// song was not a career song, nothing was committed for it, or its career does not use fans.
        /// </summary>
        public static string FanLine()
        {
            var context = GlobalVariables.State.CurrentCareer;
            var outcome = CareerSongOutcome.Last;
            if (context is null || outcome is null || !outcome.UsesFans ||
                outcome.CareerId != context.CareerId || outcome.CareerSongId != context.CareerSongId)
            {
                return null;
            }

            var text = outcome.FansGained > 0
                ? Localize.KeyFormat("Menu.Career.FansGained", outcome.FansGained.ToString("N0"))
                : Localize.Key("Menu.Career.FansNoGain");

            var unlocked = outcome.UnlockedTierNames.Where(name => !string.IsNullOrEmpty(name)).ToList();
            if (unlocked.Count > 0)
            {
                text = Localize.KeyFormat("Menu.Career.FansUnlocked", text, Localize.List(unlocked));
            }

            return text;
        }
    }
}
