using System;
using SQLite;

namespace YARG.Scores
{
    /// <summary>
    /// Retail mode: the song the players picked to close out a gig (see YARG.Career.RetailMode). One
    /// per gig of a save; it is not part of the setlist, it only unlocks its song for quickplay.
    /// </summary>
    [Table("CareerClosers")]
    public class CareerCloser
    {
        [PrimaryKey][AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int CareerSaveId { get; set; }
        public Guid CareerId { get; set; }
        public Guid TierId { get; set; }

        public byte[] SongChecksum { get; set; }
        public int GameRecordId { get; set; }
        public int BandStars { get; set; }
        public bool IsGold { get; set; }
        public DateTime PlayedAt { get; set; }
    }
}
