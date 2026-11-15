using System;
using YARG.Career;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// A tier header row. Unlocked tiers report how much of the tier's star total has been earned;
    /// the next locked tier reports what is still required to reach it and cannot be clicked.
    /// Clicking an unlocked tier expands or collapses its songs.
    /// </summary>
    public class TierViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Category;

        private readonly CareerTier                _tier;
        private readonly CareerEvaluation.TierResult _result;
        private readonly string                    _lockHint;
        private readonly bool                      _collapsed;
        private readonly Action<CareerTier>        _onToggle;

        public TierViewType(CareerTier tier, CareerEvaluation.TierResult result, bool collapsed = false,
            Action<CareerTier> onToggle = null)
        {
            _tier = tier;
            _result = result;
            _collapsed = collapsed;
            _onToggle = onToggle;
            IsLocked = _result is null || !_result.Unlocked;
            _lockHint = IsLocked ? BuildLockHint() : null;
        }

        private bool IsLocked { get; }

        public CareerTier Tier => _tier;

        /// <summary>
        /// The career uses fans: the header also shows the fans the tier's songs earned.
        /// </summary>
        public bool ShowFans { get; set; }

        // A locked tier is still selectable so the list can be browsed, but it starts nothing.
        public override bool IsClickable => !IsLocked;

        public override void ViewClick()
        {
            if (!IsLocked)
            {
                _onToggle?.Invoke(_tier);
            }
        }

        public override CareerInfo? GetCareerInfo()
        {
            return new CareerInfo
            {
                Kind           = IsLocked ? CareerRowKind.LockedTierHeader : CareerRowKind.TierHeader,
                Unlocked       = !IsLocked,
                Completed      = _result?.Completed ?? false,
                EarnedStars    = _result?.StarSum ?? 0,
                AvailableStars = _tier.Songs?.Length * STARS_PER_SONG ?? 0,
                TierName       = _tier.Name,
                LockHint       = _lockHint,
                // Nothing is listed under a locked or collapsed tier, so its header closes the box.
                LastInGroup    = IsLocked || _collapsed || (_tier.Songs?.Length ?? 0) == 0,
                Reveal         = 1f,
            };
        }

        public override string GetPrimaryText(bool selected)
        {
            var name = _tier.IsBonus ? Localize.KeyFormat("Menu.Career.BonusTier", _tier.Name) : _tier.Name;
            return FormatAs(name, TextType.Primary, selected);
        }

        public override string GetSecondaryText(bool selected)
        {
            string text;

            if (IsLocked)
            {
                text = _lockHint;
            }
            else
            {
                text = _tier.UnlockType switch
                {
                    UnlockType.StarCount => Localize.KeyFormat("Menu.Career.StarProgress", _result?.StarSum ?? 0, _tier.Songs?.Length * STARS_PER_SONG ?? 0),
                    UnlockType.CompletionCount => Localize.KeyFormat("Menu.Career.StarProgress", _result?.RemainingToUnlock ?? 0, _tier.Songs?.Length ?? 0),
                    UnlockType.FanCount => Localize.KeyFormat("Menu.Career.StarProgress", _result?.StarSum ?? 0, _tier.Songs?.Length * STARS_PER_SONG ?? 0),
                    _ => "Unknown Unlock Type (how?)"
                };

                if (ShowFans)
                {
                    text = Localize.KeyFormat("Menu.Career.TierFans", text, (_result?.Fans ?? 0).ToString("N0"));
                }
            }

            return FormatAs(text, TextType.Secondary, selected);
        }

        /// <summary>
        /// What is still missing for this tier.
        /// </summary>
        private string BuildLockHint()
        {
            var remaining = Math.Max(0, _result?.RemainingToUnlock ?? 0);

            return _tier.UnlockType switch
            {
                UnlockType.StarCount =>
                    Localize.KeyFormat("Menu.Career.UnlockStars", remaining),
                UnlockType.CompletionCount =>
                    Localize.KeyFormat("Menu.Career.UnlockSongs", remaining),
                UnlockType.FanCount =>
                    Localize.KeyFormat("Menu.Career.UnlockFans", remaining.ToString("N0")),
                _ => Localize.Key("Menu.Career.Locked"),
            };
        }

        public override string ToString()
        {
            return $"TierViewType: {_tier.Name}{(IsLocked ? " (locked)" : "")}";
        }
    }
}
