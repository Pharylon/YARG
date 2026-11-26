using System;
using YARG.Career;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The first row under a gig played as a show (<see cref="CareerCompletionMode.FullTierPlaylist"/>):
    /// starts the whole set (see <see cref="CareerShow"/>). Drawn like a song row, without stars.
    /// </summary>
    public class ShowViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Normal;

        private readonly string _primary;
        private readonly string _secondary;
        private readonly bool _lastInTier;
        private readonly Action _onClick;

        public Guid TierId { get; }

        /// <summary>
        /// Driven by <see cref="CareerMenu"/> while the tier expands or collapses.
        /// </summary>
        public float Reveal { get; set; } = 1f;

        public ShowViewType(Guid tierId, string primary, string secondary, bool lastInTier, Action onClick)
        {
            TierId = tierId;
            _primary = primary;
            _secondary = secondary;
            _lastInTier = lastInTier;
            _onClick = onClick;
        }

        public override CareerInfo? GetCareerInfo()
        {
            return new CareerInfo
            {
                Kind = CareerRowKind.Song,
                Unlocked = true,
                NoStars = true,
                LastInGroup = _lastInTier,
                Reveal = Reveal,
            };
        }

        public override string GetPrimaryText(bool selected)
        {
            return FormatAs(_primary, TextType.Primary, selected);
        }

        public override string GetSecondaryText(bool selected)
        {
            return FormatAs(_secondary, TextType.Secondary, selected);
        }

        public override void ViewClick()
        {
            _onClick?.Invoke();
        }

        /// <summary>"Play the full set" / "4 songs, one after another".</summary>
        public static ShowViewType FullSet(CareerTier tier, int songCount, bool lastInTier, Action onClick)
        {
            return new ShowViewType(tier.Id, Localize.Key("Menu.Career.Show.Play"),
                Localize.KeyFormat("Menu.Career.Show.PlayDetail", songCount), lastInTier, onClick);
        }
    }
}
