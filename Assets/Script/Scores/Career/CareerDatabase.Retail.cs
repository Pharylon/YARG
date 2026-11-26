using System.Collections.Generic;
using System.Linq;

namespace YARG.Scores
{
    // Retail mode (see YARG.Career.RetailMode): which songs career play has unlocked for quickplay,
    // and the closers of gigs.
    public partial class CareerDatabase
    {
        private class ChecksumRow
        {
            public byte[] SongChecksum { get; set; }
        }

        /// <summary>
        /// The checksum of every song ever completed in a career, by anyone, in any save (reset ones
        /// too): songs of the setlists, songs played in place of a bonus gig's song, and closers.
        /// </summary>
        public List<byte[]> GetCareerSongChecksums()
        {
            return _db.Query<ChecksumRow>(
                    @"SELECT DISTINCT SongChecksum FROM CareerSongCompletions WHERE SongChecksum IS NOT NULL
                      UNION
                      SELECT DISTINCT SongChecksum FROM CareerClosers WHERE SongChecksum IS NOT NULL")
                .Select(row => row.SongChecksum)
                .Where(checksum => checksum is { Length: > 0 })
                .ToList();
        }

        /// <summary>
        /// The closer of the gig in this save, or null if none has been played yet.
        /// </summary>
        public CareerCloser GetCloser(int careerSaveId, System.Guid tierId)
        {
            return _db.FindWithQuery<CareerCloser>(
                "SELECT * FROM CareerClosers WHERE CareerSaveId = ? AND TierId = ? ORDER BY Id LIMIT 1",
                careerSaveId, tierId);
        }

        public List<CareerCloser> GetClosers(int careerSaveId)
        {
            return _db.Query<CareerCloser>("SELECT * FROM CareerClosers WHERE CareerSaveId = ? ORDER BY Id", careerSaveId);
        }

        /// <summary>
        /// Records the gig's closer. A gig has one: a later play of another song does not replace it
        /// (a replay of the same song updates its result if better). Returns the row that stands.
        /// </summary>
        public CareerCloser CommitCloser(CareerCloser closer)
        {
            var existing = GetCloser(closer.CareerSaveId, closer.TierId);
            if (existing is null)
            {
                _db.Insert(closer);
                return closer;
            }

            if (existing.SongChecksum is not null && closer.SongChecksum is not null &&
                existing.SongChecksum.SequenceEqual(closer.SongChecksum) &&
                (closer.IsGold && !existing.IsGold || closer.BandStars > existing.BandStars))
            {
                existing.BandStars = closer.BandStars;
                existing.IsGold = closer.IsGold;
                existing.GameRecordId = closer.GameRecordId;
                existing.PlayedAt = closer.PlayedAt;
                _db.Update(existing);
            }

            return existing;
        }
    }
}
