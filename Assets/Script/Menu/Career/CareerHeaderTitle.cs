using System;
using TMPro;
using UnityEngine;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The title in a career screen's header bar ("Career - Band Selection", "Career - Foo", ...).
    /// The header is a shared prefab with no script of its own, so its title is found by the text the
    /// career prefabs give it.
    /// </summary>
    public class CareerHeaderTitle
    {
        private const string PREFAB_TEXT = "Career";

        private TextMeshProUGUI _text;

        public CareerHeaderTitle(Component menu)
        {
            foreach (var text in menu.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (string.Equals(text.text?.Trim(), PREFAB_TEXT, StringComparison.OrdinalIgnoreCase))
                {
                    _text = text;
                    break;
                }
            }
        }

        public void ShowBandSelection() => Set(Localize.Key("Menu.Career.TitleBands"));

        public void ShowName(string name)
        {
            Set(string.IsNullOrWhiteSpace(name) ? PREFAB_TEXT : Localize.KeyFormat("Menu.Career.TitleNamed", name));
        }

        private void Set(string title)
        {
            if (_text != null)
            {
                _text.text = title;
            }
        }
    }
}
