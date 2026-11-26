using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using YARG.Career;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Dialogs;
using YARG.Menu.Persistent;

namespace YARG.Menu.Career
{
    /// <summary>
    /// Gigs played as a show (<see cref="CareerCompletionMode.FullTierPlaylist"/>): the "Play the full set" or
    /// "Pick your set" row, the song picker of a make-your-own setlist, and starting the show (see
    /// <see cref="CareerShow"/>).
    /// </summary>
    public partial class CareerMenu
    {
        private ShowViewType ShowRow(CareerTier tier, float reveal, Action startShow)
        {
            var set = CareerSetlists.SetSongIndexes(tier).Count;
            var row = CareerSetlists.PicksSongs(tier)
                ? new ShowViewType(tier.Id, Localize.Key("Menu.Career.Show.PickRow"),
                    Localize.KeyFormat("Menu.Career.Show.PickDetail", CareerSetlists.RequiredSongs(tier), set),
                    (tier.Songs?.Length ?? 0) == 0, startShow)
                : ShowViewType.FullSet(tier, set, (tier.Songs?.Length ?? 0) == 0, startShow);
            row.Reveal = reveal;
            return row;
        }

        private void StartShow(int tierIndex)
        {
            if (_career is null || DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            var tier = _career.Tiers[tierIndex];
            if (CareerSetlists.PicksSongs(tier))
            {
                PickAndStartShow(tierIndex).Forget();
                return;
            }

            StartShow(tierIndex, CareerSetlists.ShowSongIndexes(tier));
        }

        private void StartShow(int tierIndex, IReadOnlyList<int> songIndexes)
        {
            if (!CareerShow.TryStart(_career, tierIndex, songIndexes, CareerBands.ActiveBandId, out _))
            {
                ToastManager.ToastWarning(Localize.Key("Menu.Career.Show.Failed"));
                return;
            }

            OnSongPlayed();
            MenuManager.Instance.PushMenu(MenuManager.Menu.DifficultySelect);
        }

        /// <summary>
        /// Make your own setlist: a list of the set's songs to tick (mystery songs stay hidden), then the show
        /// of the picked songs.
        /// </summary>
        private async UniTaskVoid PickAndStartShow(int tierIndex)
        {
            var career = _career;
            var tier = career.Tiers[tierIndex];
            var required = CareerSetlists.RequiredSongs(tier);
            var evaluation = _evaluation;

            var chosen = new HashSet<int>();
            var confirmed = false;

            var dialog = DialogManager.Instance.ShowList(Localize.KeyFormat("Menu.Career.Show.PickTitle", required));
            dialog.ClearButtons();

            foreach (var songIndex in CareerSetlists.SetSongIndexes(tier))
            {
                var index = songIndex;
                var name = PickName(tier, tierIndex, index, evaluation);
                ColoredButton button = null;
                button = CareerBandDialogs.AddNavigableListButton(dialog, PickText(name, false), () =>
                {
                    if (!chosen.Remove(index))
                    {
                        chosen.Add(index);
                    }

                    button.Text.text = PickText(name, chosen.Contains(index));
                }, false);
            }

            dialog.AddDialogButton("Menu.Common.Cancel", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
            dialog.AddDialogButton("Menu.Career.Show.PickStart", MenuData.Colors.ConfirmButton, () =>
            {
                if (chosen.Count != required)
                {
                    ToastManager.ToastInformation(Localize.KeyFormat("Menu.Career.Show.PickNeed", required, chosen.Count));
                    return;
                }

                confirmed = true;
                DialogManager.Instance.ClearDialog();
            });

            await dialog.WaitUntilClosed();

            if (!confirmed || _career != career)
            {
                return;
            }

            var picked = CareerSetlists.ShowSongIndexes(tier, chosen);
            if (picked is not null)
            {
                StartShow(tierIndex, picked);
            }
        }

        private static string PickName(CareerTier tier, int tierIndex, int songIndex, CareerEvaluation evaluation)
        {
            var song = tier.Songs[songIndex];
            var completed = evaluation?.Tiers[tierIndex].Songs[songIndex].Completed ?? false;
            if (CareerSetlists.IsHidden(tier, song, completed))
            {
                return CareerSetlistText.HiddenTitle(song);
            }

            return song.SongEntry is { } entry
                ? Localize.KeyFormat("Menu.Career.Sheet.GigSong", entry.Name, entry.Artist)
                : song.Description ?? Localize.Key("Menu.Career.SongMissing");
        }

        private static string PickText(string name, bool chosen)
        {
            return Localize.KeyFormat(chosen ? "Menu.Career.Show.PickOn" : "Menu.Career.Show.PickOff", name);
        }
    }
}
