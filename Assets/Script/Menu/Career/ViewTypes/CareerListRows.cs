using System;
using YARG.Career;
using YARG.Core.Game;
using YARG.Localization;
using YARG.Player;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// A band to play careers as (the first step of the career list).
    /// </summary>
    public class BandChoiceViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Normal;

        public CareerBand Band { get; }

        private readonly string _description;
        private readonly Action<CareerBand> _onSelect;

        public BandChoiceViewType(CareerBand band, Action<CareerBand> onSelect)
        {
            Band = band;
            _onSelect = onSelect;
            _description = BandViewType.Describe(ScoreContainer.Careers, band);
        }

        public override string GetPrimaryText(bool selected) => FormatAs(Band.Name, TextType.Primary, selected);

        public override string GetSecondaryText(bool selected) => FormatAs(_description, TextType.Secondary, selected);

        public override void ViewClick() => _onSelect?.Invoke(Band);

        public override RowIcon CareerIcon => RowIcon.Band;

        public override UnityEngine.Sprite CustomIcon => CareerIcons.BandIcon(Band.Id);

        public override string CareerIconText => CareerBandHomeLayout.BandInitials(Band.Name);
    }

    /// <summary>
    /// A row that does something when confirmed ("New band...", "Continue", ...).
    /// </summary>
    public class CareerActionViewType : ViewType
    {
        public override BackgroundType Background => _background;

        private readonly BackgroundType _background;
        private readonly string _primary;
        private readonly string _secondary;
        private readonly string _side;
        private readonly Action _onClick;

        /// <param name="side">With <see cref="BaseViewType.BackgroundType.Button"/>: the text on the
        /// row's right edge, as the music library's "Playlist Manager" row has.</param>
        public CareerActionViewType(string primary, string secondary, Action onClick,
            BackgroundType background = BackgroundType.Normal, string side = null)
        {
            _primary = primary;
            _secondary = secondary;
            _onClick = onClick;
            _background = background;
            _side = side;
        }

        public override string GetPrimaryText(bool selected) =>
            FormatAs(_primary, _background == BackgroundType.Button ? TextType.Bright : TextType.Primary, selected);

        public override string GetSideText(bool selected) => _side;

        /// <summary>
        /// The band whose icon the row shows (the lineup's band row), or null for none.
        /// </summary>
        public Guid? IconBand { get; set; }

        public override RowIcon CareerIcon => IconBand is null ? RowIcon.None : RowIcon.Band;

        public override UnityEngine.Sprite CustomIcon => IconBand is { } id ? CareerIcons.BandIcon(id) : null;

        public override string GetSecondaryText(bool selected) => FormatAs(_secondary ?? string.Empty, TextType.Secondary, selected);

        public override void ViewClick() => _onClick?.Invoke();
    }

    /// <summary>
    /// A section divider, drawn like the music library's sort headers (shaded, bright title, a count on
    /// the right). It cannot be selected.
    /// </summary>
    public class CareerDividerViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Category;

        public override bool IsSelectable => false;

        public override bool IsClickable => false;

        private readonly string _title;
        private readonly string _count;

        public CareerDividerViewType(string title, string count)
        {
            _title = title;
            _count = count;
        }

        public override string GetPrimaryText(bool selected) => FormatAs(_title, TextType.Bright, selected);

        public override string GetSecondaryText(bool selected) => string.Empty;

        public override string GetSideText(bool selected) => _count;
    }

    /// <summary>
    /// A profile on the lineup step: connected (playing) or not; confirming toggles it.
    /// </summary>
    public class LineupProfileViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Normal;

        public YargProfile Profile { get; }

        private readonly bool _isMember;
        private readonly Action<YargProfile> _onToggle;

        public LineupProfileViewType(YargProfile profile, bool isMember, Action<YargProfile> onToggle)
        {
            Profile = profile;
            _isMember = isMember;
            _onToggle = onToggle;
        }

        public override string GetPrimaryText(bool selected) => FormatAs(Profile.Name, TextType.Primary, selected);

        public override RowIcon CareerIcon => Profile.IsBot ? RowIcon.Bot : RowIcon.Person;

        public override UnityEngine.Sprite CustomIcon => CareerIcons.ProfilePicture(Profile);

        public override bool? ConnectedState => PlayerContainer.GetPlayerFromProfile(Profile) is not null;

        public override string GetSecondaryText(bool selected)
        {
            var player = PlayerContainer.GetPlayerFromProfile(Profile);
            var role = Localize.Key(Profile.IsBot
                ? "Menu.Career.Lineup.Bot"
                : _isMember ? "Menu.Career.Lineup.Member" : "Menu.Career.Lineup.Guest");

            // The connect button shows whether it is connected; a connected player without a
            // controller is worth a word.
            var text = player is { IsMissingController: true }
                ? Localize.KeyFormat("Menu.Career.HeaderJoin", role, Localize.Key("Menu.Career.Lineup.NoController"))
                : role;

            return FormatAs(text, TextType.Secondary, selected);
        }

        public override void ViewClick() => _onToggle?.Invoke(Profile);
    }
}
