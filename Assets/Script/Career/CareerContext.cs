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

        // Retail mode (see RetailMode). Closer: the song is the gig's closer, any song of the library
        // picked by the players; it is recorded on its own, not as one of the gig's songs (CareerSongId
        // and SongIndex are not used). Substitute: the song is played in place of the bonus gig's song
        // the context names, and counts for it.
        public bool Closer;
        public bool Substitute;
    }
}
