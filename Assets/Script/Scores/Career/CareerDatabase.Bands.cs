using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Scores
{
    /// <summary>
    /// Bands and band-owned saves. A band save has <see cref="CareerSaves.BandId"/> set and no
    /// <see cref="CareerSaveProfiles"/> rows: who may play it is the band's membership, not a fixed set
    /// of profiles. Saves without a band resolve by player set exactly as before.
    /// </summary>
    public partial class CareerDatabase
    {
        #region Bands

        public CareerBand CreateBand(string name, string hometown, string genre, IEnumerable<Guid> memberIds)
        {
            var band = new CareerBand
            {
                Id = Guid.NewGuid(),
                Name = name,
                Hometown = hometown ?? string.Empty,
                Genre = genre ?? string.Empty,
                CreatedAt = DateTime.Now,
            };

            _db.RunInTransaction(() =>
            {
                _db.Insert(band);
                foreach (var profileId in memberIds.Distinct())
                {
                    InsertMember(band.Id, profileId);
                }
            });

            return band;
        }

        public void RenameBand(Guid bandId, string name)
        {
            _db.Execute("UPDATE Bands SET Name = ? WHERE Id = ?", name, bandId);
        }

        public void SetBandHometown(Guid bandId, string hometown)
        {
            _db.Execute("UPDATE Bands SET Hometown = ? WHERE Id = ?", hometown ?? string.Empty, bandId);
        }

        public void SetBandGenre(Guid bandId, string genre)
        {
            _db.Execute("UPDATE Bands SET Genre = ? WHERE Id = ?", genre ?? string.Empty, bandId);
        }

        /// <summary>
        /// Soft delete: the band stops being listed and its saves stop being reachable, but nothing is
        /// removed.
        /// </summary>
        public void SoftDeleteBand(Guid bandId)
        {
            _db.Execute("UPDATE Bands SET IsDeleted = 1 WHERE Id = ?", bandId);
        }

        /// <summary>
        /// The band, or null if it does not exist or was deleted.
        /// </summary>
        public CareerBand GetBand(Guid bandId)
        {
            return _db.FindWithQuery<CareerBand>("SELECT * FROM Bands WHERE Id = ? AND IsDeleted = 0", bandId);
        }

        /// <summary>
        /// Every band that is not deleted, oldest first.
        /// </summary>
        public List<CareerBand> GetBands()
        {
            return _db.Query<CareerBand>("SELECT * FROM Bands WHERE IsDeleted = 0 ORDER BY CreatedAt, rowid");
        }

        /// <summary>
        /// The bands (not deleted) this profile is a member of, oldest first.
        /// </summary>
        public List<CareerBand> GetBandsForProfile(Guid profileId)
        {
            return _db.Query<CareerBand>(
                @"SELECT b.* FROM Bands b
                  INNER JOIN BandMembers bm ON bm.BandId = b.Id
                  WHERE bm.ProfileId = ? AND b.IsDeleted = 0
                  ORDER BY b.CreatedAt, b.rowid",
                profileId);
        }

        public List<Guid> GetBandMembers(Guid bandId)
        {
            return _db.Query<CareerBandMember>("SELECT * FROM BandMembers WHERE BandId = ? ORDER BY JoinedAt, Id", bandId)
                      .Select(member => member.ProfileId)
                      .Distinct()
                      .ToList();
        }

        /// <summary>
        /// Adds the profile to the band. Idempotent.
        /// </summary>
        public void AddBandMember(Guid bandId, Guid profileId)
        {
            var existing = _db.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM BandMembers WHERE BandId = ? AND ProfileId = ?", bandId, profileId);
            if (existing == 0)
            {
                InsertMember(bandId, profileId);
            }
        }

        public void RemoveBandMember(Guid bandId, Guid profileId)
        {
            _db.Execute("DELETE FROM BandMembers WHERE BandId = ? AND ProfileId = ?", bandId, profileId);
        }

        private void InsertMember(Guid bandId, Guid profileId)
        {
            _db.Insert(new CareerBandMember
            {
                BandId = bandId,
                ProfileId = profileId,
                JoinedAt = DateTime.Now,
            });
        }

        #endregion

        #region Band saves

        /// <summary>
        /// The band's active save for this career, or null. There is at most one: a new save is only
        /// created when none is active, and "new career" soft-deletes the old one.
        /// </summary>
        public CareerSaves FindActiveSaveForBand(Guid careerId, Guid bandId)
        {
            return _db.FindWithQuery<CareerSaves>(
                @"SELECT * FROM CareerSaves
                  WHERE CareerId = ? AND BandId = ? AND IsDeleted = 0
                  ORDER BY Id DESC",
                careerId, bandId);
        }

        /// <summary>
        /// Resolve the band's active save for this career, creating one if none exists.
        /// </summary>
        public int GetOrCreateSave(Guid careerId, int careerVersion, Guid bandId)
        {
            var existing = FindActiveSaveForBand(careerId, bandId);
            if (existing is not null)
            {
                return existing.Id;
            }

            var save = new CareerSaves
            {
                CareerId = careerId,
                CareerVersion = careerVersion,
                CreatedAt = DateTime.Now,
                BandId = bandId,
            };
            _db.Insert(save);

            return save.Id;
        }

        /// <summary>
        /// All active saves of a band, one per career it has played.
        /// </summary>
        public List<CareerSaves> GetActiveSavesForBand(Guid bandId)
        {
            return _db.Query<CareerSaves>("SELECT * FROM CareerSaves WHERE BandId = ? AND IsDeleted = 0", bandId);
        }

        /// <summary>
        /// Hand a profile-set save over to a band: from now on it is found through the band and no
        /// longer by its player set. Does nothing if the band already has an active save for that career.
        /// Returns whether the save was adopted.
        /// </summary>
        public bool AdoptSave(int careerSaveId, Guid bandId)
        {
            var save = _db.FindWithQuery<CareerSaves>(
                "SELECT * FROM CareerSaves WHERE Id = ? AND IsDeleted = 0 AND BandId IS NULL", careerSaveId);
            if (save is null || FindActiveSaveForBand(save.CareerId, bandId) is not null)
            {
                return false;
            }

            _db.Execute("UPDATE CareerSaves SET BandId = ? WHERE Id = ?", bandId, careerSaveId);
            return true;
        }

        #endregion

        #region Band records

        /// <summary>
        /// The game records (score history) behind the completion rows of the band's active saves.
        /// Completions without one (debug grants) have none.
        /// </summary>
        public List<GameRecord> GetBandGameRecords(Guid bandId)
        {
            return _db.Query<GameRecord>(
                @"SELECT DISTINCT g.* FROM GameRecords g
                  INNER JOIN CareerSongCompletions c ON c.GameRecordId = g.Id
                  INNER JOIN CareerSaves s ON s.Id = c.CareerSaveId
                  WHERE s.BandId = ? AND s.IsDeleted = 0",
                bandId);
        }

        /// <summary>
        /// Every player's score row of those game records (members and sit-ins alike).
        /// </summary>
        public List<PlayerScoreRecord> GetBandPlayerScores(Guid bandId)
        {
            return _db.Query<PlayerScoreRecord>(
                @"SELECT DISTINCT p.* FROM PlayerScores p
                  INNER JOIN CareerSongCompletions c ON c.GameRecordId = p.GameRecordId
                  INNER JOIN CareerSaves s ON s.Id = c.CareerSaveId
                  WHERE s.BandId = ? AND s.IsDeleted = 0 AND c.GameRecordId > 0",
                bandId);
        }

        #endregion
    }
}
