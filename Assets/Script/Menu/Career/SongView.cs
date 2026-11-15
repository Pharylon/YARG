using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YARG.Localization;
using YARG.Menu.ListMenu;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The one pool object CareerMenu uses for every row kind (progress header, tier header, locked
    /// tier header, song), so it switches layout from the row's <see cref="ViewType.CareerInfo"/>.
    /// Every reference is optional: unwired parts are simply skipped and the row falls back to the
    /// primary/secondary text that <see cref="ViewObject{TViewType}"/> already fills in.
    /// </summary>
    public class SongView : ViewObject<ViewType>, IPointerClickHandler
    {
        // Song-only widgets (the star display). Deliberately not the primary/secondary text nodes:
        // tier headers use those too.
        [SerializeField]
        private GameObject _songExtrasContainer;

        [Space]
        // Optional. The node holding the *secondary* (artist / album) line; it is hidden on the career
        // header row, which uses the header widgets below instead. Pointing this at Primary Text blanks
        // the player names on row 0.
        [SerializeField]
        private GameObject _secondaryTextContainer;

        [Space]
        [Header("Career Header Row")]
        [SerializeField]
        private GameObject _headerContainer;
        [SerializeField]
        private TextMeshProUGUI _playerNamesText;
        [SerializeField]
        private TextMeshProUGUI _currentTierText;
        [SerializeField]
        private TextMeshProUGUI _tierStarsText;
        [SerializeField]
        private TextMeshProUGUI _totalStarsText;

        [Space]
        [Header("Song Row")]
        [SerializeField]
        private StarView _starView;

        [Space]
        [Header("Row Heights")]
        [SerializeField]
        private float _headerRowHeight = 70f;
        [SerializeField]
        private float _songRowHeight = 105f;

        [Space]
        [Header("Tier Groups")]
        // Each tier header and its songs are drawn inside one box. Since every row is its own pooled
        // object, each row draws its own slice of that box: the header the top edge, songs the
        // sides, and the last row of a tier the bottom edge.
        [SerializeField]
        private TextMeshProUGUI _primaryLabel;
        [SerializeField]
        private HorizontalLayoutGroup _infoLayout;
        [SerializeField]
        private RectTransform _backgroundsRoot;
        [SerializeField]
        private RectTransform _contentRoot;
        [SerializeField]
        private Color _groupBorderColor = new(1f, 1f, 1f, 0.35f);
        [SerializeField]
        private float _groupBorderWidth = 2f;
        // Horizontal inset of the box from the list edges, so the sides are not clipped.
        [SerializeField]
        private float _groupSideInset = 8f;
        // Space above each tier header, separating it from the previous tier's box.
        [SerializeField]
        private float _groupSpacing = 16f;
        [SerializeField]
        private float _tierHeaderFontSize = 36f;
        [SerializeField]
        private float _songIndent = 40f;

        private RectTransform _rect;

        private float _defaultPrimaryFontSize;
        private float _defaultPrimaryHeight;
        private LayoutElement _primaryLayout;
        private float _defaultPrimaryWidth;
        private int _defaultInfoPadding;

        private RectTransform _groupFrame;
        private Image _borderTop;
        private Image _borderBottom;

        // Clips the row's contents while it is shorter than full height during a tier animation.
        private RectMask2D _revealMask;

        private static readonly Type[] MarqueeScrollTypes = { typeof(SongViewType) };

        protected override bool AllowMarqueeScroll => true;
        protected override Type[] AllowedMarqueeScrollTypes => MarqueeScrollTypes;

        private void Awake()
        {
            _rect = GetComponent<RectTransform>();

            if (_primaryLabel != null)
            {
                _defaultPrimaryFontSize = _primaryLabel.fontSize;
                _defaultPrimaryHeight = _primaryLabel.rectTransform.rect.height;
                _primaryLayout = _primaryLabel.GetComponent<LayoutElement>();
                if (_primaryLayout != null)
                {
                    _defaultPrimaryWidth = _primaryLayout.preferredWidth;
                }
            }

            if (_infoLayout != null)
            {
                _defaultInfoPadding = _infoLayout.padding.left;
            }

            CreateGroupFrame();

            _revealMask = gameObject.AddComponent<RectMask2D>();
            _revealMask.enabled = false;
        }

        /// <summary>
        /// Clicking a tier header does what Confirm does on it: expand or collapse the tier.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Showing)
            {
                return;
            }

            var kind = ViewType?.GetCareerInfo()?.Kind;
            if (kind == ViewType.CareerRowKind.TierHeader)
            {
                OnClick();
            }
        }

        /// <summary>
        /// Optional: call from a button on the row prefab. Locked rows are selectable for browsing
        /// but start nothing.
        /// </summary>
        public void OnClick()
        {
            if (ViewType is not null && ViewType.IsClickable)
            {
                ViewType.ViewClick();
            }
        }

        public override void Show(bool selected, ViewType viewType)
        {
            base.Show(selected, viewType);

            var info = viewType?.GetCareerInfo();
            var kind = info?.Kind ?? ViewType.CareerRowKind.Song;
            var isSong = kind == ViewType.CareerRowKind.Song;
            var isCareerHeader = kind == ViewType.CareerRowKind.CareerHeader;

            var isTierHeader = !isSong && !isCareerHeader;
            var topGap = isTierHeader ? _groupSpacing : 0f;

            var reveal = isSong ? Mathf.Clamp01(info?.Reveal ?? 1f) : 1f;

            // Headers are a single summary line, songs get the full row.
            if (_rect != null)
            {
                _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    isSong || isCareerHeader ? _songRowHeight * reveal : _headerRowHeight + topGap);
            }

            if (_revealMask != null)
            {
                _revealMask.enabled = reveal < 1f;
            }

            ApplyTierGrouping(isTierHeader, isSong, info?.LastInGroup ?? false, topGap);

            SetActive(_songExtrasContainer, isSong);
            SetActive(_headerContainer, isCareerHeader);

            // Headers have no detail line of their own; the career header uses its own widgets.
            SetActive(_secondaryTextContainer, !isCareerHeader);

            if (isCareerHeader)
            {
                PopulateHeader(info.Value);
            }

            PopulateStars(info);
        }

        /// <summary>
        /// Sizes the tier header text, indents song names, and draws this row's slice of its tier's box.
        /// </summary>
        private void ApplyTierGrouping(bool isTierHeader, bool isSong, bool lastInGroup, float topGap)
        {
            if (_primaryLabel != null)
            {
                var fontSize = isTierHeader ? _tierHeaderFontSize : _defaultPrimaryFontSize;
                _primaryLabel.fontSize = fontSize;

                // The label truncates overflow, so a line taller than its rect is dropped entirely.
                // Grow the rect with the font.
                _primaryLabel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    _defaultPrimaryHeight * fontSize / _defaultPrimaryFontSize);
            }

            // The song name column gives up the width it is indented by, so the secondary column
            // stays lined up with the tier header's.
            var indent = isSong ? Mathf.RoundToInt(_songIndent) : 0;
            if (_infoLayout != null)
            {
                // Assigned rather than mutated in place so the layout group is marked dirty.
                var padding = _infoLayout.padding;
                _infoLayout.padding = new RectOffset(_defaultInfoPadding + indent, padding.right, padding.top,
                    padding.bottom);
            }

            if (_primaryLayout != null && _defaultPrimaryWidth > 0)
            {
                _primaryLayout.preferredWidth = _defaultPrimaryWidth - indent;
            }

            var grouped = isTierHeader || isSong;
            var sideInset = grouped ? _groupSideInset : 0f;

            // The selection highlight and text sit inside the box, below the gap above a header.
            SetInsets(_backgroundsRoot, sideInset, topGap);
            SetInsets(_contentRoot, sideInset, topGap);

            if (_groupFrame == null)
            {
                return;
            }

            _groupFrame.gameObject.SetActive(grouped);
            if (!grouped)
            {
                return;
            }

            SetInsets(_groupFrame, sideInset, topGap);

            _borderTop.enabled = isTierHeader;
            _borderBottom.enabled = lastInGroup;
        }

        private void CreateGroupFrame()
        {
            if (_rect == null)
            {
                return;
            }

            var frame = new GameObject("Tier Group Frame", typeof(RectTransform));
            _groupFrame = frame.GetComponent<RectTransform>();
            _groupFrame.SetParent(_rect, false);
            _groupFrame.anchorMin = Vector2.zero;
            _groupFrame.anchorMax = Vector2.one;
            _groupFrame.offsetMin = Vector2.zero;
            _groupFrame.offsetMax = Vector2.zero;

            _borderTop = CreateBorder("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -_groupBorderWidth));
            _borderBottom = CreateBorder("Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, _groupBorderWidth));
            CreateBorder("Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(_groupBorderWidth, 0));
            CreateBorder("Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-_groupBorderWidth, 0));
        }

        /// <summary>
        /// A solid strip along one edge of the frame; <paramref name="thickness"/> points inward.
        /// </summary>
        private Image CreateBorder(string edge, Vector2 anchorMin, Vector2 anchorMax, Vector2 thickness)
        {
            var border = new GameObject($"Border {edge}", typeof(RectTransform), typeof(Image));
            var rect = border.GetComponent<RectTransform>();
            rect.SetParent(_groupFrame, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(Mathf.Min(0, thickness.x), Mathf.Min(0, thickness.y));
            rect.offsetMax = new Vector2(Mathf.Max(0, thickness.x), Mathf.Max(0, thickness.y));

            var image = border.GetComponent<Image>();
            image.color = _groupBorderColor;
            image.raycastTarget = false;
            return image;
        }

        private static void SetInsets(RectTransform target, float side, float top)
        {
            if (target != null)
            {
                target.offsetMin = new Vector2(side, target.offsetMin.y);
                target.offsetMax = new Vector2(-side, -top);
            }
        }

        private void PopulateHeader(ViewType.CareerInfo info)
        {
            SetText(_playerNamesText, info.PlayerNames);
            SetText(_currentTierText, info.TierName);
            SetText(_tierStarsText, StarText(info.EarnedStars, info.AvailableStars));
            SetText(_totalStarsText, StarText(info.TotalEarnedStars, info.TotalAvailableStars));
        }

        /// <summary>
        /// Best stars for the song; 0 (nothing filled in) means it has not been completed yet. Header
        /// rows report their stars as text instead, so the display is hidden there.
        /// </summary>
        private void PopulateStars(ViewType.CareerInfo? info)
        {
            if (_starView == null)
            {
                return;
            }

            var showStars = info?.Kind == ViewType.CareerRowKind.Song;
            _starView.gameObject.SetActive(showStars);

            if (showStars)
            {
                _starView.SetStars(info.Value.BestStars);
            }
        }

        private static string StarText(int earned, int available)
        {
            return Localize.KeyFormat("Menu.Career.StarProgress", earned, available);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void SetText(TextMeshProUGUI target, string text)
        {
            if (target != null)
            {
                target.text = text ?? string.Empty;
            }
        }
    }
}
