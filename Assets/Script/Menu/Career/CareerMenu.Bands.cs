using YARG.Career;
using YARG.Localization;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The active band in the career header. Looked up once per progress load, not per selection.
    /// </summary>
    public partial class CareerMenu
    {
        private CareerEvaluation _bandTextEvaluation;
        private string _bandText;

        /// <summary>
        /// "Band: Name", with a warning when no member is among the players (results would not count),
        /// or null when no band is active.
        /// </summary>
        private string BandHeaderText()
        {
            if (_bandTextEvaluation == _evaluation && _bandTextEvaluation is not null)
            {
                return _bandText;
            }

            _bandTextEvaluation = _evaluation;
            _bandText = null;

            var careers = ScoreContainer.Careers;
            var band = CareerBands.ActiveBand(careers);
            if (band is null)
            {
                return null;
            }

            var credit = CareerBands.CurrentCredit(careers);
            _bandText = credit.Block == CareerCreditBlock.NoMemberPlaying
                ? Localize.KeyFormat("Menu.Career.HeaderBandNoMember", band.Name)
                : Localize.KeyFormat("Menu.Career.HeaderBand", band.Name);
            return _bandText;
        }
    }
}
