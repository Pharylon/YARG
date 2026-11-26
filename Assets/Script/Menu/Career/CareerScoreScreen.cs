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

            // A gig's closer: it earns nothing but the song itself.
            if (context is { Closer: true })
            {
                return RetailMode.LastCloser is null ? null : Localize.Key("Menu.Retail.ScoreCloser");
            }

            if (context is null || outcome is null ||
                outcome.CareerId != context.CareerId || outcome.CareerSongId != context.CareerSongId)
            {
                return null;
            }

            var contract = CareerContractText.Outcome(outcome);
            var fans = FanLine(outcome);

            var line = fans is null ? contract
                : contract is null ? fans
                : Localize.KeyFormat("Menu.Career.HeaderJoin", fans, contract);

            // This result earned the show its encore, which Continue plays next.
            if (CareerShow.EncoreSongId == context.CareerSongId)
            {
                var encore = Localize.Key("Menu.Career.Show.EncoreEarned");
                line = line is null ? encore : Localize.KeyFormat("Menu.Career.HeaderJoin", line, encore);
            }

            return line;
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
