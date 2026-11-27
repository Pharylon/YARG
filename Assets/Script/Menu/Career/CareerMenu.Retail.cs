using System.Linq;
using YARG.Career;
using YARG.Core.Song;
using YARG.Localization;
using YARG.Menu.MusicLibrary;
using YARG.Menu.Persistent;
using YARG.Scores;
using YARG.Song;

namespace YARG.Menu.Career
{
    // Retail mode in the gig list (see YARG.Career.RetailMode): the two ways to unlock a song that is
    // in no setlist. A gig's closer: once every song of the gig has 3 stars, the players pick any song
    // to close it out. A bonus gig's songs: any song can be played in place of one of them.
    public partial class CareerMenu
    {
        /// <summary>
        /// The row that ends a gig's list in retail mode: what its closer was, or the offer to pick
        /// one, or what it takes to get to. Null outside retail mode.
        /// </summary>
        private ShowViewType CloserRow(CareerTier tier, int tierIndex, CareerEvaluation.TierResult result)
        {
            if (!RetailMode.Enabled || tier.Songs is null || tier.Songs.Length == 0)
            {
                return null;
            }

            // Already played: the gig keeps its closer, which can be played again.
            var closer = _careerSaveId > 0 ? ScoreContainer.Careers?.GetCloser(_careerSaveId, tier.Id) : null;
            if (closer is not null)
            {
                var song = closer.SongChecksum is null ? null : FindSong(HashWrapper.Create(closer.SongChecksum));
                var name = song is null ? Localize.Key("Menu.Career.SongMissing") : song.Name.ToString();
                return new ShowViewType(tier.Id, Localize.KeyFormat("Menu.Retail.CloserPlayed", name),
                    song is null ? string.Empty : Localize.KeyFormat("Menu.Retail.CloserPlayedDetail", song.Artist),
                    true, () =>
                    {
                        if (song is not null)
                        {
                            StartPicked(song, CloserContext(tier, tierIndex), false);
                        }
                    });
            }

            if (!CloserEarned(tier, result))
            {
                return new ShowViewType(tier.Id, Localize.Key("Menu.Retail.Closer"),
                    Localize.Key("Menu.Career.Locked"), true, () =>
                        ToastManager.ToastInformation(
                            Localize.KeyFormat("Menu.Retail.CloserLocked", RetailMode.CLOSER_MIN_STARS)));
            }

            return new ShowViewType(tier.Id, Localize.Key("Menu.Retail.CloserPick"), string.Empty, true, () =>
                    MusicLibraryMenu.OpenSongPicker(Localize.Key("Menu.Retail.CloserPick"),
                        song => StartPicked(song, CloserContext(tier, tierIndex), true)));
        }

