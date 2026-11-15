using System.Linq;
using YARG.Career;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The career header (name and description on the left). Careers that use fans add the save's fan
    /// total under the name and the selected tier's venue above the description; other careers show
    /// only their name and description, as before. When a band is active its name goes on the line
    /// under the career name (see CareerMenu.Bands.cs).
    /// </summary>
    public partial class CareerMenu
    {
        protected override void OnSelectedIndexChanged()
        {
            base.OnSelectedIndexChanged();

            if (_evaluation is { UsesFans: true } || HasContracts())
            {
                UpdateHeader();
            }
        }

        private bool HasContracts()
        {
            return _career?.Tiers.Any(tier => tier.Contract is not null || tier.Songs?.Any(song => song.Contract is not null) == true)
                ?? false;
        }

        private void UpdateHeader()
        {
            if (_career is null)
            {
                return;
            }

            var name = _career.Name;
            var description = _career.Description;

            // Second line: the band playing (if any), then the save's fans (fan careers only).
            var subtitle = BandHeaderText();

            // The selected row's contract, so it is known before the song is picked.
            if (CareerContractText.Line(SelectedContract(), _evaluation?.UsesFans ?? false) is { } contract)
            {
                description = $"<b>{Localize.KeyFormat("Menu.Career.Contract.Upcoming", contract)}</b>\n\n{description}";
            }

            if (_evaluation is { UsesFans: true })
            {
                var fans = Localize.KeyFormat("Menu.Career.HeaderFans", _evaluation.TotalFans.ToString("N0"));
                subtitle = string.IsNullOrEmpty(subtitle)
                    ? fans
                    : Localize.KeyFormat("Menu.Career.HeaderJoin", subtitle, fans);

                var venue = CareerBandSheetDialogs.VenueText(SelectedTier());
                if (!string.IsNullOrEmpty(venue) && IsHometown(SelectedTier()))
                {
                    venue = Localize.KeyFormat("Menu.Career.HeaderJoin", venue, Localize.Key("Menu.Career.Hometown"));
                }

                if (!string.IsNullOrEmpty(venue))
                {
                    description = $"<b>{venue}</b>\n\n{description}";
                }
            }

            if (!string.IsNullOrEmpty(subtitle))
            {
                name = $"{name}\n<size=70%>{subtitle}</size>";
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

        private CareerContract SelectedContract()
        {
            return CurrentSelection switch
            {
                TierViewType header => header.Tier.Contract is { IsActive: true } contract ? contract : null,
                SongViewType song when song.TierIndex < _career.Tiers.Count =>
                    CareerContracts.For(_career.Tiers[song.TierIndex], song.Song),
                _ => null,
            };
        }
    }
}
