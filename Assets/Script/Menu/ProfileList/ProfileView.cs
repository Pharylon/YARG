using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XInput;
using UnityEngine.UI;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Helpers.Extensions;
using YARG.Input;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Dialogs;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings.Metadata;

namespace YARG.Menu.ProfileList
{
    public class ProfileView : NavigatableBehaviour
    {
        // Cache for gamepads that have been prompted for this session
        private static readonly Dictionary<InputDevice, GamepadBindingMode> _xinputGamepads = new();

        [Space]
        [SerializeField]
        private TextMeshProUGUI _profileName;
        [SerializeField]
        private Image _profilePicture;

        [Space]
        [SerializeField]
        private GameObject _connectGroup;
        [SerializeField]
        private GameObject _disconnectGroup;

        [Space]
        [SerializeField]
        private Button _moveUpButton;
        [SerializeField]
        private Button _moveDownButton;

        [Space]
        [SerializeField]
        private Sprite _profileGenericSprite;
        [SerializeField]
        private Sprite _profileBotSprite;

        public YargProfile Profile { get; private set; }

        /// <summary>The set-aside record this view represents, or null for a normal profile row.</summary>
        public PlayerContainer.UnloadedProfile UnloadedRecord { get; private set; }

        private ProfilesMenu _profileListMenu;
        private ProfileCenterPane  _profileCenterPane;

        public void Init(ProfilesMenu menu, YargProfile profile, ProfileCenterPane centerPane)
        {
            _profileListMenu = menu;
            _profileCenterPane = centerPane;
            UpdateDisplay(profile);
        }

        /// <summary>
        /// Shows a set-aside profile record that this version of the game could
        /// not load. The row is inert except for its delete button.
        /// </summary>
        public void InitUnloaded(ProfilesMenu menu, PlayerContainer.UnloadedProfile record, ProfileCenterPane sidebar)
        {
            _profileListMenu = menu;
            _profileCenterPane = sidebar;
            UnloadedRecord = record;

            _profileName.text = Localize.KeyFormat("Menu.ProfileList.UnloadedEntry", record.Name);

            // No connecting, disconnecting, or reordering an unloaded record. The
            // connect group starts inactive in the prefab and also contains the
            // row's delete button, so activate it and hide only the connect button
            _connectGroup.SetActive(true);
            _disconnectGroup.SetActive(false);
            var connectButton = _connectGroup.GetComponentInChildren<ColoredButton>();
            if (connectButton != null)
            {
                connectButton.gameObject.SetActive(false);
            }

            _moveUpButton.interactable = false;
            _moveDownButton.interactable = false;

            _profilePicture.sprite = _profileGenericSprite;
        }

