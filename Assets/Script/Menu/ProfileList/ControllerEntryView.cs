using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Helpers;
using YARG.Input;
using YARG.Input.Bindings;
using YARG.Localization;
using YARG.Player;

namespace YARG.Menu.ProfileList
{
    public class ControllerEntryView : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _name;
        [SerializeField]
        private TMP_Dropdown _gameplayBindingSetDropdown;
        [SerializeField]
        private TMP_Dropdown _menuBindingSetDropdown;
        [SerializeField]
        private GameObject _drawer;
        [SerializeField]
        private Transform _arrow;
        [SerializeField]
        private LayoutElement _layoutElement;
        [SerializeField]
        private BindingsIndicator _gameplayIndicator;
        [SerializeField]
        private BindingsIndicator _menuIndicator;
        [SerializeField]
        private TextMeshProUGUI _gameplayBindingsTitle;
        [SerializeField]
        private TextMeshProUGUI _menuBindingsTitle;
        [SerializeField]
        private Button _jumpToGameplayBindingsButton;
        [SerializeField]
        private Button _jumpToMenuBindingsButton;

        public YargProfile Profile { get; private set; }
        private ProfileView _profileView;
        private ProfileCenterPane _centerPane;
        public InputDevice Controller { get; private set; }

        private List<ReusableBindingSet> _gameplayBindingSetsByIndex = new();
        public bool GameplayBindingSetsExist => _gameplayBindingSetsByIndex.Count > 1; // Account for null entry
        private List<ReusableBindingSet> _menuBindingSetsByIndex = new();
        public bool MenuBindingSetsExist => _menuBindingSetsByIndex.Count > 1; // Account for null entry

#nullable enable
        public ReusableBindingSet? GameplayBindingSet => _gameplayBindingSetsByIndex[_gameplayBindingSetDropdown.value];
        public ReusableBindingSet? MenuBindingSet => _menuBindingSetsByIndex[_menuBindingSetDropdown.value];
#nullable disable

        public bool DrawerOpen => _drawer.activeSelf;

        public void Init(YargProfile profile, ProfileView profileView, ProfileCenterPane centerPane, InputDevice controller, bool drawerOpen)
        {
            Profile = profile;
            _profileView = profileView;
            _centerPane = centerPane;
            Controller = controller;
            PopulateDropdownOptions();
            SetDrawerOpen(drawerOpen);

            _gameplayBindingsTitle.text = $"{Localize.Key("Menu.ProfileList.BindingsTitle", Profile.GameMode)}:";
            _menuBindingsTitle.text = $"{Localize.Key("Menu.ProfileList.BindingsTitle.Menu")}:";
        }

        public void Remove()
        {
            var player = PlayerContainer.GetPlayerFromProfile(Profile);
            player.DeviceInfo.RemoveController(Controller);
            _centerPane.UpdateCenterPane(Profile, _profileView);
        }

        private void PopulateDropdownOptions()
        {
            var player = PlayerContainer.GetPlayerFromProfile(Profile);

            var family = LayoutHelper.InputDeviceToControllerFamily(Controller);

            _gameplayBindingSetsByIndex.Clear();
            _gameplayBindingSetsByIndex.Add(null); // Always start with the None option
            _gameplayBindingSetsByIndex.AddRange(BindingsContainer.GetBindingSetsForControllerInMode(family, Profile.GameMode));
            _gameplayBindingSetDropdown.options.Clear();

            foreach (var bindingSet in _gameplayBindingSetsByIndex)
            {
                if (bindingSet is null)
                {
                    _gameplayBindingSetDropdown.options.Add(new(GameplayBindingSetsExist ?
                        "<i>No gameplay bindings</i>" :
                        "<i><color=grey>No gameplay bindings available</color></i>"
                        
                    ));
                    continue;
                }

                _gameplayBindingSetDropdown.options.Add(new(bindingSet.Name));
            }

            _gameplayBindingSetDropdown.interactable = GameplayBindingSetsExist;
            _gameplayBindingSetDropdown.SetValueWithoutNotify(_gameplayBindingSetsByIndex.IndexOf(player.DeviceInfo.GetSelectedGameplayBindingsForController(Controller)));
            UpdateGameplayBindingsStatus();

            _menuBindingSetsByIndex.Clear();
            _menuBindingSetsByIndex.Add(null); // Always start with the None option
            _menuBindingSetsByIndex.AddRange(BindingsContainer.GetBindingSetsForControllerInMode(family, GameMode.Menu));
            _menuBindingSetDropdown.options.Clear();

            foreach (var bindingSet in _menuBindingSetsByIndex)
            {
                if (bindingSet is null)
                {
                    _menuBindingSetDropdown.options.Add(new(MenuBindingSetsExist ?
                        "<i>No menu bindings</i>" :
                        "<i><color=grey>No menu bindings available</color></i>"
                    ));
                    continue;
                }

                _menuBindingSetDropdown.options.Add(new(bindingSet.Name));
            }

            _menuBindingSetDropdown.interactable = MenuBindingSetsExist;

            _menuBindingSetDropdown.SetValueWithoutNotify(_menuBindingSetsByIndex.IndexOf(player.DeviceInfo.GetActiveMenuBindingsForController(Controller)));
            UpdateMenuBindingsStatus();
        }

