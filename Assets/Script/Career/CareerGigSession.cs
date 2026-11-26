using System.Linq;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Player;

namespace YARG.Career
{
    /// <summary>
    /// Applies a career song's gig rules (<see cref="CareerGigRules"/>) to the play, and only to the play.
    ///
    /// Modifiers go on as session modifiers (<see cref="YargProfile.ApplySessionModifiers"/>): they change what
    /// gameplay reads (<see cref="YargProfile.CurrentModifiers"/>) but not the selection saved with the profile,
    /// which is all that is ever written to disk. So quitting the song, or closing the game in the middle of it,
    /// cannot leave them on anyone's profile. They are dropped again when the career menu opens, when a show ends,
    /// and by the instrument select itself (which starts every selection from the saved modifiers). The song
    /// speed only changes <c>GlobalVariables.State.SongSpeed</c>, which is reset after every song outside a show.
    /// </summary>
    public static class CareerGigSession
    {
        private static bool _applied;

        /// <summary>
        /// Called by the instrument select right before gameplay loads. Does nothing unless a career song
        /// whose tier has gig rules is being started.
        /// </summary>
        public static void Apply()
        {
            var context = GlobalVariables.State.CurrentCareer;
            if (context is null)
            {
                return;
            }

            var career = CareerBands.AllCareers().FirstOrDefault(c => c.Id == context.CareerId);
            var tier = career?.Tiers.FirstOrDefault(t => t.Id == context.TierId);
            if (!CareerGigRules.HasRules(tier))
            {
                return;
            }

            GlobalVariables.State.SongSpeed = CareerGigRules.SongSpeed(tier, GlobalVariables.State.SongSpeed);

            var forced = CareerGigRules.ForcedModifiers(tier);
            if (forced == Modifier.None)
            {
                return;
            }

            foreach (var player in PlayerContainer.Players)
            {
                if (player.SittingOut)
                {
                    continue;
                }

                var profile = player.Profile;
                var (possible, _) = profile.GameMode.PossibleModifiers(profile.CurrentInstrument);
                var modifiers = CareerGigRules.PlayerModifiers(profile.CurrentModifiers, forced, possible);
                if (modifiers == profile.CurrentModifiers)
                {
                    continue;
                }

                // ApplySessionModifiers copies from a profile; a scratch one carries the set.
                var session = new YargProfile();
                foreach (var modifier in CareerGigRules.Flags(modifiers))
                {
                    session.AddSingleModifier(modifier);
                }

                profile.ApplySessionModifiers(session);
                _applied = true;
            }

            YargLogger.LogFormatInfo<string, float>("Career: gig rules of '{0}' applied (song speed {1}).", tier.Name,
                GlobalVariables.State.SongSpeed);
        }

        /// <summary>
        /// Puts every player's own modifiers back, if gig rules changed them.
        /// </summary>
        public static void Restore()
        {
            if (!_applied)
            {
                return;
            }

            _applied = false;
            foreach (var player in PlayerContainer.Players)
            {
                player.Profile.RestoreSavedModifiers();
            }
        }
    }
}
