using System;
using System.Linq;
using YARG.Core.Game;

namespace YARG.Career
{
    /// <summary>
    /// Development-only switch: bots count for career credit, as if they were human players, so a bot can
    /// play career songs unattended in tests. Off by default; turned on by the launch argument
    /// <c>-career-bots-count</c> or the dev remote (<c>career-bots on</c>). Always off in release builds,
    /// where careers never credit bot play.
    /// </summary>
    public static class CareerBotCredit
    {
        public const string LAUNCH_ARGUMENT = "-career-bots-count";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool? _enabled;

        public static bool Enabled
        {
            get => _enabled ??= Environment.GetCommandLineArgs().Contains(LAUNCH_ARGUMENT);
            set => _enabled = value;
        }
#else
        public static bool Enabled
        {
            get => false;
            set { }
        }
#endif

        /// <summary>
        /// Bots count as human players for the song being played: the switch is on and the song is a career
        /// song. Quickplay and setlist shows from the music library are never affected.
        /// </summary>
        public static bool BotsCount => Enabled && GlobalVariables.State.CurrentCareer is not null;

        /// <summary>
        /// Whether this profile's result counts as a human's for the song being played.
        /// </summary>
        public static bool CountsAsHuman(YargProfile profile) => !profile.IsBot || BotsCount;
    }
}