        public void ChangeGameplayBindingSet()
        {
            var player = PlayerContainer.GetPlayerFromProfile(Profile);
            player.DeviceInfo.SetGameplayBindingsForController(Controller, _gameplayBindingSetsByIndex[_gameplayBindingSetDropdown.value]);
            UpdateGameplayBindingsStatus();
        }

        public void ChangeMenuBindingSet()
        {
            var player = PlayerContainer.GetPlayerFromProfile(Profile);
            player.DeviceInfo.SetMenuBindingsForController(Controller, _menuBindingSetsByIndex[_menuBindingSetDropdown.value]);
            UpdateMenuBindingsStatus();
        }

        private void UpdateGameplayBindingsStatus()
        {
            bool playsThisMode = _gameplayBindingSetDropdown.value is not 0;
            _gameplayIndicator.SetLit(playsThisMode);

            // A profile keeps all of its controllers in every game mode; say which ones the mode leaves idle
            _name.text = playsThisMode
                ? DeviceIdentity.GetName(Controller)
                : Localize.KeyFormat("Menu.ProfileList.ControllerMenusOnly", DeviceIdentity.GetName(Controller),
                    Profile.GameMode.ToLocalizedName());
        }

        private void UpdateMenuBindingsStatus()
        {
            _menuIndicator.SetLit(_menuBindingSetDropdown.value is not 0);
        }

        // Lights the name while a button is held on the controller, to tell which one this is
        private static readonly Color _pressedColor = new(0.18f, 0.85f, 1f);
        private Color? _nameColor;

        private void Update()
        {
            if (Controller is null)
            {
                return;
            }

            _nameColor ??= _name.color;
            _name.color = DeviceIdentity.IsAnyButtonPressed(Controller) ? _pressedColor : _nameColor.Value;
        }

        public void ToggleDrawer()
        {
            SetDrawerOpen(!DrawerOpen);
        }

        private void SetDrawerOpen(bool open)
        {
            _drawer.SetActive(open);
            _arrow.localScale = _arrow.localScale.WithY(open ? -1f : 1f);
            _layoutElement.preferredHeight = open ? 240 : 60;
        }

        public void JumpToGameplayBindingSet()
        {
            if (GameplayBindingSet is null)
            {
                _centerPane.ProfileMenu.JumpToBindingSetCategory(Controller.GetFamily());
            }
            else
            {
                _centerPane.ProfileMenu.JumpToBindingSet(GameplayBindingSet, Controller, Profile.LeftyFlip);
            }
        }

        public void JumpToMenuBindingSet()
        {
            if (MenuBindingSet is null)
            {
                _centerPane.ProfileMenu.JumpToBindingSetCategory(Controller.GetFamily());
            }
            else
            {
                _centerPane.ProfileMenu.JumpToBindingSet(MenuBindingSet, Controller, Profile.LeftyFlip);
            }
        }
    }
}
