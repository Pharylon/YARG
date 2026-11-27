using TMPro;
using UnityEngine;
using YARG.Menu.ListMenu;

namespace YARG.Menu.Career
{
    /// <summary>
    /// A row of the career list (bands, the lineup, careers). Besides the usual texts it can be drawn
    /// as a button row like the music library's "Playlist Manager", show a person/bot/band icon, and
    /// carry a connect button like the profiles page's.
    /// </summary>
    public class CareerView : ViewObject<ViewType>
    {
        [SerializeField]
        private GameObject _buttonHeaderBackground;
        // Shown on the selected row only, as in the music library (it darkens what is under it, so
        // headers and dividers kept it would look darker than the library's).
        [SerializeField]
        private GameObject _trackGradient;
        [SerializeField]
        private TextMeshProUGUI _buttonHelpText;
        [SerializeField]
        private GameObject _iconBackground;
        // The same image as the base class's icon, to flip custom pictures upright.
        [SerializeField]
        private UnityEngine.UI.Image _iconImage;

        [Space]
        [SerializeField]
        private Sprite _personSprite;
        [SerializeField]
        private Sprite _botSprite;
        [SerializeField]
        private Sprite _bandSprite;

        [Space]
        // A prefab asset; one instance is made per row the first time a row needs it.
        [SerializeField]
        private Menu.ColoredButton _connectButtonPrefab;
        [SerializeField]
        private Color _connectedColor = new(0.18039216f, 0.8509804f, 1f, 1f);
        [SerializeField]
        private Color _disconnectedColor = new(0.22f, 0.26f, 0.32f, 1f);
        [SerializeField]
        private Vector2 _connectButtonSize = new(170f, 0f);
        [SerializeField]
        private float _connectButtonRightMargin = 25f;

        private Menu.ColoredButton _connectButton;
        private bool _helpTextPlaced;

        // The band's initials over the band logo, made the first time a row needs it.
        private TextMeshProUGUI _iconText;

        /// <summary>
        /// The pill the lineup's Connected/Connect buttons are made from, with its two colours, for
        /// other career screens that show an on/off state the same way (the band's member picker).
        /// Set once a row exists.
        /// </summary>
        public static Menu.ColoredButton PillPrefab { get; private set; }
        public static Color PillOnColor { get; private set; }
        public static Color PillOffColor { get; private set; }

        private void Awake()
        {
            if (_connectButtonPrefab != null)
            {
                PillPrefab = _connectButtonPrefab;
                PillOnColor = _connectedColor;
                PillOffColor = _disconnectedColor;
            }
        }

        private void ShowIconText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_iconText != null)
                {
                    _iconText.gameObject.SetActive(false);
                }

