using System;
using System.Collections.Generic;
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
        // Prefab asset. When set (and _starView is not wired), song rows get the star display on
        // their right edge, built in code.
        [SerializeField]
        private StarView _starViewPrefab;
        [SerializeField]
        private Vector2 _starViewSize = new(136f, 25f);
        [SerializeField]
        private float _scoreRightMargin = 20f;
        [SerializeField]
        private float _scoreSpacing = 10f;

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
        private float _songIndent = 0f;

        private RectTransform _rect;

        private float _defaultPrimaryFontSize;
        private float _defaultPrimaryHeight;
        private LayoutElement _primaryLayout;
        private float _defaultPrimaryWidth;
        private int _defaultInfoPadding;
        private int _defaultInfoPaddingRight;

        // The secondary column: beside the name on a song row, pushed to the right edge on a gig header.
        private TextMeshProUGUI _secondaryLabel;
        private LayoutElement _secondaryLayout;
        private float _defaultSecondaryWidth;
        private TextAlignmentOptions _defaultSecondaryAlignment;

        // Between a gig header's progress text and the right edge of its box.
        private const int HEADER_RIGHT_PADDING = 24;

        // The stars and score displays on a song row's right edge.
        private RectTransform _scoreRoot;
        private float _scoreWidth;

        // Share of a song row's text width the song name column takes; a longer name is cut short
        // ("...") so the artists of every row line up.
        private const float SONG_NAME_COLUMN = 0.55f;
        // More than any row is wide: the column that has it takes whatever the others leave.
        private const float REST_OF_ROW = 5000f;

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

            if (_secondaryTextContainer != null &&
                _secondaryTextContainer.TryGetComponent(out _secondaryLabel))
            {
                _defaultSecondaryAlignment = _secondaryLabel.alignment;
                _secondaryLayout = _secondaryLabel.GetComponent<LayoutElement>();
                if (_secondaryLayout != null)
                {
                    _defaultSecondaryWidth = _secondaryLayout.preferredWidth;
                }
            }

            if (_infoLayout != null)
            {
                _defaultInfoPadding = _infoLayout.padding.left;
                _defaultInfoPaddingRight = _infoLayout.padding.right;
            }

            CreateScoreRoot();

            CreateGroupFrame();

            _revealMask = gameObject.AddComponent<RectMask2D>();
            _revealMask.enabled = false;
        }

        /// <summary>
        /// Clicking a tier header expands or collapses the tier. In the career menu, clicking any
        /// other row selects it and does what Confirm does on it, as in the music library: a song
        /// goes on to be played.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Showing || ViewType is null)
            {
                return;
            }

            // Selected first, so the menu remembers this row as the one that was played. The rows are
            // not parented to the menu, and the same row object is listed elsewhere (dialogs), so the
            // menu is asked whether the row is one of its own.
            // Selecting scrolls the list, which hands this pooled object another row: the clicked row
            // is kept aside first.
            var row = ViewType;
            var menu = FindAnyObjectByType<CareerMenu>();
            if (menu != null && menu.Select(row))
            {
                if (row.IsClickable)
                {
                    row.ViewClick();
                }

                return;
            }

            // Rows listed elsewhere keep their own buttons.
            if (ViewType.GetCareerInfo()?.Kind == ViewType.CareerRowKind.TierHeader)
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

        // The row's icon slot (a song's source icon), found in the info layout.
        private GameObject _iconSlot;
        private Image _iconSlotImage;

        public override void Show(bool selected, ViewType viewType)
        {
            base.Show(selected, viewType);

            var info = viewType?.GetCareerInfo();
            var kind = info?.Kind ?? ViewType.CareerRowKind.Song;
            var isSong = kind == ViewType.CareerRowKind.Song;
            var isCareerHeader = kind == ViewType.CareerRowKind.CareerHeader;

            var isTierHeader = !isSong && !isCareerHeader;

            // A row in a gig's song list without an icon (the closer) keeps the icon's room, so its texts
            // line up with the song names and artists above it.
            KeepIconSlot(isSong);
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

            // Before the grouping, which reserves the width the stars and scores take.
            PopulateScores(info);

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
                // Song text stops short of the stars and scores on the right.
                var padding = _infoLayout.padding;
                var right = _defaultInfoPaddingRight +
                    (isSong ? Mathf.CeilToInt(_scoreWidth) : isTierHeader ? HEADER_RIGHT_PADDING : 0);
                _infoLayout.padding = new RectOffset(_defaultInfoPadding + indent, right, padding.top,
                    padding.bottom);
            }

            _columnsHeader = isTierHeader;
            _columnsSong = isSong;
            _columnsIndent = indent;
            LayoutColumns(isTierHeader, isSong, indent);

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

            _borderTop.enabled = isTierHeader || (ViewType?.FirstInGroup ?? false);
            _borderBottom.enabled = lastInGroup;
        }

        /// <summary>
        /// Splits the row between its two texts. A gig header: the progress ("20 / 25 · 814 fans") keeps
        /// exactly what it needs at the right edge and the name gets the rest. A song: the name column is
        /// a fixed share of the row (a longer name ends in "..."), so the artists line up, and the artist
        /// gets the rest.
        /// </summary>
        private void LayoutColumns(bool isTierHeader, bool isSong, int indent)
        {
            if (_primaryLabel == null || _primaryLayout == null || _secondaryLabel == null ||
                _secondaryLayout == null || _infoLayout == null)
            {
                return;
            }

            if (isTierHeader)
            {
                var needed = Mathf.Ceil(_secondaryLabel.GetPreferredValues(_secondaryLabel.text).x) + 2f;
                Column(_secondaryLayout, needed, needed);
                Column(_primaryLayout, 0f, REST_OF_ROW);
                _secondaryLabel.alignment = TextAlignmentOptions.Right;
                return;
            }

            _secondaryLabel.alignment = _defaultSecondaryAlignment;
            if (!isSong)
            {
                Column(_primaryLayout, -1f, _defaultPrimaryWidth - indent);
                Column(_secondaryLayout, -1f, _defaultSecondaryWidth);
                return;
            }

            // What the two texts share: the row less its padding, the icon and the gaps.
            var padding = _infoLayout.padding;
            _columnsWidth = ((RectTransform) _infoLayout.transform).rect.width;
            var width = _columnsWidth - padding.left - padding.right;
            var columns = 2;
            foreach (RectTransform child in _infoLayout.transform)
            {
                if (child == _primaryLabel.rectTransform || child == _secondaryLabel.rectTransform ||
                    !child.gameObject.activeSelf)
                {
                    continue;
                }

                columns++;
                width -= child.TryGetComponent<LayoutElement>(out var element) ? Mathf.Max(0f, element.minWidth) : 0f;
            }

            width -= _infoLayout.spacing * (columns - 1);

            // A row without stars (the closer) has the stars' room to spare: its columns are measured as
            // if it had them, so its texts line up with the songs above.
            width -= Mathf.Ceil(SongScoreWidth()) - (padding.right - _defaultInfoPaddingRight);

            var name = Mathf.Max(0f, Mathf.Floor(width * SONG_NAME_COLUMN));
            Column(_primaryLayout, name, name);
            Column(_secondaryLayout, 0f, REST_OF_ROW);
        }

        // What the columns were last laid out for, and the row width they were worked out from.
        private bool _columnsHeader;
        private bool _columnsSong;
        private int _columnsIndent;
        private float _columnsWidth;

        /// <summary>
        /// A song row's columns depend on the row's width, which is not final when a row is first
        /// shown (a new row still has its prefab's width): they are worked out again once it changes.
        /// </summary>
        private void Update()
        {
            if (!Showing || !_columnsSong || _infoLayout == null)
            {
                return;
            }

            var width = ((RectTransform) _infoLayout.transform).rect.width;
            if (Mathf.Abs(width - _columnsWidth) > 0.5f)
            {
                LayoutColumns(_columnsHeader, _columnsSong, _columnsIndent);
            }
        }

        private static void Column(LayoutElement element, float minWidth, float preferredWidth)
        {
            element.minWidth = minWidth;
            element.preferredWidth = preferredWidth;
            element.flexibleWidth = -1f;
        }

        private void KeepIconSlot(bool reserve)
        {
            if (_iconSlot == null && _infoLayout != null)
            {
                var slot = _infoLayout.transform.Find("Icon");
                if (slot != null)
                {
                    _iconSlot = slot.gameObject;
                    _iconSlotImage = slot.GetComponent<Image>();
                }
            }

            if (_iconSlot == null || _iconSlotImage == null)
            {
                return;
            }

            if (!_iconSlot.activeSelf && reserve)
            {
                _iconSlot.SetActive(true);
                _iconSlotImage.enabled = false;
            }
            else if (_iconSlot.activeSelf)
            {
                _iconSlotImage.enabled = true;
            }
        }

        private void CreateScoreRoot()
        {
            if (_rect == null || _starView != null || _starViewPrefab == null)
            {
                return;
            }

            var root = new GameObject("Song Scores", typeof(RectTransform));
            _scoreRoot = root.GetComponent<RectTransform>();
            _scoreRoot.SetParent(_contentRoot != null ? _contentRoot : _rect, false);
            _scoreRoot.anchorMin = new Vector2(1, 0);
            _scoreRoot.anchorMax = new Vector2(1, 1);
            _scoreRoot.pivot = new Vector2(1, 0.5f);
            _scoreRoot.offsetMin = Vector2.zero;
            _scoreRoot.offsetMax = Vector2.zero;

            if (_starViewPrefab != null)
            {
                _starView = Instantiate(_starViewPrefab, _scoreRoot);
                PlaceFromRight((RectTransform) _starView.transform, _scoreRightMargin, _starViewSize);
            }
        }

        /// <summary>
        /// Anchors the child to the right edge of the score area, vertically centered.
        /// </summary>
        private static void PlaceFromRight(RectTransform child, float right, Vector2 size)
        {
            child.anchorMin = new Vector2(1, 0.5f);
            child.anchorMax = new Vector2(1, 0.5f);
            child.pivot = new Vector2(1, 0.5f);
            child.sizeDelta = size;
            child.anchoredPosition = new Vector2(-right, 0);
        }

        /// <summary>
        /// Song rows: one instrument/difficulty display per player of the best result, to the left of
        /// the stars. A single player also shows the percent, as in the music library.
        /// </summary>
        private void PopulateScores(ViewType.CareerInfo? info)
        {
            _scoreWidth = 0f;
            if (_scoreRoot == null)
            {
                return;
            }

            var isSong = info?.Kind == ViewType.CareerRowKind.Song && !info.Value.NoStars;
            _scoreRoot.gameObject.SetActive(isSong);
            if (!isSong)
            {
                return;
            }

            _scoreWidth = SongScoreWidth();
        }

        /// <summary>The room the stars take at a song row's right edge.</summary>
        private float SongScoreWidth()
        {
            var right = _scoreRightMargin;
            if (_starViewPrefab != null)
            {
                right += _starViewSize.x + _scoreSpacing;
            }

            return right;
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

            var showStars = info?.Kind == ViewType.CareerRowKind.Song && !info.Value.NoStars;
            _starView.gameObject.SetActive(showStars);

            if (showStars)
            {
                if (info.Value.BestIsGold)
                {
                    _starView.SetStars(ViewType.STARS_PER_SONG, StarView.StarType.Gold);
                }
                else
                {
                    _starView.SetStars(info.Value.BestStars);
                }
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
