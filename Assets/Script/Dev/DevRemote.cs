#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Menu;
using YARG.Menu.Navigation;
using YARG.Player;

namespace YARG.Dev
{
    /// <summary>
    /// Development-only remote control, for driving the game from scripts without OS-level input.
    /// Inert unless the game is launched with <c>-dev-remote &lt;directory&gt;</c>.
    ///
    /// The game polls <c>&lt;directory&gt;/cmd.txt</c>. Each line is one command, run in order; when
    /// the file has been processed it is deleted and <c>result.txt</c> is written with one line per
    /// command ("ok ..." or "error ...").
    ///
    /// Commands:
    ///   nav &lt;MenuAction&gt;        Send a menu action (Up, Down, Green, Red, ...), as a button press.
    ///   menu &lt;Menu&gt;             Push a menu directly (MainMenu, CareerList, Career, GameModes, ...).
    ///   back                    Pop the current menu.
    ///   click &lt;x&gt; &lt;y&gt;           Click the UI at a position given as 0..1 fractions, origin top-left.
    ///   wait &lt;seconds&gt;          Pause before the next command.
    ///   shot &lt;path&gt;             Save a screenshot (PNG) of the game view.
    ///   window &lt;w&gt; &lt;h&gt;          Switch to a window of the given size.
    ///   state                   Report the active scene and selected UI object.
    ///   simulate ...            Commit a made-up career song result (see DevRemote.Career.cs).
    ///   career [career=...]     Report a career's progress for the current players.
    ///   scorescreen ...         Open the score screen with a made-up result.
    ///   band / profile / player / players / dialog / type / press
    ///                           Bands, test players and driving dialogs (see DevRemote.Bands.cs).
    ///   songs / validate / editor
    ///                           Career content checks and the career preset editor (see DevRemote.Content.cs).
    ///   career-bots / ready / waitfor / show
    ///                           Playing songs unattended with a bot (see DevRemote.Play.cs).
    /// </summary>
    public partial class DevRemote : MonoBehaviour
    {
        private const string ARG = "-dev-remote";
        private const float POLL_SECONDS = 0.1f;

        private string _directory;
        private string _commandPath;
        private string _resultPath;
        private bool _busy;
        private float _nextPoll;

        // Remote commands are not input, so without this the idle dimmer darkens every screenshot after
        // three minutes.
        private static readonly MethodInfo _resetIdleDimmer = typeof(YARG.Menu.Persistent.IdleDimmer).GetMethod(
            "ResetTimer", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo _invokeNavigation = typeof(Navigator).GetMethod(
            "InvokeNavigationEvent", BindingFlags.Instance | BindingFlags.NonPublic);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, ARG);
            if (index < 0 || index + 1 >= args.Length)
            {
                return;
            }

