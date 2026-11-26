using System;
using System.Linq;
using YARG.Core.Game;

namespace YARG.Career
{
    /// <summary>
    /// Development-only: bots count for career credit, as if they were human players, so a bot can play
    /// career songs unattended in tests. Two ways in, both only in the editor, development builds and
    /// playtest builds (YARG_TEST_BUILD):
    ///  - the switch: the launch argument <c>-career-bots-count</c> or the dev remote (<c>career-bots on</c>);
    ///  - a test band: a bot can be made a member of a band, and while that band is the active one bots
    ///    count.
    /// Always off in release builds, where bots cannot join bands and careers never credit bot play.
    /// </summary>
    public static class CareerBotCredit
    {
        public const string LAUNCH_ARGUMENT = "-career-bots-count";

#if UNITY_EDITOR || DEVELOPMENT_BUILD || YARG_TEST_BUILD
        /// <summary>
        /// Bots may be picked as band members (which makes the band a test band).
        /// </summary>
        public const bool BOT_MEMBERS_ALLOWED = true;

        private static bool? _switch;
        private static int _testBandFrame = -1;
        private static bool _testBand;

        /// <summary>
        /// The switch alone, without the active band (what "career-bots" reports and sets).
        /// </summary>
        public static bool Switch
        {
            get => _switch ??= Environment.GetCommandLineArgs().Contains(LAUNCH_ARGUMENT);
            set => _switch = value;
        }

        public static bool Enabled
        {
            get => Switch || ActiveBandHasBot();
            set => _switch = value;
        }

        // Asked many times while a menu draws; looked up once per frame.
        private static bool ActiveBandHasBot()
        {
            var frame = UnityEngine.Time.frameCount;
            if (frame == _testBandFrame)
            {
                return _testBand;
            }

            _testBandFrame = frame;
            _testBand = false;

            try
            {
                var careers = YARG.Scores.ScoreContainer.Careers;
                if (careers is not null && CareerBands.ActiveBand(careers) is { } band)
                {
                    _testBand = careers.GetBandMembers(band.Id)
                        .Any(id => YARG.Player.PlayerContainer.GetProfileById(id)?.IsBot == true);
                }
            }
            catch (Exception)
            {
                // No database yet: no test band.
            }

            return _testBand;
        }
#else
        public const bool BOT_MEMBERS_ALLOWED = false;

        public static bool Switch
        {
            get => false;
            set { }
        }

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
