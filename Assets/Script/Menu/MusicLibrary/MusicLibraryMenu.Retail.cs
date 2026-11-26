using System;
using YARG.Career;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Menu.Filters;
using YARG.Menu.Navigation;

namespace YARG.Menu.MusicLibrary
{
    // Retail mode in the library (see YARG.Career.RetailMode): quickplay lists only the songs career
    // play has unlocked, with a way to see all songs for now; and the library can be opened to pick one
    // song for someone else (a gig's closer, a song to play in place of a bonus gig's).
    public partial class MusicLibraryMenu
    {
        /// <summary>
        /// Set while the library is open to pick a song: confirming a song hands it here instead of
        /// starting it. Every song is listed. Cleared when a song is picked or the library is left.
        /// </summary>
        private static Action<SongEntry> _songPicker;
        private static string _songPickerTitle;

        public static bool PickMode => _songPicker is not null;

        /// <summary>
        /// Only unlocked songs are listed right now: retail mode, in quickplay, not picking a song,
        /// and "show all songs" is off.
        /// </summary>
        public static bool RetailFilterActive =>
            RetailMode.FilterActive && LibraryMode == MusicLibraryMode.QuickPlay && !PickMode;

        /// <summary>
        /// What decides whether a song is listed: the filters menu's choice, and in retail mode whether
        /// the song is unlocked. Null: every song.
        /// </summary>
        public static Func<SongEntry, bool> EffectiveFilter
        {
            get
            {
                var filters = FiltersMenu.ActiveFilterPredicate;
                if (!RetailFilterActive)
                {
                    return filters;
                }

                return filters is null
                    ? RetailMode.IsUnlocked
                    : song => RetailMode.IsUnlocked(song) && filters(song);
            }
        }

        /// <summary>
        /// Opens the library over the current menu to pick one song. <paramref name="onPicked"/> gets
        /// the song with the library still open; it is expected to close it (MenuManager.PopMenu).
        /// </summary>
        public static void OpenSongPicker(string title, Action<SongEntry> onPicked)
        {
            _songPicker = onPicked;
            _songPickerTitle = title;
            LibraryMode = MusicLibraryMode.QuickPlay;

            var menu = MenuManager.Instance.PushMenu(MenuManager.Menu.MusicLibrary, false);
            menu.gameObject.SetActive(true);
        }

        /// <summary>
        /// A song was confirmed while picking: hand it over. False when not picking.
        /// </summary>
        public static bool TryPick(SongEntry song)
        {
            if (_songPicker is not { } picker || song is null)
            {
                return false;
            }

            ClearSongPicker();
            picker(song);
            return true;
        }

        private static void ClearSongPicker()
        {
            _songPicker = null;
            _songPickerTitle = null;
        }

        // What the list was last built for. Entering the library again with something else in force
        // (the filter toggled, a song unlocked, a pick started or ended) rebuilds it.
        private static string _retailListState;

        private static string RetailListState()
        {
            return $"{RetailFilterActive}/{PickMode}/{(RetailFilterActive ? RetailMode.UnlockedSongs.Count : 0)}";
        }

        private void EnsureRetailListCurrent()
        {
            var state = RetailListState();
            if (state != _retailListState && _reloadState == MusicLibraryReloadState.None)
            {
                _reloadState = MusicLibraryReloadState.Full;
            }

            _retailListState = state;
        }

        /// <summary>
        /// In retail mode quickplay gets a button to list every song, or only the unlocked ones again.
        /// </summary>
        private NavigationScheme.Entry? RetailToggleEntry()
        {
            if (!RetailMode.Enabled || LibraryMode != MusicLibraryMode.QuickPlay || PickMode ||
                MenuState != MenuState.Library)
            {
                return null;
            }

            return new NavigationScheme.Entry(MenuAction.Select,
                RetailMode.ShowAllSongs ? "Menu.Retail.ShowUnlocked" : "Menu.Retail.ShowAll", ToggleRetailFilter);
        }

        private void ToggleRetailFilter()
        {
            RetailMode.ShowAllSongs = !RetailMode.ShowAllSongs;
            _retailListState = RetailListState();
            Refresh();
        }
    }
}
