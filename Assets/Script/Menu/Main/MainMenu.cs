using TMPro;
using UnityEngine;
using YARG.Core.Input;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.Settings;
using YARG.Settings;

namespace YARG.Menu.Main
{
    public class MainMenu : MonoBehaviour
    {
        private static bool _antiPiracyDialogShown;

        [SerializeField]
        private TextMeshProUGUI _versionText;

        private void Start()
        {
            _versionText.text = GlobalVariables.Instance.CurrentVersion;

            // Show the anti-piracy dialog if it hasn't been shown already
            // Also only show it once per game launch
            if (!_antiPiracyDialogShown && SettingsManager.Settings.ShowAntiPiracyDialog)
            {
                DialogManager.Instance.ShowOneTimeMessage(
                    "Menu.Dialog.AntiPiracy",
                    () =>
                    {
                        SettingsManager.Settings.ShowAntiPiracyDialog = false;
                        SettingsManager.SaveSettings();
                    });

                _antiPiracyDialogShown = true;
            }

            if (SettingsMenu.ConsumeOpenOnNextMenuLoad())
            {
                SettingsMenu.Instance.gameObject.SetActive(true);
            }
        }

        private GameObject _quickPlayButton;

        // The first entry of the menu is drawn larger than the rest. In retail mode that is Game
        // Modes, so it takes on the Quickplay entry's sizes; these are both entries' own, read once.
        private EntryStyle _quickPlayStyle;
        private EntryStyle _gameModesStyle;

        private class EntryStyle
        {
            public RectTransform[] Rects;
            public Vector2[] Sizes;
            public Vector2[] Positions;
            public TextMeshProUGUI[] Texts;
            public float[] FontSizes;
            public UnityEngine.UI.LayoutElement[] Layouts;
            public Vector2[] LayoutHeights;

            public static EntryStyle Read(GameObject entry)
            {
                var style = new EntryStyle
                {
                    Rects = entry.GetComponentsInChildren<RectTransform>(true),
                    Texts = entry.GetComponentsInChildren<TextMeshProUGUI>(true),
                    Layouts = entry.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true),
                };

                style.Sizes = System.Array.ConvertAll(style.Rects, rect => rect.sizeDelta);
                style.Positions = System.Array.ConvertAll(style.Rects, rect => rect.anchoredPosition);
                style.FontSizes = System.Array.ConvertAll(style.Texts, text => text.fontSize);
                style.LayoutHeights = System.Array.ConvertAll(style.Layouts,
                    layout => new Vector2(layout.minHeight, layout.preferredHeight));
                return style;
            }

            /// <summary>
            /// Gives this entry the sizes of <paramref name="from"/> (an entry built the same way).
            /// </summary>
            public void Apply(EntryStyle from)
            {
                if (from.Rects.Length != Rects.Length || from.Texts.Length != Texts.Length ||
                    from.Layouts.Length != Layouts.Length)
                {
                    return;
                }

                for (var i = 0; i < Rects.Length; i++)
                {
                    Rects[i].sizeDelta = from.Sizes[i];

                    // The entry itself is placed by the menu's layout.
                    if (i > 0)
                    {
                        Rects[i].anchoredPosition = from.Positions[i];
                    }
                }

                for (var i = 0; i < Texts.Length; i++)
                {
                    Texts[i].fontSize = from.FontSizes[i];
                }

                for (var i = 0; i < Layouts.Length; i++)
                {
                    Layouts[i].minHeight = from.LayoutHeights[i].x;
                    Layouts[i].preferredHeight = from.LayoutHeights[i].y;
                }
            }
        }

        private void StyleGameModesEntry(bool large)
        {
            if (_gameModesStyle is null)
            {
                foreach (var entry in transform.root.GetComponentsInChildren<NavigatableButton>(true))
                {
                    if (entry.Calls(this, nameof(GameModes)))
                    {
                        _gameModesStyle = EntryStyle.Read(entry.gameObject);
                        break;
                    }
                }

                if (_gameModesStyle is null)
                {
                    return;
                }

                _quickPlayStyle = EntryStyle.Read(_quickPlayButton);
            }

            _gameModesStyle.Apply(large ? _quickPlayStyle : _gameModesStyle);
        }

        /// <summary>
        /// Retail mode: Game Modes is the first entry and Quickplay is found inside it (see
        /// YARG.Career.RetailMode), so the Quickplay entry here is hidden.
        /// </summary>
        private void ApplyRetailMode()
        {
            if (_quickPlayButton == null)
            {
                // The entries are wired to this component but need not be its children.
                foreach (var entry in transform.root.GetComponentsInChildren<NavigatableButton>(true))
                {
                    if (entry.Calls(this, nameof(QuickPlay)))
                    {
                        _quickPlayButton = entry.gameObject;
                        break;
                    }
                }
            }

            if (_quickPlayButton == null)
            {
                return;
            }

            var retail = YARG.Career.RetailMode.Enabled;
            StyleGameModesEntry(retail);
            if (_quickPlayButton.activeSelf == retail)
            {
                _quickPlayButton.SetActive(!retail);

                // The selection starts on the first entry that is there.
                var group = _quickPlayButton.GetComponentInParent<NavigationGroup>(true);
                if (group != null)
                {
                    group.SelectFirst();
                }
            }
        }

        private void OnEnable()
        {
            ApplyRetailMode();
            YARG.Career.RetailMode.Changed += ApplyRetailMode;

            // Set navigation scheme
            _ = Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                new NavigationScheme.Entry(MenuAction.Select, "Menu.Main.GoToCurrentlyPlaying", CurrentlyPlaying)
            }, true));
        }

        private void OnDisable()
        {
            YARG.Career.RetailMode.Changed -= ApplyRetailMode;
            Navigator.Instance?.PopScheme();
        }

        public void CurrentlyPlaying()
        {
            MusicLibraryMenu.RequestGoToCurrentlyPlaying(MusicPlayer.NowPlaying);
            QuickPlay();
        }

        public void QuickPlay()
        {
            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.MusicLibrary, false);

            MusicLibraryMenu.LibraryMode = MusicLibraryMode.QuickPlay;

            menu.gameObject.SetActive(true);
        }

        public void GameModes()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.GameModes);
        }

        public void Profiles()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.ProfileList);
        }

        public void Content()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.Content);
        }

        public void Credits()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.Credits);
        }

        public void Settings()
        {
            SettingsMenu.Instance.gameObject.SetActive(true);
        }

        public void Exit()
        {
#if UNITY_EDITOR

            UnityEditor.EditorApplication.isPlaying = false;

#else
			Application.Quit();

#endif
        }

        public void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/sqpu4R552r");
        }

        public void OpenTwitter()
        {
            Application.OpenURL("https://twitter.com/YARGGame");
        }

        public void OpenGithub()
        {
            Application.OpenURL("https://github.com/YARC-Official/YARG");
        }
    }
}
