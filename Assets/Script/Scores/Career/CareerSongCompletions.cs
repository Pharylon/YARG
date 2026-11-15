using System;
using SQLite;

namespace YARG.Scores
{
    [Table("CareerSongCompletions")]
    public class CareerSongCompletions
    {
        [PrimaryKey][AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int CareerSaveId { get; set; }
        [Indexed]
        public Guid CareerId { get; set; }

        public Guid TierId { get; set; }
        public int TierIndex { get; set; }

        public Guid CareerSongId { get; set; }
        public int SongIndex { get; set; }

        public byte[] SongChecksum { get; set; }
        public int GameRecordId { get; set; }

        public int BandStars { get; set; }
        // BandStars holds 5 for a gold result; this keeps the gold (fans count it). False on rows
        // written before the column existed.
        public bool IsGold { get; set; }
        public int BandScore { get; set; }

        public DateTime CompletedAt { get; set; }

        // The kind of completion (single song, tier playlist, debug grant)
        public CareerCompletionSource Source { get; set; }
    }
}