            var directory = args[index + 1];
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "DevRemote: cannot use the command directory.");
                return;
            }

            var remoteObject = new GameObject("DevRemote");
            DontDestroyOnLoad(remoteObject);

            var remote = remoteObject.AddComponent<DevRemote>();
            remote._directory = directory;
            remote._commandPath = Path.Combine(directory, "cmd.txt");
            remote._resultPath = Path.Combine(directory, "result.txt");

            // Keep responding while another window has focus.
            Application.runInBackground = true;

            YargLogger.LogFormatInfo("DevRemote: listening in {0}", directory);
        }

        private void Update()
        {
            // Unity's on-screen error console covers the lower-left of every screenshot.
            Debug.developerConsoleVisible = false;

            if (_busy || Time.unscaledTime < _nextPoll)
            {
                return;
            }

            _nextPoll = Time.unscaledTime + POLL_SECONDS;

            if (!File.Exists(_commandPath))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_commandPath);
                File.Delete(_commandPath);
            }
            catch (IOException)
            {
                // Still being written; try again on the next poll.
                return;
            }

            StartCoroutine(RunCommands(lines));
        }

        private IEnumerator RunCommands(string[] lines)
        {
            _busy = true;
            var results = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                {
                    continue;
                }

                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLowerInvariant();
                var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                string result = null;
                IEnumerator deferred = null;

                var dimmer = FindAnyObjectByType<YARG.Menu.Persistent.IdleDimmer>();
                if (dimmer != null)
                {
                    _resetIdleDimmer?.Invoke(dimmer, null);
                }

                try
                {
                    switch (command)
                    {
                        case "nav":
                            result = Navigate(argument);
                            break;
                        case "menu":
                            result = PushMenu(argument);
                            break;
                        case "back":
                            MenuManager.Instance.PopMenu();
                            result = "ok";
                            break;
                        case "click":
                            result = Click(argument);
                            break;
                        case "window":
                            result = SetWindow(argument);
                            break;
                        case "state":
                            result = DescribeState();
                            break;
                        case "simulate":
                            result = Simulate(argument);
                            break;
                        case "career":
                            result = DescribeCareer(argument);
                            break;
                        case "scorescreen":
                            result = ShowScoreScreen(argument);
                            break;
                        case "band":
                            result = BandCommand(argument);
                            break;
                        case "profile":
                            result = ProfileCommand(argument);
                            break;
                        case "player":
                            result = PlayerCommand(argument);
                            break;
                        case "players":
                            result = $"ok {DescribePlayers()}";
                            break;
                        case "dialog":
                            result = DescribeDialog();
                            break;
                        case "type":
                            result = TypeText(argument);
                            break;
                        case "press":
                            result = PressButton(argument);
                            break;
                        case "songs":
                            result = DescribeSongs(argument);
                            break;
                        case "validate":
                            result = ValidateCareer(argument);
                            break;
                        case "editor":
                            result = EditorCommand(argument);
                            break;
                        case "career-bots":
                            result = CareerBotsCommand(argument);
                            break;
                        case "ready":
                            result = Ready(argument);
                            break;
                        case "show":
                            result = DescribeShow();
                            break;
                        case "quitsong":
                            result = QuitSong();
                            break;
                        case "library":
                            result = LibraryCommand(argument);
                            break;
                        case "modifiers":
                            result = DescribeModifiers();
                            break;
                        case "waitfor":
                            deferred = WaitFor(argument, text => result = text);
                            break;
                        case "wait":
                            deferred = Wait(argument);
                            break;
                        case "shot":
                            deferred = Screenshot(argument, text => result = text);
                            break;
                        default:
                            result = $"error unknown command '{command}'";
                            break;
                    }
                }
                catch (Exception e)
                {
                    result = $"error {e.GetType().Name}: {e.Message}";
                    YargLogger.LogException(e, $"DevRemote: command failed: {line}");
                }

                if (deferred != null)
                {
                    yield return deferred;
                    result ??= "ok";
                }

                results.AppendLine($"{line} => {result}");

                // Let the game react for a frame before the next command.
                yield return null;
            }

            try
            {
                var temporaryPath = _resultPath + ".tmp";
                File.WriteAllText(temporaryPath, results.ToString());
                if (File.Exists(_resultPath))
                {
                    File.Delete(_resultPath);
                }

                File.Move(temporaryPath, _resultPath);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "DevRemote: cannot write the result file.");
            }

            _busy = false;
        }

        private static string Navigate(string argument)
        {
            if (!Enum.TryParse<MenuAction>(argument, true, out var action))
            {
                return $"error unknown menu action '{argument}'";
            }

            if (Navigator.Instance == null || _invokeNavigation == null)
            {
                return "error navigator is not available";
            }

            var player = PlayerContainer.Players.FirstOrDefault();
            _invokeNavigation.Invoke(Navigator.Instance, new object[] { new NavigationContext(action, player) });
            return "ok";
        }

        private static string PushMenu(string argument)
        {
            if (!Enum.TryParse<MenuManager.Menu>(argument, true, out var menu))
            {
                return $"error unknown menu '{argument}'";
            }

            if (MenuManager.Instance == null)
            {
                return "error menu manager is not available (not in the menu scene?)";
            }

            MenuManager.Instance.PushMenu(menu);
            return "ok";
        }

        private static string Click(string argument)
        {
            var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var y))
            {
                return "error usage: click <x 0..1> <y 0..1>";
            }

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return "error no event system";
            }

            var pointer = new PointerEventData(eventSystem)
            {
                // Screen space has its origin at the bottom-left.
                position = new Vector2(x * Screen.width, (1f - y) * Screen.height),
                button = PointerEventData.InputButton.Left,
            };

            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            if (hits.Count == 0)
            {
                return "error nothing under that position";
            }

            var target = hits[0].gameObject;
            pointer.pointerCurrentRaycast = hits[0];
            pointer.pointerPressRaycast = hits[0];

            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerUpHandler);
            var handler = ExecuteEvents.ExecuteHierarchy(target, pointer, ExecuteEvents.pointerClickHandler);

            return handler != null
                ? $"ok clicked '{handler.name}' (hit '{target.name}')"
                : $"error '{target.name}' has no click handler";
        }

        private static string SetWindow(string argument)
        {
            var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            {
                return "error usage: window <width> <height>";
            }

            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            return "ok";
        }

        private static string DescribeState()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var career = GlobalVariables.State.CurrentCareer;
            return $"ok scene={SceneManager.GetActiveScene().name} " +
                $"screen={Screen.width}x{Screen.height} " +
                $"selected={(selected != null ? selected.name : "none")}" +
                (career is null ? "" : $" careerSong={career.CareerSongId} band={career.BandId?.ToString() ?? "none"}");
        }

        private static IEnumerator Wait(string argument)
        {
            float.TryParse(argument, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds);
            yield return new WaitForSecondsRealtime(Mathf.Clamp(seconds, 0f, 120f));
        }

        private IEnumerator Screenshot(string argument, Action<string> setResult)
        {
            var path = string.IsNullOrEmpty(argument) ? Path.Combine(_directory, "shot.png") : argument;

            yield return new WaitForEndOfFrame();

            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                setResult($"ok {path}");
            }
            catch (Exception e)
            {
                setResult($"error {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }
    }
}
#endif
