#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Career;
using YARG.Menu.Settings;
using YARG.Menu.Settings.Visuals;
using YARG.Settings;
using YARG.Settings.Customization;
using YARG.Settings.Metadata;

namespace YARG.Dev
{
    /// <summary>
    /// Career content and preset editor commands.
    ///
    ///   songs [career=...]         Every song of the career: "t0s1 Artist - Title", or "MISSING" with the
    ///                              song id when the library does not have it; then how many resolved.
    ///   validate [career=...]      The career's content warnings (CareerValidation), one per line part.
    ///   editor open [career=...]   Open Settings > Presets > Career Presets with that career selected.
    ///   editor copy name=&lt;name&gt;   Copy the selected career like the Copy button, then rename the copy.
    ///   editor tier &lt;n&gt;            Select tier n's row (from 0), which shows its editor.
    ///   editor song &lt;tier&gt; &lt;song&gt;  Select a song's row, which shows its editor.
    ///   editor rows                The tier and song summary rows, as shown.
    ///   editor fields              The editor's fields: label = value, (hidden), (read-only).
    ///   editor set "&lt;label&gt;" &lt;value&gt;
    ///                              Change a field through its control, as a player would: types into a
    ///                              text field and ends the edit, picks a dropdown option by its text, or
    ///                              sets a toggle (true/false). No value: an empty text field.
    ///   editor scroll &lt;0..1&gt;       Scroll the field editor: 1 = top, 0 = bottom.
    ///   editor close               Close the settings menu.
    /// </summary>
    public partial class DevRemote
    {
        private static readonly FieldInfo _careerHeaderText = typeof(CareerHeader).GetField("_text",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static string DescribeSongs(string argument)
        {
            if (!TryCareerArgument(argument, out var career, out var error))
            {
                return $"error {error}";
            }

            var text = new StringBuilder($"ok career='{career.Name}'");
            int total = 0, found = 0;
            for (var t = 0; t < career.Tiers.Count; t++)
            {
                var songs = career.Tiers[t].Songs;
                for (var s = 0; s < songs.Length; s++)
                {
                    var song = songs[s];
                    if (song.SongEntry is null)
                    {
                        song.RefreshSongEntry();
                    }

                    total++;
                    if (song.SongEntry is { } entry)
                    {
                        found++;
                        text.Append($" | t{t}s{s} {entry.Artist.Original} - {entry.Name.Original}");
                    }
                    else
                    {
                        text.Append($" | t{t}s{s} MISSING {song.Identifier} {song.SongId?.ToString() ?? song.ShortName}");
                    }
                }
            }

            text.Append($" | resolved {found}/{total}");
            return text.ToString();
        }

        private static string ValidateCareer(string argument)
        {
            if (!TryCareerArgument(argument, out var career, out var error))
            {
                return $"error {error}";
            }

            var issues = CareerValidation.Validate(career);
            return issues.Count == 0
                ? $"ok career='{career.Name}' no warnings"
                : $"ok career='{career.Name}' {issues.Count} warning(s): " +
                    string.Join(" | ", issues.Select(issue => issue.Describe(career)));
        }

        private static bool TryCareerArgument(string argument, out CareerPreset career, out string error)
        {
            career = null;
            if (!TryParseOptions(argument, false, out var options, out error))
            {
                return false;
            }

            if (options.TryGetValue("career", out var reference))
            {
                return TryFindCareer(reference, out career, out error);
            }

            career = OpenCareerMenu()?.Career;
            error = career is null ? "no career= given and the career menu is not open" : null;
            return career is not null;
        }

        private static string EditorCommand(string argument)
        {
            var words = SplitWords(argument);
            if (words.Count == 0)
            {
                return "error usage: editor open|copy|tier|song|rows|fields|set|close";
            }

            var settings = SettingsMenu.Instance;
            var tab = SettingsManager.GetTabByName("Presets") as PresetsTab;
            if (settings == null || tab is null)
            {
                return "error the settings menu is not available";
            }

            var rest = string.Join(" ", words.Skip(1).Select(word => word.Contains(' ') ? $"\"{word}\"" : word));
            switch (words[0].ToLowerInvariant())
            {
                case "open":
                {
                    CareerPreset career = CustomContentManager.Careers.DefaultPresets[0];
                    if (rest.Length > 0 && !TryCareerArgument(rest, out career, out var error))
                    {
                        return $"error {error}";
                    }

                    settings.gameObject.SetActive(true);
                    tab.SelectedContent = CustomContentManager.Careers;
                    tab.SelectedPreset = career;
                    settings.SelectTabByName("Presets");
                    settings.Refresh();
                    return $"ok editing '{career.Name}'{(career.DefaultPreset ? " (default, read-only)" : "")}";
                }
                case "copy":
                {
                    if (tab.SelectedPreset is not CareerPreset original)
                    {
                        return "error no career selected (editor open first)";
                    }

                    var actions = UnityEngine.Object.FindAnyObjectByType<YARG.Menu.Settings.PresetActions>();
                    if (actions == null)
                    {
                        return "error the preset buttons are not shown";
                    }

                    actions.CopyPreset();
                    var copy = tab.SelectedPreset;
                    if (words.Skip(1).FirstOrDefault(word => word.StartsWith("name=")) is { } nameWord)
                    {
                        // What the rename dialog does on confirm (PresetActions.RenamePreset).
                        tab.SelectedContent.RenamePreset(copy, nameWord.Substring("name=".Length));
                        settings.Refresh();
                    }

                    return $"ok copied '{original.Name}' to '{copy.Name}' ({copy.Id}) at {copy.Path}";
                }
                case "tier":
                case "song":
                {
                    if (tab.SelectedPreset is not CareerPreset career)
                    {
                        return "error no career selected (editor open first)";
                    }

                    object row;
                    if (words[0].Equals("tier", StringComparison.OrdinalIgnoreCase))
                    {
                        if (words.Count < 2 || !int.TryParse(words[1], out var t) || t < 0 || t >= career.Tiers.Count)
                        {
                            return $"error tier must be 0..{career.Tiers.Count - 1}";
                        }

                        row = career.Tiers[t];
                    }
                    else
                    {
                        if (words.Count < 3 || !int.TryParse(words[1], out var t) || t < 0 || t >= career.Tiers.Count ||
                            !int.TryParse(words[2], out var s) || s < 0 || s >= career.Tiers[t].Songs.Length)
                        {
                            return "error usage: editor song <tier> <song>";
                        }

                        row = career.Tiers[t].Songs[s];
                    }

                    settings.RefreshSettingsAndSelectCareerRow(row);
                    var header = UnityEngine.Object.FindObjectsByType<CareerHeader>(FindObjectsSortMode.None)
                        .FirstOrDefault(h => ReferenceEquals(h.RowIdentity, row));
                    return header == null ? "error that row is not shown" : $"ok selected {RowText(header)}";
                }
                case "rows":
                {
                    var rows = settings.GetComponentsInChildren<CareerHeader>()
                        .Where(header => header.RowIdentity is CareerTier or CareerSong)
                        .Select(RowText);
                    return $"ok {string.Join(" | ", rows)}";
                }
                case "fields":
                {
                    var fields = PreviewVisuals(settings).Select(visual =>
                    {
                        var text = $"{visual.SettingLabel.text} = {VisualValue(visual)}";
                        if (!visual.gameObject.activeSelf)
                        {
                            text += " (hidden)";
                        }

                        if (!visual.IsEditable)
                        {
                            text += " (read-only)";
                        }

                        return text;
                    });
                    return $"ok {string.Join(" | ", fields)}";
                }
                case "set":
                {
                    if (words.Count < 2)
                    {
                        return "error usage: editor set \"<label>\" [<value>]";
                    }

                    var label = words[1];
                    var value = string.Join(" ", words.Skip(2));
                    var visual = PreviewVisuals(settings)
                        .Where(v => v.gameObject.activeSelf)
                        .FirstOrDefault(v => string.Equals(v.SettingLabel.text, label, StringComparison.OrdinalIgnoreCase));
                    if (visual == null)
                    {
                        return $"error no shown field '{label}'";
                    }

                    if (!visual.IsEditable)
                    {
                        return $"error '{label}' is read-only";
                    }

                    var dropdown = visual.GetComponentInChildren<TMP_Dropdown>(true);
                    if (dropdown != null)
                    {
                        var index = dropdown.options.FindIndex(option =>
                            string.Equals(option.text, value, StringComparison.OrdinalIgnoreCase));
                        if (index < 0)
                        {
                            return $"error '{label}' has no option '{value}'; options: " +
                                string.Join(", ", dropdown.options.Select(option => option.text));
                        }

                        dropdown.value = index;
                        return $"ok {label} = {value}";
                    }

                    var input = visual.GetComponentInChildren<TMP_InputField>(true);
                    if (input != null)
                    {
                        input.text = value;
                        input.onEndEdit.Invoke(value);
                        return $"ok {label} = {value} (shows '{input.text}')";
                    }

                    var toggle = visual.GetComponentInChildren<Toggle>(true);
                    if (toggle != null && bool.TryParse(value, out var on))
                    {
                        toggle.isOn = on;
                        return $"ok {label} = {on}";
                    }

                    return $"error do not know how to set '{label}'";
                }
                case "scroll":
                {
                    var scroll = settings.PreviewContainerUI == null
                        ? null
                        : settings.PreviewContainerUI.GetComponentInChildren<ScrollRect>();
                    if (scroll == null || words.Count < 2 || !float.TryParse(words[1],
                            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                            out var position))
                    {
                        return scroll == null ? "error no editor shown" : "error usage: editor scroll <0..1>";
                    }

                    // 1 = top, 0 = bottom (ScrollRect.verticalNormalizedPosition).
                    scroll.verticalNormalizedPosition = Mathf.Clamp01(position);
                    return "ok";
                }
                case "close":
                    settings.gameObject.SetActive(false);
                    return "ok";
                default:
                    return $"error unknown editor command '{words[0]}'";
            }
        }

        private static BaseSettingVisual[] PreviewVisuals(SettingsMenu settings)
        {
            return settings.PreviewContainerUI == null
                ? Array.Empty<BaseSettingVisual>()
                : settings.PreviewContainerUI.GetComponentsInChildren<BaseSettingVisual>(true);
        }

        private static string VisualValue(BaseSettingVisual visual)
        {
            var dropdown = visual.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown != null)
            {
                return dropdown.options.Count > dropdown.value && dropdown.value >= 0
                    ? dropdown.options[dropdown.value].text
                    : dropdown.captionText != null ? dropdown.captionText.text : "?";
            }

            var input = visual.GetComponentInChildren<TMP_InputField>(true);
            if (input != null)
            {
                return $"'{input.text}'";
            }

            var toggle = visual.GetComponentInChildren<Toggle>(true);
            return toggle != null ? toggle.isOn.ToString() : "?";
        }

        private static string RowText(CareerHeader header)
        {
            var text = _careerHeaderText?.GetValue(header) as TextMeshProUGUI;
            return text != null ? text.text.Trim() : header.DetailsTitle;
        }
    }
}
#endif
