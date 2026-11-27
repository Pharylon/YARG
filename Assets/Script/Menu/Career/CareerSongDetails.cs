using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;
using YARG.Localization;
using YARG.Menu.MusicLibrary;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The left panel of the gig screen while Blue (Song Details) is on: the highlighted song's
    /// parts and difficulty, as the music library shows them, and the band's best result of it in this
    /// setlist (stars, points, fans, who played what, and whether it was played on another song).
    /// Built in code, sized from its own rect.
    /// </summary>
    public class CareerSongDetails : MonoBehaviour
    {
        public class Info
        {
            public SongEntry Song;
            // The song was played on Song in place of this gig song (a swap).
            public string ReplacedName;
            public bool IsUserPick;

            public bool Played;
            public int Stars;
            public bool Gold;
            public int Points;
            public int Fans;
            public bool ShowFans;
            public List<string> Players = new();
        }

        private static readonly Color DimText = new(0.62f, 0.78f, 0.9f);
        private static readonly Color WarningText = new(1f, 0.75f, 0.3f);
        private static readonly Color EmptyStar = new(1f, 1f, 1f, 0.18f);

        private const int RING_COUNT = 10;

        private RectTransform _rect;
        private TextMeshProUGUI _template;
        private Sprite _star;
        private Sprite _goldStar;

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _subtitle;
        private TextMeshProUGUI _swap;
        private TextMeshProUGUI _difficultyHeader;
        private RectTransform _rings;
        private readonly List<DifficultyRing> _ringViews = new();
        private TextMeshProUGUI _bestHeader;
        private RectTransform _starRow;
        private readonly List<Image> _stars = new();
        private TextMeshProUGUI _results;

        private Info _info;
        private Vector2 _laidOutSize;

        public static CareerSongDetails Create(RectTransform parent, TextMeshProUGUI textTemplate,
            GameObject ringPrefab, Sprite star, Sprite goldStar, float bottomInset)
        {
            var root = new GameObject("Song Details", typeof(RectTransform), typeof(LayoutElement));
            root.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(0f, bottomInset);
            rect.offsetMax = Vector2.zero;

            var details = root.AddComponent<CareerSongDetails>();
            details._rect = rect;
            details._template = textTemplate;
            details._star = star;
            details._goldStar = goldStar;
            details.Build(ringPrefab);
            return details;
        }

        private void Build(GameObject ringPrefab)
        {
            _title = Text("Title", Color.white);
            _subtitle = Text("Subtitle", DimText);
            _swap = Text("Swap", WarningText);
            _difficultyHeader = Text("Difficulty Header", Color.white);
            _difficultyHeader.text = Localize.Key("Menu.Career.Details.Difficulty");

            _rings = Child("Rings");
            var grid = _rings.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = RING_COUNT / 2;
            grid.childAlignment = TextAnchor.MiddleCenter;
            if (ringPrefab != null)
            {
                for (var i = 0; i < RING_COUNT; i++)
                {
                    var ring = Instantiate(ringPrefab, _rings);
                    // Only a display here: the music library's rings search when clicked.
                    foreach (var graphic in ring.GetComponentsInChildren<Graphic>(true))
                    {
                        graphic.raycastTarget = false;
                    }

                    _ringViews.Add(ring.GetComponent<DifficultyRing>());
                }
            }

            _bestHeader = Text("Best Header", Color.white);
            _bestHeader.text = Localize.Key("Menu.Career.Details.Best");
            _starRow = Child("Stars");
            for (var i = 0; i < 5; i++)
            {
                var image = Child("Star", _starRow).gameObject.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                _stars.Add(image);
            }

            _results = Text("Results", Color.white);
            _results.alignment = TextAlignmentOptions.Top;
            _results.textWrappingMode = TextWrappingModes.Normal;
        }

        public void Show(Info info)
        {
            gameObject.SetActive(true);
            _info = info;
            _laidOutSize = Vector2.zero;
            Fill();
            LayoutIfNeeded();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Update()
        {
            LayoutIfNeeded();
        }

        private void Fill()
        {
            var info = _info;
            var song = info?.Song;

            _title.text = song is null
                ? Localize.Key(info is { IsUserPick: true } ? "Menu.Career.Theme.Pick" : "Menu.Career.Details.NoSong")
                : info.ReplacedName is not null ? $"<i>{song.Name}</i>" : song.Name.ToString();

            var parts = new List<string>();
            if (song is not null)
            {
                parts.Add(song.Artist.ToString());
                if (!string.IsNullOrWhiteSpace(song.Album.ToString()))
                {
                    parts.Add(song.Album.ToString());
                }

                if (!string.IsNullOrWhiteSpace(song.ParsedYear))
                {
                    parts.Add(song.ParsedYear);
                }

                parts.Add(YARG.Song.SongSources.SourceToGameName(song.Source.Original));
            }

            _subtitle.text = string.Join("  ·  ", parts);
            _swap.text = info?.ReplacedName is { } replaced
                ? Localize.KeyFormat("Menu.Career.Details.Swapped", replaced)
                : string.Empty;

            _difficultyHeader.gameObject.SetActive(song is not null);
            _rings.gameObject.SetActive(song is not null && _ringViews.Count == RING_COUNT);
            if (song is not null && _ringViews.Count == RING_COUNT)
            {
                Sidebar.FillDifficultyRings(_ringViews, song);
            }

            var played = info is { Played: true };
            _starRow.gameObject.SetActive(played);
            for (var i = 0; i < _stars.Count; i++)
            {
                var earned = played && (info.Gold || i < info.Stars);
                _stars[i].sprite = played && info.Gold ? _goldStar : _star;
                _stars[i].color = earned ? Color.white : EmptyStar;
            }

            var lines = new List<string>();
            if (!played)
            {
                lines.Add(Localize.Key("Menu.Career.Details.NotPlayed"));
            }
            else
            {
                lines.Add(Localize.KeyFormat("Menu.Career.Details.Points", info.Points.ToString("N0")));
                if (info.ShowFans)
                {
                    lines.Add(Localize.KeyFormat("Menu.Career.Details.Fans", info.Fans.ToString("N0")));
                }

                lines.AddRange(info.Players);
            }

            _results.text = string.Join("\n", lines);
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

        private void Layout(float width, float height)
        {
            var unit = height / 100f;
            var centerX = width / 2f;
            var lineWidth = width * 0.9f;
            var y = 5f * unit;

            Place(_title.rectTransform, centerX, y + 3.5f * unit, lineWidth, 7f * unit);
            _title.enableAutoSizing = true;
            _title.fontSizeMax = 5.6f * unit;
            _title.fontSizeMin = 2.6f * unit;
            y += 7.5f * unit;

            Place(_subtitle.rectTransform, centerX, y + 1.6f * unit, lineWidth, 3.2f * unit);
            _subtitle.fontSize = 2.3f * unit;
            y += 3.6f * unit;

            Place(_swap.rectTransform, centerX, y + 1.6f * unit, lineWidth, 3.2f * unit);
            _swap.fontSize = 2.3f * unit;
            y += 5f * unit;

            Place(_difficultyHeader.rectTransform, centerX, y + 1.8f * unit, lineWidth, 3.6f * unit);
            _difficultyHeader.fontSize = 2.8f * unit;
            y += 4.5f * unit;

            var cell = Mathf.Min(13f * unit, width * 0.8f / 5f);
            var grid = _rings.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cell, cell);
            grid.spacing = new Vector2(cell * 0.12f, cell * 0.12f);
            var gridHeight = cell * 2.12f;
            Place(_rings, centerX, y + gridHeight / 2f, cell * 5.5f, gridHeight);
            y += gridHeight + 4f * unit;

            Place(_bestHeader.rectTransform, centerX, y + 1.8f * unit, lineWidth, 3.6f * unit);
            _bestHeader.fontSize = 2.8f * unit;
            y += 4.5f * unit;

            var starSize = 6f * unit;
            Place(_starRow, centerX, y + starSize / 2f, starSize * 5.6f, starSize);
            for (var i = 0; i < _stars.Count; i++)
            {
                var rect = _stars[i].rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(starSize, starSize);
                rect.anchoredPosition = new Vector2(starSize * (0.5f + i * 1.15f), 0f);
            }

            y += starSize + 2f * unit;
            Place(_results.rectTransform, centerX, y + (height - y) / 2f, lineWidth, Mathf.Max(0f, height - y));
            _results.fontSize = 2.6f * unit;
        }

        private RectTransform Child(string childName, RectTransform parent = null)
        {
            var child = new GameObject(childName, typeof(RectTransform)).GetComponent<RectTransform>();
            child.SetParent(parent != null ? parent : _rect, false);
            child.anchorMin = child.anchorMax = new Vector2(0, 1);
            child.pivot = new Vector2(0.5f, 0.5f);
            return child;
        }

        private TextMeshProUGUI Text(string childName, Color color)
        {
            var text = Instantiate(_template, _rect);
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
            text.enableAutoSizing = false;
            text.color = color;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static void Place(RectTransform rect, float centerX, float centerFromTop, float width, float height)
        {
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(centerX, -centerFromTop);
        }
    }
}
