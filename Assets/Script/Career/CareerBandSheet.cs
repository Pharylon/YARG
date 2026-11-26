using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core;
using YARG.Scores;

namespace YARG.Career
{
    /// <summary>
    /// A band's identity sheet: fans and rank, totals, per-member records and the gig history, all derived
    /// on demand from the completion rows of the band's active saves and the score records they link to
    /// (<see cref="CareerSongCompletions.GameRecordId"/>). Nothing here is stored. <see cref="Build"/> is
    /// pure (covered by Tests/YARG.Career.Tests); <see cref="Load"/> only gathers the rows.
    ///
    /// Scope: the band's active saves of installed careers, the same saves its fans come from. A reset
    /// career (soft-deleted save) leaves the sheet and the history with it.
    /// </summary>
    public class CareerBandSheet
    {
        /// <summary>
        /// One played career of the band: the career content and the save's raw rows.
        /// </summary>
        public class Save
        {
            public CareerPreset Career;
            public CareerProgressSnapshot Snapshot;
        }

        /// <summary>
        /// A member's records over the band's gigs. Best streak and star power use are not here: they are
        /// not stored per player (only in replay files).
        /// </summary>
        public class MemberRecord
        {
            public Guid ProfileId;
            // Band gigs this member played in (has a score row on the gig's game record).
            public int Gigs;
            // Instruments played, most played first (then by instrument), with how many gigs each.
            public List<(Instrument instrument, int gigs)> Instruments = new();
            // Average note accuracy (0..1) over the member's gigs that had notes; null if none had.
            public float? AverageAccuracy;
            // Full combos (gigs with notes only).
            public int FullCombos;
        }

        /// <summary>
        /// One play in the gig history.
        /// </summary>
        public class Gig
        {
            public CareerSongCompletions Row;
            public CareerPreset Career;
            // The tier and song as the career content has them now; null if no longer in the content.
            public CareerTier Tier;
            public CareerSong Song;
            // The score record of the play; null for debug grants (no score was recorded).
            public GameRecord Record;
            public DateTime Date;
            // 1..5 (a gold result has 5 here and Gold set).
            public int Stars;
            public bool Gold;
            // The career uses fans; Fans is what this result is worth (contract bonus included if it met
            // the song's current contract), Counts whether it is the song's result that counts.
            public bool UsesFans;
            public int Fans;
            public bool Counts;
            // The contract the play was judged under (null: none) and whether it was met, as stored.
            public CareerContractType? ContractType;
            public bool ContractMet;
        }

        public int Fans;
        public CareerRank Rank;
        // The next rank and the fans still needed for it; null at the top rank.
        public (CareerRank rank, int fansNeeded)? NextRank;

        // Careers with at least one gig.
        public int CareersPlayed;
        // Every play (completion row), replays included.
        public int GigsPlayed;
        // Best stars per song, summed over every career (a gold result counts 5).
        public int TotalStars;
        // Plays that met their contract, out of the plays that had one (stored outcomes).
        public int ContractsMet;
        public int ContractGigs;

        // Current members, in membership order.
        public List<MemberRecord> Members = new();

        // Newest first.
        public List<Gig> History = new();

