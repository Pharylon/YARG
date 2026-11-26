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

        private void OnEnable()
        {
            ApplyRetailMode();

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