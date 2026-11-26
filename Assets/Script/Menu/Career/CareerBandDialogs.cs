using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
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
        /// What can be done with one band: its sheet and history, its details, its members, deleting it.
        /// <paramref name="onChanged"/> runs after anything changed.
        /// </summary>
        public static void ShowManage(CareerBand band, Action onChanged)
        {
            if (band is null || ScoreContainer.Careers is null || DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            var dialog = DialogManager.Instance.ShowList(band.Name);
            dialog.ClearButtons();

            // The dialog is titled with the band's name, so the options do not repeat it.
            AddListButton(dialog, Localize.Key("Menu.Career.Band.Rename"),
                () => RenameBand(band, onChanged).Forget());
            AddListButton(dialog, Localize.Key("Menu.Career.Band.EditHometown"),
                () => EditHometown(band, onChanged).Forget());
            AddListButton(dialog, Localize.Key("Menu.Career.Band.EditMembers"),
                () => EditMembers(band, onChanged).Forget());
            AddListButton(dialog, Localize.Key("Menu.Career.Band.ChooseIcon"),
                () => CareerIcons.ChooseBandIcon(band, onChanged));
            if (CareerIcons.HasBandIcon(band.Id))
            {
                AddListButton(dialog, Localize.Key("Menu.Career.Band.RemoveIcon"), () =>
                {
                    CareerIcons.RemoveBandIcon(band);
                    onChanged?.Invoke();
                });
            }

            AddListButton(dialog, Localize.Key("Menu.Career.Band.Delete"),
                () => DeleteBand(band, onChanged).Forget());

            dialog.AddDialogButton("Menu.Common.Close", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);
        }

        /// <summary>
        /// Asks for a new band's name, hometown and members, creates it and makes it the active
        /// band. <paramref name="onChanged"/> runs once it exists.
        /// </summary>
        public static void CreateBand(Action onChanged)
        {
            CreateBandAsync(onChanged).Forget();
        }

        private static async UniTaskVoid CreateBandAsync(Action onChanged)
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

            var members = await PickMembers(Localize.KeyFormat("Menu.Career.Band.PickMembers", name.Trim()),
                CareerBands.CurrentHumanPlayers(), "Menu.Career.Band.Create");
            if (members is null)
            {
                return;
            }

            var careers = ScoreContainer.Careers;
            var band = careers.CreateBand(name.Trim(), hometown.Trim(), members);
            CareerBands.ActiveBandId = band.Id;
            YargLogger.LogFormatInfo("Career: created band '{0}'.", band.Name);
            ToastManager.ToastSuccess(Localize.KeyFormat("Menu.Career.Band.Created", band.Name));
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

        /// <summary>
        /// Change the hometown; confirming an empty field clears it.
        /// </summary>
        private static async UniTaskVoid EditHometown(CareerBand band, Action onChanged)
        {
            var hometown = await AskText(Localize.KeyFormat("Menu.Career.Band.AskEditHometown", band.Name), band.Hometown);
            if (hometown is null)
            {
                return;
            }

            ScoreContainer.Careers.SetBandHometown(band.Id, hometown.Trim());
            onChanged?.Invoke();
        }

        private static async UniTaskVoid ShowAfterClose(Action show)
        {
            await NextDialog();
            show();
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
        private static async UniTask<string> AskText(string title, string initial = null)
        {
            await NextDialog();

            string text = null;
            var confirmed = false;

            var dialog = DialogManager.Instance.ShowRenameDialog(title, value => text = value);
            if (!string.IsNullOrEmpty(initial) && dialog.GetComponentInChildren<TMP_InputField>() is { } input)
            {
                input.text = initial;
            }

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
        /// Pick band members from the existing profiles (bots only for test bands, see CareerBotCredit). Null if cancelled; never empty.
        /// </summary>
        private static async UniTask<List<Guid>> PickMembers(string title, IEnumerable<Guid> initial,
            string confirmKey)
        {
            await NextDialog();

            // Bots only where they are allowed as members (test bands, never in release builds).
            var profiles = PlayerContainer.Profiles
                .Where(profile => !profile.IsBot || CareerBotCredit.BOT_MEMBERS_ALLOWED).ToList();
            var chosen = new HashSet<Guid>(initial.Where(id => profiles.Any(profile => profile.Id == id)));
            var confirmed = false;

            var dialog = DialogManager.Instance.ShowList(title);
            dialog.ClearButtons();

            foreach (var profile in profiles)
            {
                var id = profile.Id;
                var name = profile.Name;
                ColoredButton button = null;
                Menu.ColoredButton pill = null;
                button = AddListButton(dialog, MemberText(name, chosen.Contains(id)), () =>
                {
                    if (!chosen.Remove(id))
                    {
                        chosen.Add(id);
                    }

                    button.Text.text = MemberText(name, chosen.Contains(id));
                    ShowMemberPill(pill, chosen.Contains(id));
                }, false);

                // A "Member" pill on the right, like the lineup's "Connected", when one can be made.
                pill = AddMemberPill(button);
                if (pill != null)
                {
                    button.Text.text = name;
                    ShowMemberPill(pill, chosen.Contains(id));
                }
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

        private static Menu.ColoredButton AddMemberPill(ColoredButton row)
        {
            if (CareerView.PillPrefab == null)
            {
                return null;
            }

            var pill = UnityEngine.Object.Instantiate(CareerView.PillPrefab, row.transform);
            var rect = (UnityEngine.RectTransform) pill.transform;
            rect.anchorMin = new UnityEngine.Vector2(1f, 0.18f);
            rect.anchorMax = new UnityEngine.Vector2(1f, 0.82f);
            rect.pivot = new UnityEngine.Vector2(1f, 0.5f);
            rect.sizeDelta = new UnityEngine.Vector2(170f, 0f);
            rect.anchoredPosition = new UnityEngine.Vector2(-20f, 0f);

            // Only a label: the row takes the clicks.
            foreach (var graphic in pill.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            foreach (var navigatable in pill.GetComponentsInChildren<NavigatableBehaviour>(true))
            {
                navigatable.enabled = false;
            }

            return pill;
        }

        private static void ShowMemberPill(Menu.ColoredButton pill, bool member)
        {
            if (pill == null)
            {
                return;
            }

            pill.SetBackgroundAndTextColor(member ? CareerView.PillOnColor : CareerView.PillOffColor);
            pill.Text.text = Localize.Key(member ? "Menu.Career.Band.MemberPill" : "Menu.Career.Band.MemberPillOff");
        }

        private static string MemberText(string name, bool chosen)
        {
            // With a pill on the row, the name alone.
            if (CareerView.PillPrefab != null)
            {
                return name;
            }

            return Localize.KeyFormat(chosen ? "Menu.Career.Band.MemberOn" : "Menu.Career.Band.MemberOff", name);
        }

        /// <summary>
        /// <see cref="AddListButton"/> for other career dialogs (the setlist picker).
        /// </summary>
        public static ColoredButton AddNavigableListButton(ListDialog dialog, string text, UnityAction handler,
            bool closeOnClick = true) => AddListButton(dialog, text, handler, closeOnClick);

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
