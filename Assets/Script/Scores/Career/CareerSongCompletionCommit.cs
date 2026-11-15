using System;
using YARG.Career;

namespace YARG.Scores
{
    // Everything needed to commit a single career song completion.
    public class CareerSongCompletionCommit
    {
        public int CareerSaveId;
        public Guid CareerId;
        public Guid TierId;
        public int TierIndex;
        public Guid CareerSongId;
        public int SongIndex;
        public byte[] SongChecksum;
        public int GameRecordId;
        public int BandStars;
        public bool IsGold;
        public CareerContractType? ContractType;
        public bool ContractMet;
        public int BandScore;
        public DateTime CompletedAt;
        public CareerCompletionSource Source;
    }
}
