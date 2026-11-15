using System;
using SQLite;
using YARG.Career;

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

        // The contract the play was judged under (null: none) and whether it was met (see
        // CareerContracts). Null/false on rows written before the columns existed.
        public CareerContractType? ContractType { get; set; }
        public bool ContractMet { get; set; }

        public DateTime CompletedAt { get; set; }

        // The kind of completion (single song, tier playlist, debug grant)
        public CareerCompletionSource Source { get; set; }
    }
}