        public void UpdateDisplay(YargProfile profile)
        {
            Profile = profile;
            _profileName.text = profile.Name;

            bool taken = PlayerContainer.IsProfileTaken(profile);
            _connectGroup.SetActive(!taken);
            _disconnectGroup.SetActive(taken);

            if (taken)
            {
                var player = PlayerContainer.GetPlayerFromProfile(profile);
                int index = PlayerContainer.GetPlayerIndex(player);

                // Disable the transition when changing interactability to prevent weird fades
                // when moving the profiles up and down.
                var upOriginal = DisableButtonTransition(_moveUpButton);
                var downOriginal = DisableButtonTransition(_moveDownButton);

                _moveUpButton.interactable = index > 0;
                _moveDownButton.interactable = index < PlayerContainer.Players.Count - 1;

                // Make sure to set the transitions back to normal afterwards
                _moveUpButton.colors = upOriginal;
                _moveDownButton.colors = downOriginal;
            }
            else if (!_profileListMenu.CanConnectProfile)
            {
                // Make the connect button gray if we have reached the connected profile cap
                var connectButton = _connectGroup.GetComponentInChildren<ColoredButton>();
                connectButton.DisableButton();
            }
            else
            {
                // In case the button was disabled before
                var connectButton = _connectGroup.GetComponentInChildren<ColoredButton>();
                connectButton.EnableButton();
            }

            if (profile.Avatar != null && profile.Avatar.IsValid)
            {
                var texture = profile.Avatar.LoadTexture(false);
                var sprite = Sprite.Create(texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
                _profilePicture.sprite = sprite;
                _profilePicture.rectTransform.localScale = new Vector3(1f, -1f);
            }
            else if (profile.IsBot)
            {
                _profilePicture.sprite = _profileBotSprite;
                _profilePicture.rectTransform.localScale = new Vector3(1f, 1f);
            }
            else
            {
                _profilePicture.sprite = _profileGenericSprite;
                _profilePicture.rectTransform.localScale = new Vector3(1f, 1f);
            }
        }

        protected override void OnSelectionChanged(bool selected)
        {
            base.OnSelectionChanged(selected);

            if (selected)
            {
                // Unloaded records have nothing to show in the sidebar
                if (UnloadedRecord is not null)
                {
                    _profileCenterPane.HideContents();
                    return;
                }

                _profileCenterPane.UpdateCenterPane(Profile, this);
            }
        }

        public async void RemoveProfile()
        {
            if (UnloadedRecord is not null)
            {
                RemoveUnloadedProfile();
                return;
            }

            // Bots currently delete instantly; give them the delayed-confirmation
            // dialog too so they can't be removed by an accidental click
            if (Profile.IsBot)
            {
                PresetSubTab.ShowCompactConfirmation(
                    Localize.KeyFormat("Menu.Dialog.ConfirmDelete.Title", Profile.Name),
                    Localize.Key("Menu.ProfileList.BotDelete"),
                    "Menu.Common.Delete", MenuData.Colors.CancelButton, () =>
                    {
                        DialogManager.Instance.ClearDialog();
                        RemoveNow();
                    },
                    cancelColor: MenuData.Colors.BrightButton,
                    armDelaySeconds: 2f);
                return;
            }

            bool remove = false;

            // Confirm that the user wants to delete the profile first by typing its name
            var dialog = DialogManager.Instance.ShowConfirmDeleteDialog(
                "Deleting this profile is permanent and you will lose all stats and binds. Play history will " +
                "remain and can be accessed in the <b>History</b> tab.", () => { remove = true; }, Profile.Name);

            // Wait...
            await dialog.WaitUntilClosed();

            if (!remove) return;

            RemoveNow();
        }

        private void RemoveNow()
        {
            if (Selected)
            {
                _profileCenterPane.HideContents();
            }

            if (PlayerContainer.RemoveProfile(Profile))
            {
                // Rebuild the list so emptied group headers disappear immediately
                _profileListMenu.RefreshProfileList();
            }
        }

        private void RemoveUnloadedProfile()
        {
            // Delayed-confirmation dialog (the button arms after a short delay);
            // the section header and row description already explain why the
            // profile couldn't load, so the message stays short
            PresetSubTab.ShowCompactConfirmation(
                Localize.Key("Menu.ProfileList.UnloadedDeleteTitle"),
                Localize.Key("Menu.ProfileList.UnloadedDelete"),
                "Menu.Common.Delete", MenuData.Colors.CancelButton, () =>
                {
                    DialogManager.Instance.ClearDialog();

                    if (Selected)
                    {
                        _profileCenterPane.HideContents();
                    }

                    if (PlayerContainer.DeleteUnloadedProfile(UnloadedRecord))
                    {
                        // Rebuild the list so an emptied "Couldn't Load" group's
                        // header goes away immediately
                        _profileListMenu.RefreshProfileList();
                    }
                },
                cancelColor: MenuData.Colors.BrightButton,
                armDelaySeconds: 2f);
        }

        public async UniTask<bool> PromptAddController()
        {
            var dialog = DialogManager.Instance.ShowList("Add Controller\n" +
                "<alpha=#44><size=65%><line-height=50%>\nIf your controller does not show up, try hitting a button/pad on " +
                "it first, and then retry.</size>");
            var player = PlayerContainer.GetPlayerFromProfile(Profile);

            bool selectedController = false;
            bool xinputDialogShowing = false;
            var listButtons = new List<(InputDevice controller, YARG.Menu.ColoredButton button, string text)>();

            // Add InputSystem devices immediately — fast, no probe
            foreach (var controller in InputSystem.devices)
            {
                if (!controller.enabled)
                {
                    continue;
                }

                var existingUser = PlayerContainer.GetControllerUser(controller);
                if (existingUser == player)
                {
                    continue;
                }

                var text = (existingUser is null) ?
                    DeviceIdentity.GetName(controller) :
                    Localize.KeyFormat("Menu.ProfileList.StealDevice", DeviceIdentity.GetName(controller), existingUser.Profile.Name);

                var listButton = dialog.AddListButton(text, async () =>
                {
                    xinputDialogShowing = IsGamepadOfUnknownKind(controller);
                    if (!await AssignController(player, controller))
                    {
                        return;
                    }

                    selectedController = true;
                });
                listButtons.Add((controller, listButton, text));
            }

            // While the list is up, a controller's entry lights up when a button is pressed on it, so
            // that two of the same kind can be told apart
            bool open = true;
            dialog.WaitUntilClosed().ContinueWith(() => open = false).Forget();
            while (open)
            {
                foreach (var (controller, listButton, text) in listButtons)
                {
                    if (listButton == null)
                    {
                        continue;
                    }

                    bool pressed = controller.added && DeviceIdentity.IsAnyButtonPressed(controller);
                    listButton.Text.text = pressed ? $"<color=#2DD9FE>\u25ba {text} \u25c4</color>" : text;
                }

                await UniTask.Yield();
            }

            if (xinputDialogShowing)
            {
                await UniTask.Yield();
                await DialogManager.Instance.WaitUntilCurrentClosed();
                await UniTask.Yield();
            }

            StatsManager.Instance.UpdateActivePlayers();

            return selectedController;
        }

        public async UniTask<bool> PromptAddMicrophone()
        {
            var dialog = DialogManager.Instance.ShowList("Add Microphone");
            var player = PlayerContainer.GetPlayerFromProfile(Profile);

            bool selectedMicrophone = false;

            bool TryAddMicrophone(InputDeviceInfo device)
            {
                var created = GlobalAudioHandler.CreateInputDevice(device);
                if (created is null)
                {
                    YargLogger.LogFormatWarning("Failed to initialize microphone `{0}`.", device.DisplayName);
                    DialogManager.Instance.ClearDialog();
                    DialogManager.Instance.ShowMessage("Microphone Error",
                        $"Failed to initialize microphone:\n\n{device.DisplayName}\n\nPlease try again or choose a different microphone.");
                    return false;
                }

                player.DeviceInfo.AddMicrophone(created);
                return true;
            }

            PopulateMicsAsync(dialog, player, mic =>
            {
                var claimant = PlayerContainer.GetMicrophoneUser(mic.Id);
                if (claimant is not null && claimant != player)
                {
                    claimant.DeviceInfo.RemoveMicrophone(mic.Id);
                }

                if (TryAddMicrophone(mic))
                {
                    selectedMicrophone = true;
                }
            }).Forget();

            await dialog.WaitUntilClosed();

            StatsManager.Instance.UpdateActivePlayers();

            return selectedMicrophone;
        }

        private static async UniTask PopulateMicsAsync(
            ListDialog dialog,
            YargPlayer player,
            Action<InputDeviceInfo> onMicSelected)
        {
            var placeholderButton = dialog.AddListButton("Scanning microphones...", () => { }, closeOnClick: false);
            placeholderButton.DisableButton();
            var ct = dialog.GetCancellationTokenOnDestroy();

            List<InputDeviceInfo> mics;
            try
            {
                mics = await UniTask.RunOnThreadPool(() => GlobalAudioHandler.GetAllInputDevices(),
                    cancellationToken: ct);
                await UniTask.SwitchToMainThread(cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (placeholderButton != null)
            {
                Destroy(placeholderButton.gameObject);
            }

            // Dialog may have been closed while microphones were being probed
            if (!dialog.IsOpen)
            {
                return;
            }

            int addedCount = 0;
            foreach (var mic in mics)
            {
                var existingUser = PlayerContainer.GetMicrophoneUser(mic.Id);
                if (existingUser == player)
                {
                    continue;
                }

                var text = (existingUser is null)
                    ? mic.DisplayName
                    : Localize.KeyFormat("Menu.ProfileList.StealDevice", mic.DisplayName, existingUser.Profile.Name);

                var localMic = mic;
                dialog.AddListButton(text, () => onMicSelected(localMic));
                addedCount++;
            }

            if (addedCount == 0)
            {
                var btn = dialog.AddListButton("No devices found", () => { }, closeOnClick: false);
                btn.DisableButton();
            }
        }

        /// <summary>
        /// Gives a controller to a player on the player's say-so, taking it from whoever holds it. For
        /// a controller that presents itself as a plain gamepad this asks what it is, and takes the
        /// answer for the bindings and for the game mode.
        /// </summary>
        /// <returns>False if that question was left unanswered (the controller is attached all the same).</returns>
        public static async UniTask<bool> AssignController(YargPlayer player, InputDevice controller)
        {
            var existingUser = PlayerContainer.GetControllerUser(controller);
            if (existingUser != player)
            {
                existingUser?.DeviceInfo.RemoveController(controller);
                player.DeviceInfo.AddControllerManually(controller);
            }

            if (!IsGamepadOfUnknownKind(controller))
            {
                return true;
            }

            var mode = await PromptGamepadMode(controller);
            if (!mode.HasValue || !controller.added)
            {
                return false;
            }

            // Before the mode change, which looks at what the controller is
            DeviceIdentity.SetInstrumentHint(controller, GetGameMode(mode.Value));
            if (GetGameMode(mode.Value) is { } gameMode)
            {
                player.ChooseGameMode(gameMode);
            }

            var (gameplay, menu) = player.DeviceInfo.GetBindingSetsForGamepad(mode.Value, player.Profile.GameMode);
            player.DeviceInfo.SetGameplayBindingsForController(controller, gameplay);
            player.DeviceInfo.SetMenuBindingsForController(controller, menu);
            return true;
        }

        private static bool IsGamepadOfUnknownKind(InputDevice controller)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return controller is XInputController;
#else
            return YARG.Helpers.LayoutHelper.InputDeviceToControllerFamily(controller) is ControllerFamily.Gamepad;
#endif
        }

        // The game mode the instrument is for; null for a plain gamepad, which says nothing
        private static YARG.Core.GameMode? GetGameMode(GamepadBindingMode mode)
        {
            return mode switch
            {
                GamepadBindingMode.CrkdGuitar_Mode1 or GamepadBindingMode.CrkdGuitar_Mode1_Fw30 or
                    GamepadBindingMode.WiitarThing_Guitar or GamepadBindingMode.RB4InstrumentMapper_Guitar
                    => YARG.Core.GameMode.FiveFretGuitar,
                GamepadBindingMode.RB4InstrumentMapper_GHLGuitar => YARG.Core.GameMode.SixFretGuitar,
                GamepadBindingMode.WiitarThing_Drums => YARG.Core.GameMode.FiveLaneDrums,
                GamepadBindingMode.RB4InstrumentMapper_Drums => YARG.Core.GameMode.FourLaneDrums,
                _ => null,
            };
        }

        private static async UniTask<GamepadBindingMode?> PromptGamepadMode(InputDevice xinput)
        {
            await DialogManager.Instance.WaitUntilCurrentClosed();

            // Check if this gamepad has been prompted for already
            if (_xinputGamepads.TryGetValue(xinput, out var existing))
            {
                return existing;
            }

            GamepadBindingMode? mode = null;

            var dialog = DialogManager.Instance.ShowList("Which kind of controller is this?");
            dialog.AddListButton("Gamepad", () => mode = GamepadBindingMode.Gamepad);
            dialog.AddListButton("CRKD Guitar (Mode 1)", () => mode = GamepadBindingMode.CrkdGuitar_Mode1);
            dialog.AddListButton("CRKD Guitar (Mode 1, FW3.0+)", () => mode = GamepadBindingMode.CrkdGuitar_Mode1_Fw30);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // These are Windows programs
            dialog.AddListButton("WiitarThing Guitar", () => mode = GamepadBindingMode.WiitarThing_Guitar);
            dialog.AddListButton("WiitarThing Drumkit", () => mode = GamepadBindingMode.WiitarThing_Drums);
            dialog.AddListButton("RB4InstrumentMapper Guitar",
                () => mode = GamepadBindingMode.RB4InstrumentMapper_Guitar);
            dialog.AddListButton("RB4InstrumentMapper GHL Guitar",
                () => mode = GamepadBindingMode.RB4InstrumentMapper_GHLGuitar);
            dialog.AddListButton("RB4InstrumentMapper Drumkit",
                () => mode = GamepadBindingMode.RB4InstrumentMapper_Drums);
#endif
            await dialog.WaitUntilClosed();

            if (mode.HasValue)
            {
                // Cache so we only prompt once
                _xinputGamepads[xinput] = mode.Value;
            }

            return mode;
        }

        public void ConnectButtonAction()
        {
            if (_profileListMenu.CanConnectProfile)
            {
                Connect(true).Forget();
            }
        }

        public async UniTask Connect(bool resolveDevices)
        {
            // Select item to prevent confusion (it has to be through the mouse in this case)
            SetSelected(true, SelectionOrigin.Mouse);

            if (PlayerContainer.IsProfileTaken(Profile))
            {
                YargLogger.LogFormatError("Attempted to connect already-taken profile {0}!", Profile.Name);
                return;
            }

            // Create player from profile
            var player = PlayerContainer.CreatePlayerFromProfile(Profile, resolveDevices);
            if (player is null)
            {
                YargLogger.LogFormatError("Failed to connect profile {0}!", Profile.Name);
                return;
            }

            if (!Profile.IsBot && player.DeviceInfo.HasNoDevices)
            {
                // Prompt the user to select a device
                if (!await PromptAddController())
                {
                    // Don't leak player when cancelling
                    PlayerContainer.DisposePlayer(player);
                    _profileListMenu.RefreshProfileList(Profile);
                    return;
                }
            }

            _profileListMenu.RefreshProfileList(Profile);
        }

        public void Disconnect()
        {
            // Select item to prevent confusion (it has to be through the mouse in this case)
            SetSelected(true, SelectionOrigin.Mouse);

            var player = PlayerContainer.GetPlayerFromProfile(Profile);
            if (player is null)
            {
                YargLogger.LogFormatError("Could not get player for profile {0}!", Profile.Name);
                return;
            }

            PlayerContainer.DisposePlayer(player);
            _profileListMenu.RefreshProfileList();
        }

        public void MoveUp()
        {
            _profileListMenu.MoveProfileUp(Profile);
        }

        public void MoveDown()
        {
            _profileListMenu.MoveProfileDown(Profile);
        }

        private static ColorBlock DisableButtonTransition(Button button)
        {
            var original = button.colors;

            var noFade = button.colors;
            noFade.fadeDuration = 0f;
            button.colors = noFade;

            return original;
        }
    }
}
