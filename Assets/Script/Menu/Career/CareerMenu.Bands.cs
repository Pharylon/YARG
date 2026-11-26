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
        private string _bandHometown;

        /// <summary>
        /// The tier is a "Hometown show": its city is the active band's hometown (text only).
        /// </summary>
        private bool IsHometown(CareerTier tier)
        {
            BandHeaderText();
            return tier is not null &&
                (IsOpeningGig(tier) || CareerBandSheet.IsHometown(tier.City, _bandHometown));
        }

        /// <summary>
        /// A setlist's first gig is always played in the band's hometown, when it has one: wherever
        /// the setlist puts that gig, the band starts out at home.
        /// </summary>
        private bool IsOpeningGig(CareerTier tier)
        {
            BandHeaderText();
            return !string.IsNullOrWhiteSpace(_bandHometown) && _career is { Tiers: { Count: > 0 } } &&
                ReferenceEquals(_career.Tiers[0], tier);
        }

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
            _bandHometown = null;

            var careers = ScoreContainer.Careers;
            var band = CareerBands.ActiveBand(careers);
            if (band is null)
            {
                return null;
            }

            _bandHometown = band.Hometown;

            var credit = CareerBands.CurrentCredit(careers);
            _bandText = credit.Block == CareerCreditBlock.NoMemberPlaying
                ? Localize.KeyFormat("Menu.Career.HeaderBandNoMember", band.Name)
                : Localize.KeyFormat("Menu.Career.HeaderBand", band.Name);
            return _bandText;
        }
    }
}