        /// <summary>
        /// Build the sheet. <paramref name="members"/>: the band's members; <paramref name="records"/> and
        /// <paramref name="playerScores"/>: the score rows the saves' completions link to (others are
        /// ignored).
        /// </summary>
        public static CareerBandSheet Build(IEnumerable<Guid> members, IEnumerable<Save> saves,
            IEnumerable<GameRecord> records, IEnumerable<PlayerScoreRecord> playerScores)
        {
            var sheet = new CareerBandSheet();
            var recordsById = new Dictionary<int, GameRecord>();
            foreach (var record in records ?? Enumerable.Empty<GameRecord>())
            {
                recordsById.TryAdd(record.Id, record);
            }

            var scoresByRecord = (playerScores ?? Enumerable.Empty<PlayerScoreRecord>())
                .GroupBy(score => score.GameRecordId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var evaluations = new List<CareerEvaluation>();
            var gigRecordIds = new List<int>();

            foreach (var save in saves ?? Enumerable.Empty<Save>())
            {
                if (save?.Career is null || save.Snapshot is null)
                {
                    continue;
                }

                var evaluation = CareerEvaluation.Evaluate(save.Career, save.Snapshot);
                evaluations.Add(evaluation);

                var rows = save.Snapshot.Completions;
                if (rows.Count == 0)
                {
                    continue;
                }

                sheet.CareersPlayed++;
                sheet.TotalStars += evaluation.Tiers.Sum(tier => tier.StarSum);

                var counted = new HashSet<int>(evaluation.Tiers
                    .SelectMany(tier => tier.Songs)
                    .Where(song => song.Completed)
                    .Select(song => song.CountedCompletionId));

                foreach (var row in rows)
                {
                    sheet.History.Add(GigFor(save.Career, evaluation, row, counted, recordsById));
                    if (row.GameRecordId > 0)
                    {
                        gigRecordIds.Add(row.GameRecordId);
                    }
                }
            }

            sheet.GigsPlayed = sheet.History.Count;
            sheet.ContractGigs = sheet.History.Count(gig => gig.ContractType is not null);
            sheet.ContractsMet = sheet.History.Count(gig => gig.ContractType is not null && gig.ContractMet);

            sheet.History = sheet.History
                .OrderByDescending(gig => gig.Date)
                .ThenByDescending(gig => gig.Row.Id)
                .ToList();

            sheet.Fans = CareerFans.BandFans(evaluations);
            sheet.Rank = CareerFans.Rank(sheet.Fans);
            sheet.NextRank = CareerFans.NextRank(sheet.Fans);

            foreach (var member in (members ?? Enumerable.Empty<Guid>()).Distinct())
            {
                sheet.Members.Add(RecordFor(member, gigRecordIds, scoresByRecord));
            }

            return sheet;
        }

        private static Gig GigFor(CareerPreset career, CareerEvaluation evaluation, CareerSongCompletions row,
            HashSet<int> counted, Dictionary<int, GameRecord> recordsById)
        {
            var tier = career.Tiers.FirstOrDefault(t => t.Id == row.TierId);
            var song = tier?.Songs?.FirstOrDefault(s => s.Id == row.CareerSongId);
            recordsById.TryGetValue(row.GameRecordId, out var record);

            var gig = new Gig
            {
                Row = row,
                Career = career,
                Tier = tier,
                Song = song,
                Record = record,
                Date = row.CompletedAt,
                Stars = row.BandStars,
                Gold = row.IsGold,
                UsesFans = evaluation.UsesFans,
                ContractType = row.ContractType,
                ContractMet = row.ContractMet,
            };

            // Valued the way the evaluation values every row (see CareerEvaluation.ApplyBestFans).
            if (tier is not null && song is not null)
            {
                var contract = CareerContracts.For(tier, song);
                var met = CareerContracts.RowMet(contract, row);
                gig.Fans = CareerFans.SongFans(tier, song, row.IsGold ? CareerFans.GOLD_STARS : row.BandStars,
                    met ? contract.BonusPercent : 0, row.SongChecksum);
                gig.Counts = counted.Contains(row.Id);
            }

            return gig;
        }

        private static MemberRecord RecordFor(Guid member, List<int> gigRecordIds,
            Dictionary<int, List<PlayerScoreRecord>> scoresByRecord)
        {
            var record = new MemberRecord { ProfileId = member };
            var instruments = new Dictionary<Instrument, int>();
            var accuracies = new List<float>();

            foreach (var recordId in gigRecordIds.Distinct())
            {
                if (!scoresByRecord.TryGetValue(recordId, out var scores))
                {
                    continue;
                }

                var own = scores.Where(score => score.PlayerId == member).ToList();
                if (own.Count == 0)
                {
                    continue;
                }

                record.Gigs++;
                foreach (var score in own)
                {
                    instruments[score.Instrument] = instruments.GetValueOrDefault(score.Instrument) + 1;

                    if (score.NotesHit + score.NotesMissed <= 0)
                    {
                        continue;
                    }

                    var percent = score.GetPercent();
                    if (!float.IsNaN(percent))
                    {
                        accuracies.Add(percent);
                    }

                    if (score.IsFc)
                    {
                        record.FullCombos++;
                    }
                }
            }

            record.Instruments = instruments
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => (pair.Key, pair.Value))
                .ToList();
            record.AverageAccuracy = accuracies.Count == 0 ? null : accuracies.Average();
            return record;
        }

        /// <summary>
        /// A tier's city matches the band's hometown: both set, equal ignoring case and surrounding
        /// spaces. Text only ("Hometown show"); it earns nothing extra.
        /// </summary>
        public static bool IsHometown(string city, string hometown)
        {
            return !string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(hometown) &&
                string.Equals(city.Trim(), hometown.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gather the band's rows (thin queries) and build the sheet. Saves of careers that are not
        /// installed are skipped, as for the band's fans.
        /// </summary>
        public static CareerBandSheet Load(CareerDatabase careers, Guid bandId, IEnumerable<CareerPreset> allCareers)
        {
            var byId = new Dictionary<Guid, CareerPreset>();
            foreach (var career in allCareers ?? Enumerable.Empty<CareerPreset>())
            {
                byId.TryAdd(career.Id, career);
            }

            var saves = new List<Save>();
            foreach (var save in careers.GetActiveSavesForBand(bandId))
            {
                if (byId.TryGetValue(save.CareerId, out var career))
                {
                    saves.Add(new Save { Career = career, Snapshot = careers.LoadProgressSnapshot(save.Id) });
                }
            }

            return Build(careers.GetBandMembers(bandId), saves, careers.GetBandGameRecords(bandId),
                careers.GetBandPlayerScores(bandId));
        }
    }
}