                return;
            }

            if (_iconText == null)
            {
                if (_iconImage == null || _buttonHelpText == null)
                {
                    return;
                }

                var child = new GameObject("Icon Text", typeof(RectTransform));
                var rect = (RectTransform) child.transform;
                rect.SetParent(_iconImage.rectTransform, false);
                rect.anchorMin = new Vector2(0.06f, 0.1f);
                rect.anchorMax = new Vector2(0.94f, 0.9f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                _iconText = child.AddComponent<TextMeshProUGUI>();
                _iconText.font = _buttonHelpText.font;
                _iconText.fontStyle = FontStyles.Bold;
                _iconText.color = Color.white;
                _iconText.alignment = TextAlignmentOptions.Center;
                _iconText.textWrappingMode = TextWrappingModes.NoWrap;
                _iconText.enableAutoSizing = true;
                _iconText.fontSizeMin = 4f;
                _iconText.fontSizeMax = 200f;
                _iconText.outlineColor = new Color32(8, 10, 18, 255);
                _iconText.outlineWidth = 0.3f;
                _iconText.raycastTarget = false;
            }

            _iconText.gameObject.SetActive(true);
            _iconText.text = text;
        }

        /// <summary>
        /// The help text sits in the row's layout sized for the music library's short labels; it is
        /// moved onto the row itself, right-aligned with room for longer text.
        /// </summary>
        private void PlaceHelpText()
        {
            if (_helpTextPlaced || _buttonHelpText == null)
            {
                return;
            }

            _helpTextPlaced = true;
            var rect = _buttonHelpText.rectTransform;
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(500f, 0f);
            rect.anchoredPosition = new Vector2(-25f, 0f);
            _buttonHelpText.alignment = TextAlignmentOptions.MidlineRight;
            _buttonHelpText.textWrappingMode = TextWrappingModes.NoWrap;
            _buttonHelpText.overflowMode = TextOverflowModes.Overflow;
        }

        public void PrimaryButtonClick() => ViewType?.ViewClick();

        public override void Show(bool selected, ViewType viewType)
        {
            base.Show(selected, viewType);

            var custom = viewType.CustomIcon;
            var sprite = custom != null ? custom : viewType.CareerIcon switch
            {
                ViewType.RowIcon.Person => _personSprite,
                ViewType.RowIcon.Bot => _botSprite,
                ViewType.RowIcon.Band => _bandSprite,
                _ => null,
            };

            if (sprite != null)
            {
                SetIcon(sprite);
            }

            if (_iconImage != null)
            {
                _iconImage.rectTransform.localScale = new Vector3(1f, custom != null ? -1f : 1f, 1f);
            }

            ShowIconText(custom == null && sprite != null ? viewType.CareerIconText : null);

            if (_iconBackground != null)
            {
                // No empty icon box on rows without an icon.
                _iconBackground.SetActive(viewType.GetIcon() != null || sprite != null);
            }

            if (_buttonHelpText != null)
            {
                PlaceHelpText();
                var help = viewType.GetSideText(selected);
                _buttonHelpText.gameObject.SetActive(!string.IsNullOrEmpty(help));
                _buttonHelpText.text = help ?? string.Empty;
            }

            ShowConnectButton(viewType.ConnectedState);
        }

        public override void Hide()
        {
            base.Hide();

            if (_connectButton != null)
            {
                _connectButton.gameObject.SetActive(false);
            }
        }

        private void ShowConnectButton(bool? connected)
        {
            if (connected is null || _connectButtonPrefab == null)
            {
                if (_connectButton != null)
                {
                    _connectButton.gameObject.SetActive(false);
                }

                return;
            }

            if (_connectButton == null)
            {
                _connectButton = Instantiate(_connectButtonPrefab, transform);
                var rect = (RectTransform) _connectButton.transform;
                rect.anchorMin = new Vector2(1, 0);
                rect.anchorMax = new Vector2(1, 1);
                rect.pivot = new Vector2(1, 0.5f);
                rect.sizeDelta = _connectButtonSize;
                rect.anchoredPosition = new Vector2(-_connectButtonRightMargin, 0);

                // The button toggles whatever row this pooled object shows at the time.
                _connectButton.OnClick.AddListener(() =>
                {
                    if (Showing)
                    {
                        ViewType?.ViewClick();
                    }
                });
            }

            _connectButton.gameObject.SetActive(true);
            _connectButton.SetBackgroundAndTextColor(connected.Value ? _connectedColor : _disconnectedColor);
            _connectButton.Text.text = YARG.Localization.Localize.Key(connected.Value
                ? "Menu.Career.Lineup.Connected"
                : "Menu.Career.Lineup.Connect");
        }

        protected override void SetBackground(bool selected, BaseViewType.BackgroundType type)
        {
            base.SetBackground(selected, type);

            if (_trackGradient != null)
            {
                _trackGradient.SetActive(selected);
            }

            if (_buttonHeaderBackground == null)
            {
                return;
            }

            // Drawn like the music library's button rows ("Playlist Manager").
            var isButton = type == BaseViewType.BackgroundType.Button;
            _buttonHeaderBackground.SetActive(isButton && !selected);
            if (isButton && selected)
            {
                SelectedBackground.SetActive(true);
            }
        }
    }
}