        /// <summary>
        /// Every song of the gig that can be played has the stars a closer takes.
        /// </summary>
        private static bool CloserEarned(CareerTier tier, CareerEvaluation.TierResult result)
        {
            for (var i = 0; i < tier.Songs.Length; i++)
            {
                if (!CareerSetlists.CanBePlayed(tier, tier.Songs[i]) || tier.Songs[i].IsEncore)
                {
                    continue;
                }

                var song = i < result.Songs.Count ? result.Songs[i] : null;
                if (song is null || !song.Completed || song.BestStars < RetailMode.CLOSER_MIN_STARS)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The selected gig offers "Play Another Song Instead": retail mode, on a bonus gig.
        /// </summary>
        private bool SubstituteOffered()
        {
            if (!RetailMode.Enabled || _career is null)
            {
                return false;
            }

            var selected = SelectedGig();
            // Only a gig with songs of its own: user picks are chosen freely already.
            return _career.Tiers.Any(tier => tier.Id == selected && SwapsAllowed(tier) &&
                tier.Songs.Any(song => !song.IsUserPick && !song.IsEncore));
        }

        // What the button bar was last built for; it is rebuilt when the selected gig changes that.
        private bool _navigationSet;
        private bool _navigationOffersSubstitute;

        private void RefreshNavigationForGig()
        {
            if (_navigationSet && !_videoPlaying && _navigationOffersSubstitute != SubstituteOffered())
            {
                SetMenuNavigation(true);
            }
        }

        private CareerContext CloserContext(CareerTier tier, int tierIndex)
        {
            return new CareerContext
            {
                CareerId = _career.Id,
                TierId = tier.Id,
                TierIndex = tierIndex,
                VenueHint = tier.VenueHint,
                SongIndex = -1,
                BandId = CareerBands.ActiveBandId,
                Closer = true,
            };
        }

        private static SongEntry FindSong(HashWrapper hash)
        {
            return SongContainer.SongsByHash.TryGetValue(hash, out var songs) && songs.Count > 0 ? songs[0] : null;
        }

        /// <summary>
        /// Orange on a song of a bonus gig: pick any song to play in its place. The result counts for
        /// that song of the gig, and the song played is unlocked.
        /// </summary>
        private void PickSubstitute()
        {
            if (!RetailMode.Enabled || _career is null || _videoPlaying || CurrentSelection is not SongViewType row ||
                row.TierIndex >= _career.Tiers.Count)
            {
                return;
            }

            var tier = _career.Tiers[row.TierIndex];
            if (!SwapsAllowed(tier) || row.Song.IsEncore || row.Song.IsUserPick || CareerSetlists.IsShow(tier))
            {
                return;
            }

            var context = new CareerContext
            {
                CareerId = _career.Id,
                TierId = tier.Id,
                TierIndex = row.TierIndex,
                VenueHint = tier.VenueHint,
                CareerSongId = row.Song.Id,
                SongIndex = row.SongIndex,
                BandId = CareerBands.ActiveBandId,
                Substitute = true,
            };

            var replaced = row.SongEntry?.Name.ToString() ?? Localize.Key("Menu.Career.SongMissing");
            MusicLibraryMenu.OpenSongPicker(Localize.KeyFormat("Menu.Retail.SubstituteTitle", replaced),
                song => StartPicked(song, context, true), CareerThemes.Filter(tier.Theme));
        }

        /// <summary>
        /// Retail mode lets a gig's songs be swapped for others: any song in a bonus gig, a song that
        /// fits the theme in a themed gig.
        /// </summary>
        private static bool SwapsAllowed(CareerTier tier)
        {
            return RetailMode.Enabled && tier is not null && (tier.IsBonus || tier.Theme is { IsEmpty: false });
        }

        /// <summary>
        /// One of a gig's user picks: the players pick any song of their library that fits the gig's
        /// theme, and play it as that song of the gig. In every mode, not only retail mode.
        /// </summary>
        private void PickUserSong(CareerTier tier, int tierIndex, int songIndex)
        {
            if (_career is null || _videoPlaying || songIndex < 0 || songIndex >= tier.Songs.Length)
            {
                return;
            }

            var filter = CareerThemes.Filter(tier.Theme);
            if (filter is not null && !SongContainer.Songs.Any(filter))
            {
                ToastManager.ToastWarning(Localize.Key("Menu.Career.Theme.NoSongs"));
                return;
            }

            var context = new CareerContext
            {
                CareerId = _career.Id,
                TierId = tier.Id,
                TierIndex = tierIndex,
                VenueHint = tier.VenueHint,
                CareerSongId = tier.Songs[songIndex].Id,
                SongIndex = songIndex,
                BandId = CareerBands.ActiveBandId,
                Substitute = true,
            };

            MusicLibraryMenu.OpenSongPicker(Localize.Key("Menu.Career.Theme.PickTitle"),
                song => StartPicked(song, context, true), filter);
        }

        /// <summary>The song a result was played on, from its completion row.</summary>
        private SongEntry PlayedSong(CareerEvaluation.SongResult result)
        {
            if (result is not { Completed: true } ||
                !_completionById.TryGetValue(result.CountedCompletionId, out var row) ||
                row.SongChecksum is not { Length: > 0 } checksum)
            {
                return null;
            }

            return FindSong(HashWrapper.Create(checksum));
        }

        /// <summary>
        /// Plays a song of the library under the career context. <paramref name="fromLibrary"/>: the
        /// library is still open on top of this menu and is closed first (which re-enables this menu,
        /// clearing any career context, so the song is set after).
        /// </summary>
        private void StartPicked(SongEntry song, CareerContext context, bool fromLibrary)
        {
            if (fromLibrary)
            {
                MenuManager.Instance.PopMenu();
            }

            GlobalVariables.State.SetPlaybackSong(song, context);
            OnSongPlayed();
            MenuManager.Instance.PushMenu(MenuManager.Menu.DifficultySelect);
        }
    }
}
