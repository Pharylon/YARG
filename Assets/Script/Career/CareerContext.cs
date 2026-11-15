using System;
using YARG.Scores;

namespace YARG.Career
{
    // Identifies the career context for an in-progress game. Carried from the career menu through
    // gameplay to the score-save path, so a score can be linked to a committed career completion.
    // The save itself is resolved at commit time from the set of players who actually played, so no
    // save ID is carried here.
    public class CareerContext
    {
        public Guid CareerId;
        public Guid TierId;
        public int TierIndex;
        public string VenueHint;
        public Guid CareerSongId;
        public int SongIndex;

        // The band the song is played as (see CareerBands.ActiveBandId), or null to credit the save of
        // the exact set of players who played.
        public Guid? BandId;

        // How the song is played: on its own, or as part of a show (a tier played as a setlist, see
        // CareerShow). Stored on the completion row.
        public CareerCompletionSource Source;
    }
}
