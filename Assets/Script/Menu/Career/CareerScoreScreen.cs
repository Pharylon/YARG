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
        /// "+350 fans", plus any tiers the result unlocked, plus whether the song's contract was met, for
        /// the career song just shown. Null if the song was not a career song, nothing was committed for it,
        /// or its career neither uses fans nor gives the song a contract.
        /// </summary>
        public static string StatusLine()
        {
            var context = GlobalVariables.State.CurrentCareer;
            var outcome = CareerSongOutcome.Last;
            if (context is null || outcome is null ||
                outcome.CareerId != context.CareerId || outcome.CareerSongId != context.CareerSongId)
            {
                return null;
            }

            var contract = CareerContractText.Outcome(outcome);
            var fans = FanLine(outcome);

            if (fans is null)
            {
                return contract;
            }

            return contract is null ? fans : Localize.KeyFormat("Menu.Career.HeaderJoin", fans, contract);
        }

        private static string FanLine(CareerSongOutcome outcome)
        {
            if (!outcome.UsesFans)
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
