#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using YARG.Career;
using YARG.Core;
using YARG.Core.Game;
using YARG.Menu;
using YARG.Menu.Career;
using YARG.Menu.Dialogs;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Scores;

namespace YARG.Dev
{
    /// <summary>
    /// Band, player and dialog commands. Values with spaces go in double quotes; lists are comma
    /// separated. A band or profile is named by its name (case-insensitive) or id.
    ///
    ///   band list                         Every band: id, name, hometown, members, fans; "*" = active.
    ///   band create name=.. [hometown=..] [members=a,b] [select=false]
    ///                                     Create a band (members default to the current human players) and
    ///                                     make it active unless select=false.
    ///   band select &lt;band|none&gt;           Set the active band (none: play as the current players).
    ///   band rename &lt;band&gt; to=..          Rename.
    ///   band members &lt;band&gt; members=a,b   Set the members.
    ///   band delete &lt;band&gt;               Soft-delete (clears it if active).
    ///   band adopt &lt;band&gt; career=..       Hand the current players' profile-set save of that career to the band.
    ///   band edit &lt;band&gt; [hometown=..] [genre=..]   Change hometown and/or genre (empty value clears).
    ///   band sheet [&lt;band&gt;] [open=true]     Print the band sheet (raw numbers, then the lines the sheet
    ///                                     shows); open=true also opens the sheet dialog. Default: active band.
    ///   band history [&lt;band&gt;] [open=true]   The same for the gig history (newest first).
    ///
    ///   profile create name=.. [player=true] [bot=true] [instrument=..] [difficulty=..]
    ///                                     Add a profile (and, with player=true, a player using it); bot=true
    ///                                     makes a bot profile. instrument/difficulty: what the instrument
    ///                                     select picks first (e.g. instrument=FiveFretGuitar difficulty=Expert).
    ///   profile set &lt;profile&gt; [instrument=..] [difficulty=..]   The same for an existing profile.
    ///   player add &lt;profile&gt;                Add a player for an existing profile (no input devices).
    ///   player remove &lt;profile&gt;             Remove that profile's player.
    ///   player sitout &lt;profile&gt; &lt;true|false&gt;
    ///   players                             List players: name, bot, sitting out.
    ///
    /// Driving the stock dialogs (the real band UI) without a keyboard or mouse:
    ///   dialog                Describe the open dialog: title, input text, buttons.
    ///   type &lt;text&gt;           Set the open dialog's text field (rename dialog, confirm-delete dialog).
    ///   press &lt;label&gt;         Click the open dialog's button whose label matches (exactly, else the one
    ///                         label containing it), list buttons included.
    /// </summary>
    public partial class DevRemote
    {
        private static string BandCommand(string argument)
        {
            var careers = ScoreContainer.Careers;
            if (careers is null)
            {
                return "error the score database is not initialized";
            }

            var words = SplitWords(argument);
            if (words.Count == 0)
            {
                return "error usage: band <list|create|select|rename|edit|members|delete|adopt|sheet|history> ...";
            }

            var sub = words[0].ToLowerInvariant();
            var positional = words.Skip(1).Where(word => !word.Contains('=')).ToList();
            var options = words.Skip(1).Where(word => word.Contains('='))
                .ToDictionary(word => word.Substring(0, word.IndexOf('=')).ToLowerInvariant(),
                    word => word.Substring(word.IndexOf('=') + 1));

            CareerBand band = null;
            if (sub is "sheet" or "history" && positional.Count == 0)
            {
                band = CareerBands.ActiveBand(careers);
                if (band is null)
                {
                    return $"error no active band; usage: band {sub} [<band>] [open=true]";
                }
            }
            else if (sub is "select" or "rename" or "members" or "delete" or "adopt" or "edit" or "sheet" or "history")
            {
                if (positional.Count == 0)
                {
                    return $"error usage: band {sub} <band> ...";
                }

                if (sub == "select" && positional[0].Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    CareerBands.ActiveBandId = null;
                    RefreshCareerScreens();
                    return "ok active band: none";
                }

                band = FindBand(careers, positional[0]);
                if (band is null)
                {
                    return $"error no band '{positional[0]}'; bands: {string.Join(", ", careers.GetBands().Select(b => $"'{b.Name}'"))}";
                }
            }

            switch (sub)
            {
                case "list":
                    return $"ok {DescribeBands(careers)}";

                case "create":
                {
                    if (!options.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
                    {
                        return "error band create needs name=";
                    }

                    List<Guid> members;
                    if (options.TryGetValue("members", out var memberText))
                    {
                        if (!TryFindProfiles(memberText, out members, out var error))
                        {
                            return $"error {error}";
                        }
                    }
                    else
                    {
                        members = CareerBands.CurrentHumanPlayers();
                    }

                    var created = careers.CreateBand(name, options.GetValueOrDefault("hometown"), members);
                    if (!options.TryGetValue("select", out var select) || select != "false")
                    {
                        CareerBands.ActiveBandId = created.Id;
                    }

                    RefreshCareerScreens();
                    return $"ok created {DescribeBand(careers, created)}";
                }

                case "select":
                    CareerBands.ActiveBandId = band.Id;
                    RefreshCareerScreens();
                    return $"ok active band: {DescribeBand(careers, band)}";

                case "edit":
                    if (!options.ContainsKey("hometown"))
                    {
                        return "error band edit needs hometown= (empty to clear)";
                    }

                    if (options.TryGetValue("hometown", out var hometown))
                    {
                        careers.SetBandHometown(band.Id, hometown.Trim());
                    }

                    RefreshCareerScreens();
                    return $"ok {DescribeBand(careers, careers.GetBand(band.Id))}";

                case "sheet":
                case "history":
                {
                    var open = options.TryGetValue("open", out var openText) && openText == "true";
                    if (open)
                    {
                        if (DialogManager.Instance.IsDialogShowing)
                        {
                            return "error a dialog is already open";
                        }

                        if (sub == "sheet")
                        {
                            CareerBandSheetDialogs.ShowSheet(band);
                        }
                        else
                        {
                            CareerBandSheetDialogs.ShowHistory(band);
                        }
                    }

                    var sheet = CareerBandSheet.Load(careers, band.Id, CareerBands.AllCareers());
                    var lines = sub == "sheet"
                        ? CareerBandSheetDialogs.SheetLines(careers, band, sheet)
                        : CareerBandSheetDialogs.HistoryLines(band, sheet);
                    var stats = sub == "sheet" ? DescribeSheet(sheet) + " || " : string.Empty;
                    return $"ok {(open ? "opened, " : "")}{stats}" +
                        string.Join(" || ", lines.Select(line => Plain(line).Replace("\n", " / ")));
                }

                case "rename":
                    if (!options.TryGetValue("to", out var newName) || string.IsNullOrWhiteSpace(newName))
                    {
                        return "error band rename needs to=";
                    }

                    careers.RenameBand(band.Id, newName);
                    RefreshCareerScreens();
                    return $"ok {DescribeBand(careers, careers.GetBand(band.Id))}";

                case "members":
                {
                    if (!options.TryGetValue("members", out var memberText) ||
                        !TryFindProfiles(memberText, out var members, out var error))
                    {
                        return "error band members needs members=<profile,profile>";
                    }

                    var current = careers.GetBandMembers(band.Id);
                    foreach (var added in members.Except(current))
                    {
                        careers.AddBandMember(band.Id, added);
                    }

                    foreach (var removed in current.Except(members))
                    {
                        careers.RemoveBandMember(band.Id, removed);
                    }

                    RefreshCareerScreens();
                    return $"ok {DescribeBand(careers, band)}";
                }

                case "delete":
                    careers.SoftDeleteBand(band.Id);
                    if (CareerBands.ActiveBandId == band.Id)
                    {
                        CareerBands.ActiveBandId = null;
                    }

                    RefreshCareerScreens();
                    return $"ok deleted '{band.Name}'";

                case "adopt":
                {
                    if (!options.TryGetValue("career", out var reference) ||
                        !TryFindCareer(reference, out var career, out var error))
                    {
                        return "error band adopt needs career=";
                    }

                    var save = careers.FindActiveSaveForProfiles(career.Id, CareerBands.CurrentHumanPlayers());
                    if (save is null)
                    {
                        return "error the current players have no profile-set save for that career";
                    }

                    return careers.AdoptSave(save.Id, band.Id)
                        ? $"ok save {save.Id} now belongs to '{band.Name}'"
                        : "error not adopted (the band already has a save for that career?)";
                }

                default:
                    return $"error unknown band command '{sub}'";
            }
        }

        private static string ProfileCommand(string argument)
        {
            var words = SplitWords(argument);
            if (words.Count == 0 || words[0] is not ("create" or "set"))
            {
                return "error usage: profile create name=.. [player=true] [bot=true] [instrument=..] [difficulty=..] | " +
                    "profile set <profile> [instrument=..] [difficulty=..]";
            }

            var options = words.Skip(1).Where(word => word.Contains('='))
                .ToDictionary(word => word.Substring(0, word.IndexOf('=')).ToLowerInvariant(),
                    word => word.Substring(word.IndexOf('=') + 1));

            YargProfile profile;
            if (words[0] == "set")
            {
                profile = words.Count > 1 ? FindProfile(words[1]) : null;
                if (profile is null)
                {
                    return $"error no profile '{(words.Count > 1 ? words[1] : "")}'";
                }
            }
            else
            {
                if (!options.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
                {
                    return "error profile create needs name=";
                }

                // As the profile list's "Add Bot" makes them.
                profile = new YargProfile { Name = name };
                if (options.TryGetValue("bot", out var bot) && bot == "true")
                {
                    profile.IsBot = true;
                    profile.NoteSpeed = 5;
                    profile.HighwayLength = 1;
                    profile.GameMode = GameMode.FiveFretGuitar;
                }
            }

            // What the instrument/difficulty select picks first (PreferredInstrument, DifficultyFallback).
            if (options.TryGetValue("instrument", out var instrumentText))
            {
                if (!Enum.TryParse<Instrument>(instrumentText, true, out var instrument))
                {
                    return $"error unknown instrument '{instrumentText}'";
                }

                profile.GameMode = instrument.ToNativeGameMode();
                profile.PreferredInstrument = instrument;
                profile.CurrentInstrument = instrument;
            }

            if (options.TryGetValue("difficulty", out var difficultyText))
            {
                if (!Enum.TryParse<Difficulty>(difficultyText, true, out var difficulty))
                {
                    return $"error unknown difficulty '{difficultyText}'";
                }

                profile.DifficultyFallback = difficulty;
                profile.CurrentDifficulty = difficulty;
            }

            if (words[0] == "create")
            {
                if (!PlayerContainer.AddProfile(profile))
                {
                    return "error could not add the profile";
                }

                if (options.TryGetValue("player", out var player) && player == "true")
                {
                    PlayerContainer.CreatePlayerFromProfile(profile, false);
                }
            }

            RefreshCareerScreens();
            return $"ok profile '{profile.Name}' {profile.Id}{(profile.IsBot ? " bot" : "")} " +
                $"{profile.GameMode}/{profile.PreferredInstrument}/{profile.DifficultyFallback} | {DescribePlayers()}";
        }

        private static string PlayerCommand(string argument)
        {
            var words = SplitWords(argument);
            if (words.Count < 2)
            {
                return "error usage: player <add|remove|sitout> <profile> [true|false]";
            }

            var profile = FindProfile(words[1]);
            if (profile is null)
            {
                return $"error no profile '{words[1]}'";
            }

            var existing = PlayerContainer.GetPlayerFromProfile(profile);
            switch (words[0].ToLowerInvariant())
            {
                case "add":
                    if (existing is null && PlayerContainer.CreatePlayerFromProfile(profile, false) is null)
                    {
                        return "error could not create the player";
                    }
                    break;
                case "remove":
                    if (existing is not null)
                    {
                        PlayerContainer.DisposePlayer(existing);
                    }
                    break;
                case "sitout":
                    if (existing is null || words.Count < 3 || !bool.TryParse(words[2], out var sittingOut))
                    {
                        return "error usage: player sitout <profile with a player> <true|false>";
                    }

                    existing.SittingOut = sittingOut;
                    break;
                default:
                    return $"error unknown player command '{words[0]}'";
            }

            RefreshCareerScreens();
            return $"ok {DescribePlayers()}";
        }

        private static string DescribePlayers()
        {
            return "players: " + string.Join(", ", PlayerContainer.Players.Select(player =>
                $"'{player.Profile.Name}'{(player.Profile.IsBot ? " bot" : "")}{(player.SittingOut ? " sitting-out" : "")}"));
        }

        /// <summary>
        /// "band='Name'" (with "no-member-playing" if the players would not earn it credit) or "band=none".
        /// </summary>
        private static string DescribeOwner()
        {
            var careers = ScoreContainer.Careers;
            var band = CareerBands.ActiveBand(careers);
            if (band is null)
            {
                return "band=none";
            }

            var credit = CareerBands.CurrentCredit(careers);
            return credit.Counts ? $"band='{band.Name}'" : $"band='{band.Name}'({credit.Block})";
        }

        private static string DescribeBands(CareerDatabase careers)
        {
            var bands = careers.GetBands();
            return bands.Count == 0
                ? "no bands"
                : string.Join(" | ", bands.Select(band => DescribeBand(careers, band)));
        }

        private static string DescribeBand(CareerDatabase careers, CareerBand band)
        {
            var active = band.Id == CareerBands.ActiveBandId ? "*" : "";
            var saves = careers.GetActiveSavesForBand(band.Id);
            return $"{active}'{band.Name}' id={band.Id} hometown='{band.Hometown}' " +
                $"members=[{string.Join(",", CareerBands.MemberNames(careers, band.Id))}] " +
                $"saves=[{string.Join(",", saves.Select(save => save.Id))}] " +
                $"fans={CareerBands.BandFans(careers, band.Id, CareerBands.AllCareers())}";
        }

        /// <summary>
        /// The sheet's raw numbers, for checking against <c>career</c> and the database.
        /// </summary>
        private static string DescribeSheet(CareerBandSheet sheet)
        {
            var next = sheet.NextRank is { } rank ? $"{rank.rank}(+{rank.fansNeeded})" : "top";
            var members = sheet.Members.Select(member =>
                $"{PlayerContainer.GetProfileById(member.ProfileId)?.Name ?? member.ProfileId.ToString()}:" +
                $"gigs={member.Gigs},instruments=[{string.Join(",", member.Instruments.Select(i => $"{i.instrument}x{i.gigs}"))}]," +
                $"accuracy={(member.AverageAccuracy is { } accuracy ? (accuracy * 100f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "none")}," +
                $"fc={member.FullCombos}");
            var gigs = sheet.History.Select(gig =>
                $"#{gig.Row.Id}:{gig.Career.Name}/t{gig.Row.TierIndex}s{gig.Row.SongIndex}:" +
                $"{(gig.Gold ? "G" : gig.Stars.ToString())}:{(gig.UsesFans ? gig.Fans + (gig.Counts ? "*" : "") : "-")}:" +
                $"{(gig.ContractType is { } type ? type + (gig.ContractMet ? "+" : "x") : "none")}:rec={gig.Row.GameRecordId}");
            return $"fans={sheet.Fans} rank={sheet.Rank} next={next} careers={sheet.CareersPlayed} " +
                $"gigs={sheet.GigsPlayed} stars={sheet.TotalStars} contracts={sheet.ContractsMet}/{sheet.ContractGigs} " +
                $"members=[{string.Join(" ", members)}] history=[{string.Join(" ", gigs)}]";
        }

        private static CareerBand FindBand(CareerDatabase careers, string reference)
        {
            var bands = careers.GetBands();
            return Guid.TryParse(reference, out var id)
                ? bands.FirstOrDefault(band => band.Id == id)
                : bands.FirstOrDefault(band => string.Equals(band.Name, reference, StringComparison.OrdinalIgnoreCase));
        }

        private static YargProfile FindProfile(string reference)
        {
            return Guid.TryParse(reference, out var id)
                ? PlayerContainer.GetProfileById(id)
                : PlayerContainer.Profiles.FirstOrDefault(profile =>
                    string.Equals(profile.Name, reference, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryFindProfiles(string list, out List<Guid> ids, out string error)
        {
            ids = new List<Guid>();
            foreach (var reference in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var profile = FindProfile(reference.Trim());
                if (profile is null)
                {
                    error = $"no profile '{reference}'; profiles: " +
                        string.Join(", ", PlayerContainer.Profiles.Select(p => $"'{p.Name}'"));
                    return false;
                }

                ids.Add(profile.Id);
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Redraw whichever career screen is open after a band or player change.
        /// </summary>
        private static void RefreshCareerScreens()
        {
            var list = FindAnyObjectByType<CareerListMenu>();
            if (list != null && list.isActiveAndEnabled)
            {
                list.Refresh();
            }

            OpenCareerMenu()?.RefreshProgress();
        }

        #region Dialogs

        private static Dialog OpenDialog()
        {
            return FindObjectsByType<Dialog>(UnityEngine.FindObjectsSortMode.None)
                .FirstOrDefault(dialog => dialog.IsOpen);
        }

        private static string DescribeDialog()
        {
            var dialog = OpenDialog();
            if (dialog == null)
            {
                return "ok no dialog";
            }

            var text = new StringBuilder($"ok {dialog.GetType().Name} title='{Plain(dialog.Title.text)}'");
            foreach (var input in dialog.GetComponentsInChildren<TMP_InputField>())
            {
                text.Append($" input='{input.text}'");
            }

            text.Append(" buttons=[");
            text.Append(string.Join(" | ", DialogButtons(dialog).Select(button => Plain(button.Text.text))));
            text.Append(']');
            return text.ToString();
        }

        private static string TypeText(string argument)
        {
            var dialog = OpenDialog();
            var input = dialog != null ? dialog.GetComponentInChildren<TMP_InputField>() : null;
            if (input == null)
            {
                return "error no open dialog with a text field";
            }

            input.text = argument.Trim('"');
            return $"ok typed '{input.text}'";
        }

        private static string PressButton(string argument)
        {
            var dialog = OpenDialog();
            if (dialog == null)
            {
                return "error no dialog";
            }

            var label = argument.Trim('"');
            var buttons = DialogButtons(dialog).ToList();
            var matches = buttons.Where(button =>
                string.Equals(Plain(button.Text.text), label, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                matches = buttons.Where(button =>
                    Plain(button.Text.text).IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            if (matches.Count != 1)
            {
                return $"error {matches.Count} buttons match '{label}'; buttons: " +
                    string.Join(" | ", buttons.Select(button => $"'{Plain(button.Text.text)}'"));
            }

            var pressed = Plain(matches[0].Text.text);
            matches[0].OnClick.Invoke();
            return $"ok pressed '{pressed}'";
        }

        private static IEnumerable<ColoredButton> DialogButtons(Dialog dialog)
        {
            return dialog.GetComponentsInChildren<ColoredButton>()
                .Where(button => button.isActiveAndEnabled && button.Text != null);
        }

        private static string Plain(string text)
        {
            return Regex.Replace(text ?? string.Empty, "<[^>]+>", string.Empty).Trim();
        }

        #endregion
    }
}
#endif
