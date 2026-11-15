using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using YARG.Career;
using YARG.Core.Logging;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Dialogs;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// Band choices, built from the stock dialogs: a list dialog to pick, create, rename or delete a
    /// band, the rename dialog for typed text, and the confirm-delete dialog. Only one dialog can be
    /// open at a time, so each step waits for the previous dialog to close.
    /// </summary>
    public static class CareerBandDialogs
    {
        /// <summary>
        /// The band list: choose the active band (or none), make a new one, or manage the active one.
        /// <paramref name="onChanged"/> runs after anything changed.
        /// </summary>
        public static void ShowBands(Action onChanged)
        {
            var careers = ScoreContainer.Careers;
            if (careers is null || DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            var active = CareerBands.ActiveBand(careers);
            var dialog = DialogManager.Instance.ShowList(Localize.Key("Menu.Career.Band.Title"));
            dialog.ClearButtons();

            foreach (var band in careers.GetBands())
            {
                var members = CareerBands.MemberNames(careers, band.Id);
                var text = Localize.KeyFormat("Menu.Career.Band.ListEntry", band.Name,
                    members.Count == 0 ? Localize.Key("Menu.Career.Band.NoMembers") : Localize.List(members));
                if (band.Id == active?.Id)
                {
                    text = Localize.KeyFormat("Menu.Career.Band.ActiveEntry", text);
                }

                var id = band.Id;
                AddListButton(dialog, text, () => Select(id, onChanged));
            }

            AddListButton(dialog, active is null
                    ? Localize.KeyFormat("Menu.Career.Band.ActiveEntry", Localize.Key("Menu.Career.Band.NoBand"))
                    : Localize.Key("Menu.Career.Band.NoBand"),
                () => Select(null, onChanged));

            AddListButton(dialog, Localize.Key("Menu.Career.Band.New"), () => CreateBand(onChanged).Forget());

            if (active is not null)
            {
                AddListButton(dialog, Localize.KeyFormat("Menu.Career.Band.Rename", active.Name),
                    () => RenameBand(active, onChanged).Forget());
                AddListButton(dialog, Localize.KeyFormat("Menu.Career.Band.EditMembers", active.Name),
                    () => EditMembers(active, onChanged).Forget());
                AddListButton(dialog, Localize.KeyFormat("Menu.Career.Band.Delete", active.Name),
                    () => DeleteBand(active, onChanged).Forget());
            }

            dialog.AddDialogButton("Menu.Common.Close", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
        }

        private static void Select(Guid? bandId, Action onChanged)
        {
            CareerBands.ActiveBandId = bandId;
            onChanged?.Invoke();
        }

        private static async UniTaskVoid CreateBand(Action onChanged)
        {
            var name = await AskText(Localize.Key("Menu.Career.Band.AskName"));
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var hometown = await AskText(Localize.Key("Menu.Career.Band.AskHometown"));
            if (hometown is null)
            {
                return;
            }

            var genre = await AskText(Localize.Key("Menu.Career.Band.AskGenre"));
            if (genre is null)
            {
                return;
            }

            var members = await PickMembers(Localize.KeyFormat("Menu.Career.Band.PickMembers", name.Trim()),
                CareerBands.CurrentHumanPlayers(), "Menu.Career.Band.Create");
            if (members is null)
            {
                return;
            }

            var careers = ScoreContainer.Careers;
            var band = careers.CreateBand(name.Trim(), hometown.Trim(), genre.Trim(), members);
            CareerBands.ActiveBandId = band.Id;
            YargLogger.LogFormatInfo("Career: created band '{0}'.", band.Name);
            ToastManager.ToastSuccess(Localize.KeyFormat("Menu.Career.Band.Created", band.Name));
            onChanged?.Invoke();

            await OfferAdoption(band, members, onChanged);
        }

        /// <summary>
        /// If the new band's members already have careers going as exactly this group (profile-set
        /// saves), offer to carry that progress over to the band.
        /// </summary>
        private static async UniTask OfferAdoption(CareerBand band, List<Guid> members, Action onChanged)
        {
            var careers = ScoreContainer.Careers;
            var saves = CareerBands.AllCareers()
                .Select(career => careers.FindActiveSaveForProfiles(career.Id, members))
                .Where(save => save is not null)
                .ToList();
            if (saves.Count == 0)
            {
                return;
            }

            await NextDialog();

            var adopt = false;
            var dialog = DialogManager.Instance.ShowMessage(Localize.Key("Menu.Career.Band.AdoptTitle"),
                Localize.KeyFormat("Menu.Career.Band.AdoptMessage", saves.Count, band.Name));
            dialog.ClearButtons();
            dialog.AddDialogButton("Menu.Career.Band.AdoptNo", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
            dialog.AddDialogButton("Menu.Career.Band.AdoptYes", MenuData.Colors.ConfirmButton, () =>
            {
                adopt = true;
                DialogManager.Instance.ClearDialog();
            });

            await dialog.WaitUntilClosed();
            if (!adopt)
            {
                return;
            }

            var adopted = saves.Count(save => careers.AdoptSave(save.Id, band.Id));
            ToastManager.ToastSuccess(Localize.KeyFormat("Menu.Career.Band.Adopted", adopted));
            onChanged?.Invoke();
        }

        private static async UniTaskVoid RenameBand(CareerBand band, Action onChanged)
        {
            var name = await AskText(Localize.KeyFormat("Menu.Career.Band.AskRename", band.Name));
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            ScoreContainer.Careers.RenameBand(band.Id, name.Trim());
            onChanged?.Invoke();
        }

        private static async UniTaskVoid EditMembers(CareerBand band, Action onChanged)
        {
            var careers = ScoreContainer.Careers;
            var current = careers.GetBandMembers(band.Id);

            var members = await PickMembers(Localize.KeyFormat("Menu.Career.Band.PickMembers", band.Name),
                current, "Menu.Common.Confirm");
            if (members is null)
            {
                return;
            }

            foreach (var added in members.Except(current))
            {
                careers.AddBandMember(band.Id, added);
            }

            foreach (var removed in current.Except(members))
            {
                careers.RemoveBandMember(band.Id, removed);
            }

            onChanged?.Invoke();
        }

        private static async UniTaskVoid DeleteBand(CareerBand band, Action onChanged)
        {
            await NextDialog();

            DialogManager.Instance.ShowConfirmDeleteDialog(Localize.Key("Menu.Career.Band.DeleteMessage"), () =>
            {
                ScoreContainer.Careers.SoftDeleteBand(band.Id);
                if (CareerBands.ActiveBandId == band.Id)
                {
                    CareerBands.ActiveBandId = null;
                }

                ToastManager.ToastSuccess(Localize.KeyFormat("Menu.Career.Band.Deleted", band.Name));
                onChanged?.Invoke();
            }, band.Name);
        }

        /// <summary>
        /// Typed text through the rename dialog. Null if cancelled; empty if confirmed with nothing typed.
        /// </summary>
        private static async UniTask<string> AskText(string title)
        {
            await NextDialog();

            string text = null;
            var confirmed = false;

            var dialog = DialogManager.Instance.ShowRenameDialog(title, value => text = value);
            dialog.ClearButtons();
            dialog.AddDialogButton("Menu.Common.Cancel", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
            dialog.AddDialogButton("Menu.Common.Confirm", MenuData.Colors.ConfirmButton, () =>
            {
                confirmed = true;
                DialogManager.Instance.SubmitAndClearDialog();
            });

            await dialog.WaitUntilClosed();
            return confirmed ? text ?? string.Empty : null;
        }

        /// <summary>
        /// Pick band members from the existing (non-bot) profiles. Null if cancelled; never empty.
        /// </summary>
        private static async UniTask<List<Guid>> PickMembers(string title, IEnumerable<Guid> initial,
            string confirmKey)
        {
            await NextDialog();

            var profiles = PlayerContainer.Profiles.Where(profile => !profile.IsBot).ToList();
            var chosen = new HashSet<Guid>(initial.Where(id => profiles.Any(profile => profile.Id == id)));
            var confirmed = false;

            var dialog = DialogManager.Instance.ShowList(title);
            dialog.ClearButtons();

            foreach (var profile in profiles)
            {
                var id = profile.Id;
                var name = profile.Name;
                ColoredButton button = null;
                button = AddListButton(dialog, MemberText(name, chosen.Contains(id)), () =>
                {
                    if (!chosen.Remove(id))
                    {
                        chosen.Add(id);
                    }

                    button.Text.text = MemberText(name, chosen.Contains(id));
                }, false);
            }

            dialog.AddDialogButton("Menu.Common.Cancel", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
            dialog.AddDialogButton(confirmKey, MenuData.Colors.ConfirmButton, () =>
            {
                if (chosen.Count == 0)
                {
                    ToastManager.ToastInformation(Localize.Key("Menu.Career.Band.NeedMember"));
                    return;
                }

                confirmed = true;
                DialogManager.Instance.ClearDialog();
            });

            await dialog.WaitUntilClosed();

            // Keep the profile order, which is the order the profiles menu shows.
            return confirmed ? profiles.Select(profile => profile.Id).Where(chosen.Contains).ToList() : null;
        }

        private static string MemberText(string name, bool chosen)
        {
            return Localize.KeyFormat(chosen ? "Menu.Career.Band.MemberOn" : "Menu.Career.Band.MemberOff", name);
        }

        /// <summary>
        /// A list button that a controller can reach too: the stock list dialog does not add its list
        /// buttons to the dialog's navigation, so this does.
        /// </summary>
        private static ColoredButton AddListButton(ListDialog dialog, string text, UnityAction handler,
            bool closeOnClick = true)
        {
            var button = dialog.AddListButton(text, handler, closeOnClick);

            var group = dialog.GetComponentInChildren<NavigationGroup>();
            if (group != null)
            {
                // The list button prefab has no navigatable of its own (the dialog button prefab does).
                var navigatable = button.GetComponentInChildren<NavigatableBehaviour>();
                if (navigatable == null)
                {
                    navigatable = RuntimeNavigatable.Attach(button.gameObject, () => button.OnClick.Invoke());
                }

                group.AddNavigatable(navigatable);
            }

            return button;
        }

        /// <summary>
        /// Waits for the dialog that started this step (a list button closes its dialog after its handler
        /// runs) and one more frame, so the next dialog can open.
        /// </summary>
        private static async UniTask NextDialog()
        {
            await DialogManager.Instance.WaitUntilCurrentClosed();
            await UniTask.Yield();
        }
    }
}
