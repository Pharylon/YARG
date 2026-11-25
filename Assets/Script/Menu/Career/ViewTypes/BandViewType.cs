using System;
using System.Collections.Generic;
using YARG.Career;
using YARG.Localization;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The first row of the career list's last step: the band the careers are played as. Confirming
    /// it goes back to choose another band.
    /// </summary>
    public class BandViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Category;

        private readonly Action _onBandChanged;
        private readonly Guid? _bandId;
        private readonly string _primary;
        private readonly string _secondary;

        public BandViewType(Action onBandChanged)
        {
            _onBandChanged = onBandChanged;

            var careers = ScoreContainer.Careers;
            var band = CareerBands.ActiveBand(careers);
            if (band is null)
            {
                _primary = Localize.Key("Menu.Career.Band.NoBand");
                _secondary = Localize.Key("Menu.Career.Band.NoBandHint");
                return;
            }

            _bandId = band.Id;
            _primary = Localize.KeyFormat("Menu.Career.HeaderBand", band.Name);
            _secondary = Describe(careers, band);
        }

        /// <summary>
        /// "Hometown · Genre · Members: A, B · 1,234 fans (Local Act)", leaving out what is not set.
        /// </summary>
        public static string Describe(CareerDatabase careers, CareerBand band)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(band.Hometown))
            {
                parts.Add(band.Hometown);
            }

            if (!string.IsNullOrWhiteSpace(band.Genre))
            {
                parts.Add(band.Genre);
            }

            var members = CareerBands.MemberNames(careers, band.Id);
            parts.Add(Localize.KeyFormat("Menu.Career.Band.Members", members.Count == 0
                ? Localize.Key("Menu.Career.Band.NoMembers")
                : Localize.List(members)));

            var fans = CareerBands.BandFans(careers, band.Id, CareerBands.AllCareers());
            parts.Add(Localize.KeyFormat("Menu.Career.Band.FansRank", fans.ToString("N0"),
                CareerBandSheetDialogs.RankName(CareerFans.Rank(fans))));

            return string.Join("  ·  ", parts);
        }

        public override string GetPrimaryText(bool selected)
        {
            return FormatAs(_primary, TextType.Primary, selected);
        }

        public override string GetSecondaryText(bool selected)
        {
            return FormatAs(_secondary, TextType.Secondary, selected);
        }

        public override RowIcon CareerIcon => _bandId is null ? RowIcon.None : RowIcon.Person;

        public override UnityEngine.Sprite CustomIcon => _bandId is { } id ? CareerIcons.BandIcon(id) : null;

        public override void ViewClick()
        {
            _onBandChanged?.Invoke();
        }
    }
}
