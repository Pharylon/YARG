using YARG.Career;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The career header (name and description on the left). Careers that use fans add the save's fan
    /// total under the name and the selected tier's venue above the description; other careers show
    /// only their name and description, as before.
    /// </summary>
    public partial class CareerMenu
    {
        protected override void OnSelectedIndexChanged()
        {
            base.OnSelectedIndexChanged();

            if (_evaluation is { UsesFans: true })
            {
                UpdateHeader();
            }
        }

        private void UpdateHeader()
        {
            if (_career is null)
            {
                return;
            }

            var name = _career.Name;
            var description = _career.Description;

            if (_evaluation is { UsesFans: true })
            {
                var fans = Localize.KeyFormat("Menu.Career.HeaderFans", _evaluation.TotalFans.ToString("N0"));
                name = $"{name}\n<size=70%>{fans}</size>";

                var venue = VenueText(SelectedTier());
                if (!string.IsNullOrEmpty(venue))
                {
                    description = $"<b>{venue}</b>\n\n{description}";
                }
            }

            SetText(_careerNameText, name);
            SetText(_careerDescriptionText, description);
        }

        private CareerTier SelectedTier()
        {
            return CurrentSelection switch
            {
                TierViewType header => header.Tier,
                SongViewType song when song.TierIndex < _career.Tiers.Count => _career.Tiers[song.TierIndex],
                _ => null,
            };
        }

        /// <summary>
        /// "Venue, City", either one alone, or null if the tier names neither.
        /// </summary>
        private static string VenueText(CareerTier tier)
        {
            var venue = tier?.VenueName;
            var city = tier?.City;

            if (string.IsNullOrWhiteSpace(venue))
            {
                return string.IsNullOrWhiteSpace(city) ? null : city;
            }

            return string.IsNullOrWhiteSpace(city) ? venue : Localize.KeyFormat("Menu.Career.Venue", venue, city);
        }
    }
}
