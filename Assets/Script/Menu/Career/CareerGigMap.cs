using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The gigs of a setlist as a map: one stop per gig along a winding route, in the setlist's
    /// order. Bonus gigs are smaller stops. Stops the band has not unlocked are dimmed; the selected
    /// stop is enlarged and named. Built in code, sized from its
    /// own rect; the stops are drawn with shapes made here, so the map needs no art.
    /// </summary>
    public class CareerGigMap : MonoBehaviour
    {
        public struct Stop
        {
            public Guid Id;
            // The venue (or, without one, the gig's name).
            public string Label;
            public bool Bonus;
            public bool Unlocked;
            public bool Completed;
            // Where along the route the stop is. Stops sharing a column are a fork: gigs that unlock
            // together, drawn one above the other with the route splitting to them and joining after.
            public int Column;
            // The gig's own picture, shown in the stop instead of its number. Null: none.
            public Texture Picture;
        }

        private static readonly Color Route = new(0.18f, 0.85f, 1f, 0.55f);
        private static readonly Color RouteLocked = new(1f, 1f, 1f, 0.12f);
        private static readonly Color Ring = new(0.18f, 0.85f, 1f);
        private static readonly Color RingLocked = new(1f, 1f, 1f, 0.22f);
        private static readonly Color Fill = new(0.02f, 0.09f, 0.18f);
        private static readonly Color FillDone = new(0.08f, 0.46f, 0.72f);
        private static readonly Color FillLocked = new(0.05f, 0.07f, 0.1f);
        private static readonly Color Glow = new(1f, 1f, 1f, 0.9f);
        private static readonly Color DimText = new(0.62f, 0.78f, 0.9f);
        private static readonly Color StoryText = new(0.86f, 0.9f, 0.95f);
        private static readonly Color PictureLocked = new(0.3f, 0.33f, 0.38f);

        private const float BONUS_SCALE = 0.62f;
        private const float SELECTED_SCALE = 1.28f;

        private class Dot
        {
            public RectTransform Root;
            public Image Glow;
            public Image Ring;
            public Image Fill;
            public TextMeshProUGUI Number;
            public GameObject PictureMask;
            public RawImage Picture;
            public Guid Id;
        }

        private RectTransform _rect;
        private TextMeshProUGUI _template;
        private Action<Guid> _onPick;

        private RectTransform _routeRoot;
        private readonly List<Image> _segments = new();
        private readonly List<Dot> _dots = new();
        private TextMeshProUGUI _selectedLabel;
        private TextMeshProUGUI _infoLabel;
        private TextMeshProUGUI _storyLabel;

        private List<Stop> _stops = new();
        private Guid? _selected;
        private string _info;
        private string _story;

        // Stops that have just unlocked: drawn locked until their moment comes, then they light up
        // with a burst (see Celebrate).
        private readonly HashSet<Guid> _fresh = new();
        private Func<bool> _holdCelebration;
        private float _celebrationAsked;
        private float _celebrationStart = -1f;

        private const float CELEBRATION_MIN_WAIT = 0.9f;
        private const float CELEBRATION_SECONDS = 2.4f;
        private const float BURST_SECONDS = 0.8f;

        private Vector2 _laidOutSize;
        private bool _dirty;

        private static Sprite _disc;

        /// <summary>
        /// A map filling <paramref name="parent"/>. <paramref name="onPick"/> is called with a stop's id
        /// when it is clicked.
        /// </summary>
        public static CareerGigMap Create(RectTransform parent, TextMeshProUGUI textTemplate, Action<Guid> onPick)
        {
            var root = new GameObject("Gig Map", typeof(RectTransform));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var map = root.AddComponent<CareerGigMap>();
            map._rect = rect;
            map._template = textTemplate;
            map._onPick = onPick;

            map._routeRoot = map.Child("Route", rect);
            map._selectedLabel = map.Label("Selected Stop", Color.white);
            map._selectedLabel.alignment = TextAlignmentOptions.Left;
            map._infoLabel = map.Label("Selected Stop Info", DimText);
            map._infoLabel.alignment = TextAlignmentOptions.TopLeft;
            map._infoLabel.textWrappingMode = TextWrappingModes.Normal;
            map._infoLabel.maxVisibleLines = 2;
            map._storyLabel = map.Label("Selected Stop Story", StoryText);
            map._storyLabel.alignment = TextAlignmentOptions.TopLeft;
            map._storyLabel.textWrappingMode = TextWrappingModes.Normal;
            map._storyLabel.maxVisibleLines = 3;
            map._storyLabel.fontStyle = FontStyles.Normal;
            return map;
        }

        /// <summary>
        /// <paramref name="info"/>: what goes under the selected stop's name (its progress, fans and
        /// rules), up to two lines. <paramref name="story"/>: the gig's own text under that, up to
        /// three lines.
        /// </summary>
        public void Show(List<Stop> stops, Guid? selected, string info, string story)
        {
            _story = story;
            _stops = stops ?? new List<Stop>();
            _selected = selected;
            _info = info;
            _dirty = true;
            LayoutIfNeeded();
        }

        /// <summary>
        /// These stops have just unlocked. They stay dimmed until <paramref name="hold"/> (if any)
        /// stops returning true and a moment has passed, then light up one burst after another.
        /// </summary>
        public void Celebrate(IEnumerable<Guid> stops, Func<bool> hold)
        {
            foreach (var id in stops)
            {
                _fresh.Add(id);
            }

            _holdCelebration = hold;
            _celebrationAsked = Time.unscaledTime;
            _celebrationStart = -1f;
            _dirty = true;
        }

        private bool Celebrating => _fresh.Count > 0 && _celebrationStart >= 0f;

        // A fresh stop is still shown locked before its celebration starts.
        private bool ShownUnlocked(Stop stop) => stop.Unlocked && (!_fresh.Contains(stop.Id) || Celebrating);

        private void UpdateCelebration()
        {
            if (_fresh.Count == 0)
            {
                return;
            }

            if (_celebrationStart < 0f)
            {
                if (Time.unscaledTime - _celebrationAsked < CELEBRATION_MIN_WAIT || _holdCelebration?.Invoke() == true)
                {
                    return;
                }

                _celebrationStart = Time.unscaledTime;
                _dirty = true;
                LayoutIfNeeded();
            }

            var elapsed = Time.unscaledTime - _celebrationStart;
            if (elapsed >= CELEBRATION_SECONDS)
            {
                _fresh.Clear();
                _celebrationStart = -1f;
                _dirty = true;
                LayoutIfNeeded();
                return;
            }

            // The stop pops, and rings spread out from it and fade, a few times over.
            var burst = elapsed % BURST_SECONDS / BURST_SECONDS;
            var pop = elapsed < 0.35f ? 0.6f + 0.4f * EaseOutBack(elapsed / 0.35f) : 1f;
            foreach (var dot in _dots)
            {
                if (!dot.Root.gameObject.activeSelf || !_fresh.Contains(dot.Id))
                {
                    continue;
                }

                dot.Root.localScale = new Vector3(pop, pop, 1f);
                dot.Glow.enabled = true;
                var scale = 1f + 1.6f * burst;
                dot.Glow.rectTransform.localScale = new Vector3(scale, scale, 1f);
                var color = Ring;
                color.a = 0.85f * (1f - burst);
                dot.Glow.color = color;
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private void Update()
        {
            LayoutIfNeeded();

            // The selected stop's halo breathes.
            var pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
            foreach (var dot in _dots)
            {
                if (dot.Glow.enabled)
                {
                    var color = Glow;
                    color.a *= pulse;
                    dot.Glow.color = color;
                }
            }

            UpdateCelebration();
        }

        private void LayoutIfNeeded()
        {
            var size = _rect.rect.size;
            if (size.x < 1f || size.y < 1f)
            {
                return;
            }

            if (!_dirty && (size - _laidOutSize).sqrMagnitude < 0.25f)
            {
                return;
            }

            _dirty = false;
            _laidOutSize = size;
            Layout(size.x, size.y);
        }

        /// <summary>
        /// Columns left to right in order. A column with one stop alternates between an upper and a
        /// lower row so the route winds; a fork's stops are spread from the upper row to the lower; the selected stop's name and details (for a locked stop, what unlocks it) sit under
        /// the route, above the list of its songs.
        /// </summary>
        private void Layout(float width, float height)
        {
            var count = _stops.Count;

            // The stops of each column, in order.
            var columns = new List<List<int>>();
            for (var i = 0; i < count; i++)
            {
                if (i == 0 || _stops[i].Column != _stops[i - 1].Column)
                {
                    columns.Add(new List<int>());
                }

                columns[^1].Add(i);
            }

            var margin = width * 0.075f;
            var step = columns.Count > 1 ? (width - 2f * margin) / (columns.Count - 1) : 0f;
            var size = Mathf.Min(height * 0.165f, columns.Count > 1 ? step * 1.5f : height * 0.165f);
            var upper = height * 0.21f;
            var lower = height * 0.4f;

            var centers = new Vector2[count];
            for (var column = 0; column < columns.Count; column++)
            {
                var x = columns.Count > 1 ? margin + column * step : width / 2f;
                var stops = columns[column];
                for (var k = 0; k < stops.Count; k++)
                {
                    float y;
                    if (stops.Count > 1)
                    {
                        // A fork's stops stand further apart than the rows, so they clear each other
                        // even with one of them selected.
                        y = Mathf.Lerp(upper - height * 0.045f, lower + height * 0.045f, k / (float) (stops.Count - 1));
                    }
                    else
                    {
                        y = columns.Count > 1 ? (column % 2 == 0 ? lower : upper) : height / 2f;
                    }

                    centers[stops[k]] = new Vector2(x, y);
                }
            }

            // The route: every stop of a column to every stop of the next.
            var segment = 0;
            for (var column = 0; column + 1 < columns.Count; column++)
            {
                foreach (var fromStop in columns[column])
                {
                    foreach (var toStop in columns[column + 1])
                    {
                        if (segment == _segments.Count)
                        {
                            var image = Child("Segment", _routeRoot).gameObject.AddComponent<Image>();
                            image.raycastTarget = false;
                            _segments.Add(image);
                        }

                        var from = centers[fromStop];
                        var to = centers[toStop];
                        var delta = to - from;
                        var line = _segments[segment++];
                        line.gameObject.SetActive(true);
                        var rect = line.rectTransform;
                        rect.sizeDelta = new Vector2(delta.magnitude, Mathf.Max(2f, size * 0.07f));
                        rect.anchoredPosition = new Vector2((from.x + to.x) / 2f, -(from.y + to.y) / 2f);
                        rect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                        line.color = ShownUnlocked(_stops[toStop]) ? Route : RouteLocked;
                    }
                }
            }

            for (var i = segment; i < _segments.Count; i++)
            {
                _segments[i].gameObject.SetActive(false);
            }

            // The stops
            while (_dots.Count < count)
            {
                _dots.Add(CreateDot());
            }

            var number = 0;
            for (var i = 0; i < _dots.Count; i++)
            {
                var dot = _dots[i];
                var active = i < count;
                dot.Root.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }

                var stop = _stops[i];
                var selected = stop.Id == _selected;
                var diameter = size * (stop.Bonus ? BONUS_SCALE : 1f) * (selected ? SELECTED_SCALE : 1f);

                dot.Id = stop.Id;
                dot.Root.sizeDelta = new Vector2(diameter, diameter);
                dot.Root.anchoredPosition = new Vector2(centers[i].x, -centers[i].y);
                var unlocked = ShownUnlocked(stop);
                dot.Root.localScale = Vector3.one;
                dot.Glow.enabled = selected;
                dot.Glow.rectTransform.localScale = Vector3.one;
                dot.Ring.color = unlocked ? Ring : RingLocked;
                dot.Fill.color = !unlocked ? FillLocked : stop.Completed ? FillDone : Fill;

                if (!stop.Bonus)
                {
                    number++;
                }

                // The gig's picture takes the number's place.
                var pictured = stop.Picture != null;
                dot.PictureMask.SetActive(pictured);
                dot.Number.gameObject.SetActive(!pictured);
                if (pictured)
                {
                    dot.Picture.texture = stop.Picture;
                    dot.Picture.color = unlocked ? Color.white : PictureLocked;
                }

                dot.Number.text = stop.Bonus ? "+" : number.ToString();
                dot.Number.fontSize = diameter * 0.42f;
                dot.Number.color = unlocked ? Color.white : RingLocked;

            }

            // The selected stop: its name, then its details, as a block along the left edge.
            var left = width * 0.025f;
            var blockWidth = width - 2f * left;
            var name = SelectedText();
            _selectedLabel.gameObject.SetActive(!string.IsNullOrEmpty(name));
            _selectedLabel.text = name ?? string.Empty;
            _selectedLabel.fontSize = height * 0.064f;
            _selectedLabel.rectTransform.sizeDelta = new Vector2(blockWidth, height * 0.09f);
            _selectedLabel.rectTransform.anchoredPosition = new Vector2(width / 2f, -height * 0.59f);

            _infoLabel.gameObject.SetActive(!string.IsNullOrEmpty(_info));
            _infoLabel.text = _info ?? string.Empty;
            _infoLabel.fontSize = height * 0.042f;
            var infoLines = string.IsNullOrEmpty(_info) ? 0 : Mathf.Clamp(
                Mathf.CeilToInt(_infoLabel.GetPreferredValues(_info, blockWidth, 0f).y / (height * 0.05f) - 0.2f), 1, 2);
            var infoHeight = infoLines * height * 0.056f;
            var top = height * 0.645f;
            _infoLabel.rectTransform.sizeDelta = new Vector2(blockWidth, infoHeight + 2f);
            _infoLabel.rectTransform.anchoredPosition = new Vector2(width / 2f, -(top + infoHeight / 2f));
            top += infoHeight + height * 0.02f;

            // The gig's own text takes what is left above the songs.
            var storyHeight = Mathf.Max(0f, height * 0.985f - top);
            _storyLabel.gameObject.SetActive(!string.IsNullOrEmpty(_story) && storyHeight > height * 0.05f);
            _storyLabel.text = _story ?? string.Empty;
            _storyLabel.fontSize = height * 0.04f;
            _storyLabel.maxVisibleLines = Mathf.Max(1, Mathf.FloorToInt(storyHeight / (height * 0.052f)));
            _storyLabel.rectTransform.sizeDelta = new Vector2(blockWidth, storyHeight);
            _storyLabel.rectTransform.anchoredPosition = new Vector2(width / 2f, -(top + storyHeight / 2f));
        }

        private string SelectedText()
        {
            foreach (var stop in _stops)
            {
                if (stop.Id == _selected)
                {
                    return stop.Label;
                }
            }

            return null;
        }

        private Dot CreateDot()
        {
            var dot = new Dot { Root = Child("Stop", _rect) };

            // Behind the stop, a little larger.
            dot.Glow = Disc("Halo", dot.Root, -0.16f);
            dot.Ring = Disc("Ring", dot.Root, 0f);
            dot.Fill = Disc("Fill", dot.Root, 0.1f);

            // The gig's picture, cut to the stop's shape.
            var mask = Disc("Picture", dot.Root, 0.1f);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            dot.PictureMask = mask.gameObject;
            var picture = Child("Image", mask.rectTransform);
            picture.anchorMin = Vector2.zero;
            picture.anchorMax = Vector2.one;
            picture.offsetMin = Vector2.zero;
            picture.offsetMax = Vector2.zero;
            dot.Picture = picture.gameObject.AddComponent<RawImage>();
            dot.Picture.raycastTarget = false;
            // Decoded textures come out flipped.
            dot.Picture.uvRect = new Rect(0, 0, 1, -1);
            dot.PictureMask.SetActive(false);

            dot.Number = Instantiate(_template, dot.Root);
            dot.Number.name = "Number";
            Prepare(dot.Number);
            var rect = dot.Number.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            dot.Ring.raycastTarget = true;
            var button = dot.Root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = dot.Ring;
            button.onClick.AddListener(() => _onPick?.Invoke(dot.Id));
            return dot;
        }

        /// <summary>
        /// A disc filling its parent, inset by a share of the parent's size (negative: larger).
        /// </summary>
        private Image Disc(string childName, RectTransform parent, float inset)
        {
            var rect = Child(childName, parent);
            rect.anchorMin = new Vector2(inset, inset);
            rect.anchorMax = new Vector2(1f - inset, 1f - inset);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = DiscSprite();
            image.raycastTarget = false;
            return image;
        }

        private RectTransform Child(string childName, RectTransform parent)
        {
            var child = new GameObject(childName, typeof(RectTransform)).GetComponent<RectTransform>();
            child.SetParent(parent, false);
            // Everything is placed from the map's top-left corner.
            child.anchorMin = child.anchorMax = new Vector2(0, 1);
            child.pivot = new Vector2(0.5f, 0.5f);
            if (parent == _rect && childName == "Route")
            {
                child.anchorMin = Vector2.zero;
                child.anchorMax = Vector2.one;
                child.pivot = new Vector2(0, 1);
                child.offsetMin = Vector2.zero;
                child.offsetMax = Vector2.zero;
            }

            return child;
        }

        private TextMeshProUGUI Label(string childName, Color color)
        {
            var text = Instantiate(_template, _rect);
            text.name = childName;
            Prepare(text);
            text.color = color;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return text;
        }

        private static void Prepare(TextMeshProUGUI text)
        {
            if (text.TryGetComponent<LayoutElement>(out var layout))
            {
                layout.ignoreLayout = true;
            }

            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableAutoSizing = false;
            text.raycastTarget = false;
            text.text = string.Empty;
        }

        /// <summary>
        /// A white disc with a soft edge, drawn once.
        /// </summary>
        private static Sprite DiscSprite()
        {
            if (_disc != null)
            {
                return _disc;
            }

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            var radius = size / 2f - 1f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _disc = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _disc;
        }
    }
}
