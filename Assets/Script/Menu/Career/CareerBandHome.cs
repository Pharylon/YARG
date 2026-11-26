using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Career;
using YARG.Localization;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The band's home screen: its picture, name and rank, its stars as a row of medals, and its fans
    /// as a crowd along the bottom. Built in code inside the career list's left panel.
    ///
    /// It is a stack of sections laid out from the panel's size (hero, rank, stars from the top; the
    /// crowd pinned to the bottom) with a free band between the stars and the crowd
    /// (<see cref="_extras"/>) kept for what the screen grows next: tour info, the band's transport, ...
    /// </summary>
    public class CareerBandHome : MonoBehaviour
    {
        public struct Art
        {
            public Sprite Star;
            public Sprite GoldStar;
            public Sprite[] Fans;
            // Cloned for the panel's texts so they match the menu's fonts.
            public TextMeshProUGUI TitleTemplate;
            public TextMeshProUGUI BodyTemplate;
        }

        private const float FAN_ASPECT = 2f / 3f;

        private const float INTRO_SECONDS = 0.9f;
        private const float STAR_POP_SECONDS = 0.28f;
        private const float COUNT_SECONDS = 0.8f;

        private static readonly Color GoldText = new(1f, 0.82f, 0.16f);
        private static readonly Color DimText = new(0.62f, 0.78f, 0.9f);
        private static readonly Color WarningText = new(1f, 0.75f, 0.3f);
        private static readonly Color BarBack = new(0f, 0f, 0f, 0.35f);
        private static readonly Color BarFill = new(0.18f, 0.85f, 1f);
        private static readonly Color BackRowTint = new(0.42f, 0.52f, 0.68f);

        private Art _art;
        private RectTransform _rect;

        // Hero
        private Image _icon;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _tagline;

        // Rank
        private TextMeshProUGUI _rank;
        private RectTransform _barBack;
        private RectTransform _barFill;
        private TextMeshProUGUI _nextRank;

        // Stars
        private RectTransform _starRow;
        private readonly List<Image> _stars = new();
        private List<CareerBandHomeLayout.Placed> _starLayout = new();
        private TextMeshProUGUI _starCount;

        // Room for what comes next (tour info, transport, ...).
        private RectTransform _extras;
        private TextMeshProUGUI _notice;

        // Crowd
        private RectTransform _crowdArea;
        private readonly List<Image> _fans = new();
        private List<CareerBandHomeLayout.Placed> _crowdLayout = new();
        private TextMeshProUGUI _fanCount;
        private RectTransform _fanPlate;

        private int _totalStars;
        private int _totalFans;
        private float _rankFraction;
        private bool _iconFlipped;
        private Vector2 _laidOutSize;
        private float _shownAt;
        private System.Guid? _shownBand;

        // What each band had when a home screen last showed it. Kept for the session (the menu scene is
        // rebuilt after every song), so coming back with more shows the gain arriving.
        private static readonly Dictionary<System.Guid, (int stars, int fans)> _seen = new();

        // The gain animation: what the band had before, and when the new stars start to arrive.
        private bool _gaining;
        private int _starsBefore;
        private int _fansBefore;
        private float _rankFractionBefore;
        private float _gainAt;
        // Per icon: its turn among the new ones (0, 1, ...), or -1 for one the band already had.
        private readonly List<int> _starTurn = new();
        private readonly List<int> _fanTurn = new();
        private int _newStars;
        private int _newFans;

        // The scene is still fading in when the menu first shows.
        private const float GAIN_DELAY_SECONDS = 0.7f;
        private const float GAIN_STAR_STEP = 0.24f;
        private const float GAIN_FAN_STEP = 0.14f;
        // The longest either run of new icons may take, however many there are.
        private const float GAIN_RUN_SECONDS = 3f;
        private const float GAIN_MIN_COUNT_SECONDS = 0.6f;
        private const float FAN_POP_SECONDS = 0.3f;

        public static CareerBandHome Create(RectTransform parent, Art art, float bottomInset)
        {
            var root = new GameObject("Band Home", typeof(RectTransform), typeof(LayoutElement));
            root.GetComponent<LayoutElement>().ignoreLayout = true;

            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            // The panel runs on under the button bar at the bottom of the screen; the home screen stops
            // above it.
            rect.offsetMin = new Vector2(0f, bottomInset);
            rect.offsetMax = Vector2.zero;

            var home = root.AddComponent<CareerBandHome>();
            home._art = art;
            home._rect = rect;
            home.Build();
            return home;
        }

        private void Build()
        {
            // Back to front: the crowd is behind everything else.
            _crowdArea = Child("Crowd");
            _fanPlate = Child("Fan Count Plate");
            var plate = _fanPlate.gameObject.AddComponent<Image>();
            plate.color = new Color(0.01f, 0.04f, 0.09f, 0.82f);
            plate.raycastTarget = false;
            _fanCount = Text("Fan Count", _art.TitleTemplate, Color.white);

            _icon = Child("Band Icon").gameObject.AddComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            _name = Text("Band Name", _art.TitleTemplate, Color.white);
            _tagline = Text("Tagline", _art.BodyTemplate, DimText);

            _rank = Text("Rank", _art.TitleTemplate, GoldText);
            _barBack = Child("Rank Bar");
            _barBack.gameObject.AddComponent<Image>().color = BarBack;
            _barFill = Child("Fill", _barBack);
            _barFill.gameObject.AddComponent<Image>().color = BarFill;
            foreach (var image in _barBack.GetComponentsInChildren<Image>())
            {
                image.raycastTarget = false;
            }

            _nextRank = Text("Next Rank", _art.BodyTemplate, DimText);

            _starRow = Child("Stars");
            _starCount = Text("Star Count", _art.TitleTemplate, Color.white);

            _extras = Child("Extras");
            _notice = Text("Notice", _art.BodyTemplate, WarningText);
        }

        private RectTransform Child(string childName, RectTransform parent = null)
        {
            var child = new GameObject(childName, typeof(RectTransform)).GetComponent<RectTransform>();
            child.SetParent(parent != null ? parent : _rect, false);
            // Everything is placed from the panel's top-left corner.
            child.anchorMin = child.anchorMax = new Vector2(0, 1);
            child.pivot = new Vector2(0.5f, 0.5f);
            return child;
        }

        private TextMeshProUGUI Text(string childName, TextMeshProUGUI template, Color color)
        {
            var text = Instantiate(template, _rect);
            text.name = childName;
            if (text.TryGetComponent<LayoutElement>(out var layout))
            {
                layout.ignoreLayout = true;
            }

            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);

            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.color = color;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        /// <summary>
        /// Shows the band. The numbers count up and the stars and crowd make their entrance when the
        /// band (or what it has earned) changed since the panel last showed it.
        /// </summary>
        public void Show(CareerBand band, CareerBandSheet sheet, Sprite icon, bool iconFlipped, string notice)
        {
            gameObject.SetActive(true);

            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _iconFlipped = iconFlipped;

            _name.text = band.Name;

            var places = new List<string>();
            if (!string.IsNullOrWhiteSpace(band.Hometown))
            {
                places.Add(band.Hometown);
            }

            _tagline.text = string.Join("  ·  ", places);

            _rank.text = CareerBandSheetDialogs.RankName(sheet.Rank).ToUpperInvariant();
            var rankFloor = CareerFans.RankFans(sheet.Rank);
            var rankSpan = 0;
            if (sheet.NextRank is { } next)
            {
                _nextRank.text = Localize.KeyFormat("Menu.Career.Sheet.NextRank", next.fansNeeded.ToString("N0"),
                    CareerBandSheetDialogs.RankName(next.rank));
                rankSpan = Mathf.Max(1, sheet.Fans + next.fansNeeded - rankFloor);
                _rankFraction = Mathf.Clamp01((sheet.Fans - rankFloor) / (float) rankSpan);
            }
            else
            {
                _nextRank.text = Localize.Key("Menu.Career.Home.TopRank");
                _rankFraction = 1f;
            }

            _notice.text = notice ?? string.Empty;

            var changed = _shownBand != band.Id || _totalStars != sheet.TotalStars || _totalFans != sheet.Fans;
            _shownBand = band.Id;
            _totalStars = Mathf.Max(0, sheet.TotalStars);
            _totalFans = Mathf.Max(0, sheet.Fans);

            // The band earned something since a home screen last showed it (a song was played): what
            // it had is there at once, and the new stars and fans arrive one at a time.
            var gained = _seen.TryGetValue(band.Id, out var before) &&
                _totalStars >= before.stars && _totalFans >= before.fans &&
                (_totalStars > before.stars || _totalFans > before.fans);
            _seen[band.Id] = (_totalStars, _totalFans);

            if (gained)
            {
                _gaining = true;
                _starsBefore = before.stars;
                _fansBefore = before.fans;
                // On the bar of the rank the band has now: a band that just ranked up starts it empty.
                _rankFractionBefore = rankSpan > 0
                    ? Mathf.Clamp01((before.fans - rankFloor) / (float) rankSpan)
                    : _rankFraction;
                _gainAt = Time.unscaledTime + GAIN_DELAY_SECONDS;
                _laidOutSize = Vector2.zero;
            }
            else if (changed)
            {
                _gaining = false;
                _shownAt = Time.unscaledTime;
                _laidOutSize = Vector2.zero;
            }

            LayoutIfNeeded();
            Animate();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// The new stars and fans of a just-played song are still arriving.
        /// </summary>
        public bool IsGaining => _gaining && isActiveAndEnabled;

        private void OnEnable()
        {
            // Coming back to the screen replays the entrance. A gain that was set up while the menu was
            // still hidden (it is filled in before it is shown) starts now instead.
            _shownAt = Time.unscaledTime;
            if (_gaining)
            {
                _gainAt = Time.unscaledTime + GAIN_DELAY_SECONDS;
            }
        }

        private void Update()
        {
            LayoutIfNeeded();
            Animate();
        }

        private void LayoutIfNeeded()
        {
            var size = _rect.rect.size;
            if (size.x < 1f || size.y < 1f || (size - _laidOutSize).sqrMagnitude < 0.25f)
            {
                return;
            }

            _laidOutSize = size;
            Layout(size.x, size.y);
        }

        /// <summary>
        /// Top-down: hero (picture, name, tagline), rank (title, progress to the next), stars (medals,
        /// count), then the free band; the crowd takes the bottom of the panel under all of it.
        /// </summary>
        private void Layout(float width, float height)
        {
            var centerX = width / 2f;
            var unit = height / 100f;
            var y = 4f * unit;

            var iconSize = Mathf.Min(24f * unit, width * 0.34f);
            Place(_icon.rectTransform, centerX, y + iconSize / 2f, iconSize, iconSize);
            _icon.rectTransform.localScale = new Vector3(1f, _iconFlipped ? -1f : 1f, 1f);
            y += iconSize + 1.5f * unit;

            Place(_name.rectTransform, centerX, y + 3.5f * unit, width * 0.9f, 7f * unit);
            _name.enableAutoSizing = true;
            _name.fontSizeMax = 6.2f * unit;
            _name.fontSizeMin = 2.4f * unit;
            y += 7f * unit;

            if (!string.IsNullOrEmpty(_tagline.text))
            {
                Place(_tagline.rectTransform, centerX, y + 1.6f * unit, width * 0.9f, 3.2f * unit);
                _tagline.fontSize = 2.4f * unit;
                y += 3.4f * unit;
            }

            _tagline.gameObject.SetActive(!string.IsNullOrEmpty(_tagline.text));

            y += 1.2f * unit;
            Place(_rank.rectTransform, centerX, y + 1.9f * unit, width * 0.9f, 3.8f * unit);
            _rank.fontSize = 3.1f * unit;
            _rank.characterSpacing = 12f;
            y += 4.2f * unit;

            var barWidth = width * 0.5f;
            var barHeight = Mathf.Max(4f, 0.9f * unit);
            Place(_barBack, centerX, y + barHeight / 2f, barWidth, barHeight);
            _barFill.anchorMin = new Vector2(0, 0);
            _barFill.anchorMax = new Vector2(0, 1);
            _barFill.pivot = new Vector2(0, 0.5f);
            _barFill.anchoredPosition = Vector2.zero;
            y += barHeight + 0.6f * unit;

            Place(_nextRank.rectTransform, centerX, y + 1.4f * unit, width * 0.9f, 2.8f * unit);
            _nextRank.fontSize = 2f * unit;
            y += 3.4f * unit;

            // Stars
            y += 1.5f * unit;
            var starMax = 8.5f * unit;
            var rowHeight = starMax * 1.5f;
            var rowWidth = width * 0.84f;
            Place(_starRow, centerX, y + rowHeight / 2f, rowWidth, rowHeight);
            _starLayout = CareerBandHomeLayout.Stars(_totalStars, rowWidth, starMax, starMax * 0.62f);
            Pool(_stars, _starLayout.Count, _starRow);
            for (var i = 0; i < _starLayout.Count; i++)
            {
                var star = _starLayout[i];
                var image = _stars[i];
                image.sprite = star.Gold ? _art.GoldStar : _art.Star;
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(star.Size, star.Size);
                rect.anchoredPosition = new Vector2(star.X, star.Y);
                rect.localRotation = Quaternion.Euler(0, 0, star.Rotation);
            }

            y += rowHeight;
            Place(_starCount.rectTransform, centerX, y + 2f * unit, width * 0.9f, 4f * unit);
            _starCount.fontSize = 3f * unit;
            y += 4.4f * unit;

            // The crowd: the bottom of the panel, a little taller than what is left so the back rows
            // tuck in behind the star count on a big crowd.
            var crowdHeight = Mathf.Max(26f * unit, height - y - 3f * unit);
            _crowdArea.anchorMin = _crowdArea.anchorMax = Vector2.zero;
            _crowdArea.pivot = Vector2.zero;
            _crowdArea.sizeDelta = new Vector2(width, crowdHeight);
            _crowdArea.anchoredPosition = Vector2.zero;

            var fanMax = 17f * unit;
            _crowdLayout = CareerBandHomeLayout.Crowd(_totalFans, width, crowdHeight, Mathf.Max(1, _art.Fans?.Length ?? 0),
                fanMax, fanMax * 0.5f, FAN_ASPECT);
            Pool(_fans, _crowdLayout.Count, _crowdArea);
            for (var i = 0; i < _crowdLayout.Count; i++)
            {
                var fan = _crowdLayout[i];
                var image = _fans[i];
                image.sprite = _art.Fans is { Length: > 0 } ? _art.Fans[fan.Variant % _art.Fans.Length] : null;
                image.enabled = image.sprite != null;
                image.color = Color.Lerp(Color.white, BackRowTint, fan.Depth);
                var rect = image.rectTransform;
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(fan.Size * FAN_ASPECT, fan.Size);
                rect.localRotation = Quaternion.Euler(0, 0, fan.Rotation);
            }

            // The fan count sits on the crowd, near the bottom, over a dark plate so it stays readable.
            Place(_fanCount.rectTransform, centerX, height - 4.2f * unit, width * 0.9f, 5f * unit);
            _fanCount.fontSize = 3.6f * unit;
            Place(_fanPlate, centerX, height - 4.2f * unit, width * 0.42f, 5.6f * unit);

            // What is left between the stars and the crowd's front rows.
            var extrasHeight = Mathf.Max(0f, height - y - crowdHeight * 0.55f);
            Place(_extras, centerX, y + extrasHeight / 2f, width * 0.9f, extrasHeight);
            Place(_notice.rectTransform, centerX, y + 1.6f * unit, width * 0.9f, 3.2f * unit);
            _notice.fontSize = 2.2f * unit;

            MarkNewIcons();
        }

        /// <summary>
        /// For the gain animation: which medals and which fans of the layout are the new ones, and in
        /// what order they arrive. The row and the crowd are laid out for the new totals, so the new
        /// ones are the last of their kind (a new gold medal replaces the hundred it stands for, so the
        /// medals after it are new as well).
        /// </summary>
        private void MarkNewIcons()
        {
            _starTurn.Clear();
            _fanTurn.Clear();
            _newStars = 0;
            _newFans = 0;

            for (var i = 0; i < _starLayout.Count; i++)
            {
                _starTurn.Add(-1);
            }

            for (var i = 0; i < _crowdLayout.Count; i++)
            {
                _fanTurn.Add(-1);
            }

            if (!_gaining)
            {
                return;
            }

            var (goldBefore, regularBefore) = CareerBandHomeLayout.SplitStars(_starsBefore);
            var golds = new List<int>();
            var regulars = new List<int>();
            for (var i = 0; i < _starLayout.Count; i++)
            {
                (_starLayout[i].Gold ? golds : regulars).Add(i);
            }

            var keptRegulars = golds.Count > goldBefore ? 0 : Mathf.Min(regularBefore, regulars.Count);
            for (var i = Mathf.Min(goldBefore, golds.Count); i < golds.Count; i++)
            {
                _starTurn[golds[i]] = _newStars++;
            }

            for (var i = keptRegulars; i < regulars.Count; i++)
            {
                _starTurn[regulars[i]] = _newStars++;
            }

            var crowdBefore = Mathf.Min(CareerBandHomeLayout.CrowdSize(_fansBefore), _crowdLayout.Count);
            for (var i = crowdBefore; i < _crowdLayout.Count; i++)
            {
                _fanTurn[i] = _newFans++;
            }
        }

        private static void Place(RectTransform rect, float centerX, float centerFromTop, float width, float height)
        {
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(centerX, -centerFromTop);
        }

        private static void Pool(List<Image> pool, int count, RectTransform parent)
        {
            while (pool.Count < count)
            {
                var image = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.rectTransform.SetParent(parent, false);
                image.raycastTarget = false;
                image.preserveAspect = true;
                pool.Add(image);
            }

            for (var i = 0; i < pool.Count; i++)
            {
                pool[i].gameObject.SetActive(i < count);
            }
        }

        /// <summary>
        /// The entrance (numbers count up, medals pop in one after another, the crowd rises) and, after
        /// it, the crowd's idle bounce.
        /// </summary>
        private void Animate()
        {
            if (_gaining)
            {
                AnimateGain();
                return;
            }

            var elapsed = Time.unscaledTime - _shownAt;

            var counted = EaseOut(Mathf.Clamp01(elapsed / COUNT_SECONDS));
            _starCount.text = Localize.KeyFormat("Menu.Career.Home.Stars",
                Mathf.RoundToInt(_totalStars * counted).ToString("N0"));
            _fanCount.text = Localize.KeyFormat("Menu.Career.Home.Fans",
                Mathf.RoundToInt(_totalFans * counted).ToString("N0"));

            _barFill.sizeDelta = new Vector2(_barBack.sizeDelta.x * _rankFraction * counted, 0f);

            // Medals: one after another, all within the entrance however many there are.
            var step = _starLayout.Count <= 1 ? 0f : Mathf.Min(0.05f, (INTRO_SECONDS - STAR_POP_SECONDS) / (_starLayout.Count - 1));
            for (var i = 0; i < _starLayout.Count && i < _stars.Count; i++)
            {
                var t = Mathf.Clamp01((elapsed - i * step) / STAR_POP_SECONDS);
                var scale = t <= 0f ? 0f : EaseOutBack(t);
                _stars[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            // Crowd: rises from below the panel, back rows first, then keeps bouncing a little.
            for (var i = 0; i < _crowdLayout.Count && i < _fans.Count; i++)
            {
                var fan = _crowdLayout[i];
                var delay = (1f - fan.Depth) * 0.25f + (i % 7) * 0.02f;
                var rise = EaseOut(Mathf.Clamp01((elapsed - delay) / 0.5f));
                var phase = i * 1.618f;
                var bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * (2.6f + (i % 5) * 0.35f) + phase)) * fan.Size * 0.045f;
                _fans[i].rectTransform.localScale = Vector3.one;
                _fans[i].rectTransform.anchoredPosition =
                    new Vector2(fan.X, fan.Y - (1f - rise) * (fan.Y + fan.Size * 1.2f) + bounce * rise);
            }
        }

        /// <summary>
        /// After a song: what the band had is in place, then the new medals pop in one at a time, then
        /// the new fans join the crowd one at a time, the numbers and the rank bar climbing with them.
        /// </summary>
        private void AnimateGain()
        {
            var elapsed = Mathf.Max(0f, Time.unscaledTime - _gainAt);

            // Medals
            var starStep = _newStars <= 1 ? 0f : Mathf.Min(GAIN_STAR_STEP, GAIN_RUN_SECONDS / (_newStars - 1));
            var starSeconds = _newStars == 0 ? 0f : (_newStars - 1) * starStep + STAR_POP_SECONDS;
            for (var i = 0; i < _starLayout.Count && i < _stars.Count; i++)
            {
                var scale = 1f;
                if (_starTurn[i] >= 0)
                {
                    var t = Mathf.Clamp01((elapsed - _starTurn[i] * starStep) / STAR_POP_SECONDS);
                    scale = t <= 0f ? 0f : EaseOutBack(t);
                }

                _stars[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
            }

            var starCountSeconds = _totalStars > _starsBefore ? Mathf.Max(starSeconds, GAIN_MIN_COUNT_SECONDS) : 0f;
            var starsCounted = starCountSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / starCountSeconds);
            _starCount.text = Localize.KeyFormat("Menu.Career.Home.Stars",
                Mathf.RoundToInt(Mathf.Lerp(_starsBefore, _totalStars, starsCounted)).ToString("N0"));

            // Fans, once the medals are in
            var fanElapsed = elapsed - starCountSeconds - (starCountSeconds > 0f ? 0.2f : 0f);
            var fanStep = _newFans <= 1 ? 0f : Mathf.Min(GAIN_FAN_STEP, GAIN_RUN_SECONDS / (_newFans - 1));
            var fanSeconds = _newFans == 0 ? 0f : (_newFans - 1) * fanStep + FAN_POP_SECONDS;
            for (var i = 0; i < _crowdLayout.Count && i < _fans.Count; i++)
            {
                var fan = _crowdLayout[i];
                var scale = 1f;
                if (_fanTurn[i] >= 0)
                {
                    var t = Mathf.Clamp01((fanElapsed - _fanTurn[i] * fanStep) / FAN_POP_SECONDS);
                    scale = t <= 0f ? 0f : EaseOutBack(t);
                }

                var phase = i * 1.618f;
                var bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * (2.6f + (i % 5) * 0.35f) + phase)) * fan.Size * 0.045f;
                var rect = _fans[i].rectTransform;
                rect.localScale = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2(fan.X, fan.Y + bounce);
            }

            var fanCountSeconds = _totalFans > _fansBefore ? Mathf.Max(fanSeconds, GAIN_MIN_COUNT_SECONDS) : 0f;
            var fansCounted = fanCountSeconds <= 0f ? 1f : EaseOut(Mathf.Clamp01(fanElapsed / fanCountSeconds));
            _fanCount.text = Localize.KeyFormat("Menu.Career.Home.Fans",
                Mathf.RoundToInt(Mathf.Lerp(_fansBefore, _totalFans, fansCounted)).ToString("N0"));
            _barFill.sizeDelta = new Vector2(
                _barBack.sizeDelta.x * Mathf.Lerp(_rankFractionBefore, _rankFraction, fansCounted), 0f);

            // Done: hand over to the idle state of the entrance animation (everything in, crowd bouncing).
            if (fanElapsed > fanCountSeconds + 0.1f)
            {
                _gaining = false;
                _shownAt = Time.unscaledTime - 60f;
            }
        }

        private static float EaseOut(float t)
        {
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
