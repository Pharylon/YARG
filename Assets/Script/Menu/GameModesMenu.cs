using TMPro;
using UnityEngine;
using YARG.Core.Input;
using YARG.Menu.Career;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Menu
{
    public class GameModesMenu : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _versionText;

        private void Start()
        {
            _versionText.text = GlobalVariables.Instance.CurrentVersion;
        }

        private GameObject _quickPlayButton;

        /// <summary>
        /// Retail mode moves Quickplay here, under Careers (see YARG.Career.RetailMode). The entry is
        /// a copy of the Practice one, made the first time it is needed.
        /// </summary>
        private void ApplyRetailMode()
        {
            var retail = YARG.Career.RetailMode.Enabled;
            if (_quickPlayButton == null)
            {
                if (!retail)
                {
                    return;
                }

                var practice = FindButton(nameof(Practice));
                var careers = FindButton(nameof(CareerList));
                if (practice == null || careers == null)
                {
                    return;
                }

                _quickPlayButton = Instantiate(practice.gameObject, practice.transform.parent);
                _quickPlayButton.name = "Quickplay";
                // Made from Practice: sized again, as large as Careers.
                _sized = false;
                _quickPlayButton.transform.SetSiblingIndex(careers.transform.GetSiblingIndex() + 1);

                foreach (var label in _quickPlayButton.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    label.text = YARG.Localization.Localize.Key("Menu.Main.Options.Quickplay");
                }

                if (_quickPlayButton.TryGetComponent<NavigatableButton>(out var navigatable))
                {
                    navigatable.ReplaceOnClickEvent(QuickPlay);
                }

                // Up and Down follow the group's own order: rebuild it in the order shown.
                var group = _quickPlayButton.GetComponentInParent<NavigationGroup>(true);
                if (group != null)
                {
                    group.ClearNavigatables();
                    foreach (var entry in group.GetComponentsInChildren<NavigatableBehaviour>(true))
                    {
                        group.AddNavigatable(entry);
                    }

                    group.SelectFirst();
                }
            }

            _quickPlayButton.SetActive(retail);
        }

        private NavigatableButton FindButton(string method)
        {
            // The entries are wired to this component but need not be its children.
            foreach (var entry in transform.root.GetComponentsInChildren<NavigatableButton>(true))
            {
                if (entry.Calls(this, method))
                {
                    return entry;
                }
            }

            return null;
        }

        public void QuickPlay()
        {
            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.MusicLibrary, false);

            MusicLibraryMenu.LibraryMode = MusicLibraryMode.QuickPlay;

            menu.gameObject.SetActive(true);
        }

        private bool _sized;

        /// <summary>
        /// Careers and Quickplay are drawn as large as the main menu's first entry, Practice, History
        /// and Main Menu as its other entries.
        /// </summary>
        private void ApplyEntrySizes()
        {
            if (_sized)
            {
                return;
            }

            var main = FindAnyObjectByType<Main.MainMenu>(FindObjectsInactive.Include);
            if (main == null)
            {
                return;
            }

            GameObject MainEntry(string method)
            {
                foreach (var entry in main.transform.root.GetComponentsInChildren<NavigatableButton>(true))
                {
                    if (entry.Calls(main, method))
                    {
                        return entry.gameObject;
                    }
                }

                return null;
            }

            var largeEntry = MainEntry(nameof(Main.MainMenu.QuickPlay));
            var smallEntry = MainEntry(nameof(Main.MainMenu.Profiles));
            if (largeEntry == null || smallEntry == null)
            {
                return;
            }

            _sized = true;
            var large = Main.MenuEntryStyle.Read(largeEntry);
            var small = Main.MenuEntryStyle.Read(smallEntry);

            void Size(GameObject entry, Main.MenuEntryStyle style)
            {
                if (entry != null)
                {
                    Main.MenuEntryStyle.Read(entry).Apply(style);
                }
            }

            Size(FindButton(nameof(CareerList))?.gameObject, large);
            Size(_quickPlayButton, large);
            Size(FindButton(nameof(Practice))?.gameObject, small);
            Size(FindButton(nameof(Replays))?.gameObject, small);
            Size(FindButton(nameof(Back))?.gameObject, small);
        }

        private void OnEnable()
        {
            ApplyRetailMode();
            ApplyEntrySizes();

            // Set navigation scheme
            _ = Navigator.Instance.PushScheme(new NavigationScheme(new()
            {
                NavigationScheme.Entry.NavigateSelect,
                NavigationScheme.Entry.NavigateUp,
                NavigationScheme.Entry.NavigateDown,
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", () => MenuManager.Instance.PopMenu())
            }, true));
        }

        public void CareerList()
        {
            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.CareerList, false);
            menu.gameObject.SetActive(true);
        }

        public void Practice()
        {
            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.MusicLibrary, false);

            MusicLibraryMenu.LibraryMode = MusicLibraryMode.Practice;

            menu.gameObject.SetActive(true);
        }

        public void Replays()
        {
            MenuManager.Instance.PushMenu(MenuManager.Menu.History);
        }

        public void Back()
        {
            MenuManager.Instance.PopMenu();
        }

        private void OnDisable()
        {
            Navigator.Instance?.PopScheme();
        }
    }
}