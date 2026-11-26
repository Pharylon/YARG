#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Career;
using YARG.Menu.DifficultySelect;
using YARG.Player;
using YARG.Playlists;

namespace YARG.Dev
{
    /// <summary>
    /// Commands for playing songs unattended (with a bot):
    ///
    ///   career-bots [on|off]   The dev-only switch that lets bots earn career credit (see
    ///                          <see cref="CareerBotCredit"/>; also the launch argument -career-bots-count).
    ///                          No argument: report it.
    ///   ready [speed=&lt;percent&gt;]
    ///                          On the instrument/difficulty select: set the song speed field, then press
    ///                          Ready for every player in turn (each with the instrument and difficulty the
    ///                          menu picked for them; see "profile set ... instrument= difficulty=").
    ///   waitfor scene=&lt;name&gt; [timeout=&lt;seconds&gt;]
    ///                          Wait until the active scene's name contains the text (e.g. Score, Menu,
    ///                          Gameplay) and no loading screen is up; default timeout 300 s. Reports the
    ///                          time waited, or an error on timeout. Raise YARG_REMOTE_TIMEOUT for the
    ///                          shell side (yarg-remote.sh) when waiting long.
    ///   show                   Report the setlist (Setlist owner) and the career show, if any.
    ///   quitsong               In a song: what the pause menu's Quit does (GameManager.ForceQuitSong).
    ///   library greenhold      In the music library: what holding Green does (start the setlist if it has
    ///                          songs, else add the selected song to it). Yellow (nav Yellow) adds a song.
    ///   modifiers              Every player's modifiers: in effect now / saved with the profile.
    /// </summary>
    public partial class DevRemote
    {
        private static readonly FieldInfo _speedInputField = typeof(DifficultySelectMenu).GetField("_speedInput",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo _changePlayer = typeof(DifficultySelectMenu).GetMethod("ChangePlayer",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static string CareerBotsCommand(string argument)
        {
            switch (argument.Trim().ToLowerInvariant())
            {
                case "on":
                case "true":
                    CareerBotCredit.Enabled = true;
                    break;
                case "off":
                case "false":
                    CareerBotCredit.Enabled = false;
                    break;
                case "":
                    break;
                default:
                    return "error usage: career-bots [on|off]";
            }

            RefreshCareerScreens();
            return $"ok career-bots={(CareerBotCredit.Enabled ? "on" : "off")}";
        }

        private static string Ready(string argument)
        {
            var menu = FindAnyObjectByType<DifficultySelectMenu>();
            if (menu == null || !menu.isActiveAndEnabled)
            {
                return "error the instrument/difficulty select is not open";
            }

            if (!TryParseOptions(argument, out var options, out var error, "speed"))
            {
                return $"error {error}";
            }

            if (options.TryGetValue("speed", out var speedText))
            {
                if (!int.TryParse(speedText.TrimEnd('%'), out var speed) || speed < 10 || speed > 5000)
                {
                    return "error speed must be a percent, 10..5000";
                }

                if (_speedInputField?.GetValue(menu) is not TMP_InputField input)
                {
                    return "error cannot find the speed field";
                }

                input.SetTextWithoutNotify($"{speed}%");
            }

            if (_changePlayer is null)
            {
                return "error cannot find ChangePlayer";
            }

            // One Ready per player; the menu itself skips players that must sit out and starts the song
            // after the last one.
            var pressed = 0;
            for (var i = 0; i < PlayerContainer.Players.Count && menu.isActiveAndEnabled; i++)
            {
                _changePlayer.Invoke(menu, new object[] { 1 });
                pressed++;
            }

            return $"ok ready x{pressed} speed={GlobalVariables.State.SongSpeed:0.##} " +
                $"song='{GlobalVariables.State.CurrentSong?.Name}' | {DescribePlayers()}";
        }

        private static IEnumerator WaitFor(string argument, Action<string> setResult)
        {
            if (!TryParseOptions(argument, out var options, out var error, "scene", "timeout") ||
                !options.TryGetValue("scene", out var scene))
            {
                setResult($"error {error ?? "usage: waitfor scene=<name> [timeout=<seconds>]"}");
                yield break;
            }

            var timeout = 300f;
            if (options.TryGetValue("timeout", out var timeoutText) && !float.TryParse(timeoutText,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out timeout))
            {
                setResult("error timeout must be a number of seconds");
                yield break;
            }

            var start = Time.realtimeSinceStartup;
            while (true)
            {
                var name = SceneManager.GetActiveScene().name;
                if (name.IndexOf(scene, StringComparison.OrdinalIgnoreCase) >= 0 && !LoadingScreen.IsActive)
                {
                    setResult($"ok scene={name} after {Time.realtimeSinceStartup - start:0.0}s");
                    yield break;
                }

                if (Time.realtimeSinceStartup - start > timeout)
                {
                    setResult($"error timed out after {timeout:0}s in scene={name}");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private static string QuitSong()
        {
            var manager = FindAnyObjectByType<YARG.Gameplay.GameManager>();
            if (manager == null)
            {
                return "error not in a song";
            }

            manager.ForceQuitSong();
            return "ok quit";
        }

        private static string LibraryCommand(string argument)
        {
            var menu = FindAnyObjectByType<YARG.Menu.MusicLibrary.MusicLibraryMenu>();
            if (menu == null || !menu.isActiveAndEnabled)
            {
                return "error the music library is not open";
            }

            if (argument.Trim().ToLowerInvariant() != "greenhold")
            {
                return "error usage: library greenhold";
            }

            menu.ExecuteGreenHoldAction();
            return $"ok | setlist=[{string.Join(", ", Setlist.ResolveSongs().Select(song => $"'{song.Name}'"))}]";
        }

        private static readonly FieldInfo _savedModifiersField = typeof(YARG.Core.Game.YargProfile).GetField(
            "_savedModifiers", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string DescribeModifiers()
        {
            return "ok " + string.Join(" | ", PlayerContainer.Players.Select(player =>
                $"'{player.Profile.Name}' now={player.Profile.CurrentModifiers} " +
                $"saved={_savedModifiersField?.GetValue(player.Profile)} speed={GlobalVariables.State.SongSpeed:0.##}"));
        }

        private static string DescribeShow()
        {
            var songs = Setlist.ResolveSongs();
            var text = $"ok setlist playing={Setlist.IsPlaying} index={Setlist.Index} songs=[" +
                string.Join(", ", songs.Select(song => $"'{song.Name}'")) + "]";
            var context = GlobalVariables.State.CurrentCareer;
            if (context is not null)
            {
                text += $" | context tier={context.TierIndex} song={context.SongIndex} source={context.Source}";
            }

            return $"{text} | {CareerShow.Describe()}";
        }

        /// <summary>
        /// <c>key=value</c> words with only the given keys allowed.
        /// </summary>
        private static bool TryParseOptions(string argument, out System.Collections.Generic.Dictionary<string, string> options,
            out string error, params string[] keys)
        {
            options = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var word in SplitWords(argument))
            {
                var equals = word.IndexOf('=');
                var key = equals > 0 ? word.Substring(0, equals).ToLowerInvariant() : string.Empty;
                if (!keys.Contains(key))
                {
                    error = $"unknown option '{word}'";
                    return false;
                }

                options[key] = word.Substring(equals + 1);
            }

            error = null;
            return true;
        }
    }
}
#endif
