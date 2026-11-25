using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Scores;

namespace YARG.Career
{
    // Computed career progress for a single save: best stars per song, tier stars, fans, and unlock
    // state. Evaluated above the database layer from raw rows (CareerProgressSnapshot), never from
    // general score records.
    public class CareerEvaluation
    {
        public class SongResult
        {
            public Guid CareerSongId;
            public int SongIndex;
            public bool Completed;
            public int BestStars;
            // Whether the best result was gold (BestStars is 5 then)
            public bool BestIsGold;
            // Fans from the best result (see CareerFans); 0 if not completed. The best result is the
            // one worth the most fans, its contract bonus included.
            public int Fans;
            // The song is played under a contract (its own or its tier's).
            public bool HasContract;
            // The result counted for fans met the contract (so its fans include the bonus).
            public bool ContractMet;
            // Any result met the contract.
            public bool ContractEverMet;
            // Nothing more to gain on this song for this save: the row is shown struck through. Fan
            // careers: its fans are at the most it can earn (gold, and the contract met if it has a
            // bonus). Other careers: what its tier's unlock type counts is maxed (completed, or 5 stars).
            public bool Done;
            // Id of the completion row whose fans count (the first of equal ones); 0 if not completed.
            public int CountedCompletionId;
            // Set when the song can no longer improve unlock criteria for its tier
            // (e.g. finished for CompletionCount, or 5+ stars for StarCount).
            public bool CriteriaSatisfied;
        }

        public class TierResult
        {
            public Guid TierId;
            public int TierIndex;
            public bool IsBonus;
            public bool Unlocked;
            public bool Completed;
            // Average of the tier's songs' best stars (display value, 0..5 scale)
            public float Stars;
            // Sum of the tier's songs' best stars (used for StarCount unlocks)
            public int StarSum;
            // Number of the tier's songs that have been completed (used for CompletionCount display)
            public int CompletedSongs;
            public int RemainingToUnlock;
            // Sum of the tier's songs' fans
            public int Fans;
            public List<SongResult> Songs = new();
        }

        public int CareerSaveId;
        public List<TierResult> Tiers = new();

        // Fans are earned and shown in every career (CareerFans.UsesFans).
        public bool UsesFans;
        // The career has fan-count unlocks (CareerFans.FanGated): a song is done at its top fans.
        public bool FanGated;
        // The save's fans: every song's best result, bonus tiers included (at their reduced rate).
        public int TotalFans;

        public static CareerEvaluation Evaluate(CareerPreset career, CareerProgressSnapshot snapshot)
        {
            var evaluation = new CareerEvaluation
            {
                CareerSaveId = snapshot.CareerSaveId,
                UsesFans = CareerFans.UsesFans(career),
                FanGated = CareerFans.FanGated(career),
            };

            // Best stars per song: max BandStars across all committed completions for that song.
            // Gold is tracked beside it (a gold row stores 5 in BandStars), for fans only.
            var bestStarsBySong = new Dictionary<Guid, int>();
            var goldSongs = new HashSet<Guid>();
            var rowsBySong = new Dictionary<Guid, List<CareerSongCompletions>>();
            foreach (var completion in snapshot.Completions)
            {
                if (!rowsBySong.TryGetValue(completion.CareerSongId, out var rows))
                {
                    rowsBySong[completion.CareerSongId] = rows = new List<CareerSongCompletions>();
                }

                rows.Add(completion);

                if (!bestStarsBySong.TryGetValue(completion.CareerSongId, out var currentBest) ||
                    completion.BandStars > currentBest)
                {
                    bestStarsBySong[completion.CareerSongId] = completion.BandStars;
                }

                if (completion.IsGold)
                {
                    goldSongs.Add(completion.CareerSongId);
                }
            }

            TierResult previousRegularTier = null;
            var completedCount = 0;
            var starSum = 0;

            for (var tierIndex = 0; tierIndex < career.Tiers.Count; tierIndex++)
            {
                var tier = career.Tiers[tierIndex];
                var result = new TierResult
                {
                    TierId = tier.Id,
                    TierIndex = tierIndex,
                    IsBonus = tier.IsBonus,
                };

                for (var songIndex = 0; songIndex < tier.Songs.Length; songIndex++)
                {
                    var song = tier.Songs[songIndex];
                    var completed = bestStarsBySong.ContainsKey(song.Id);
                    var bestStars = bestStarsBySong.GetValueOrDefault(song.Id);
                    var songResult = new SongResult
                    {
                        CareerSongId = song.Id,
                        SongIndex = songIndex,
                        Completed = completed,
                        BestStars = bestStars,
                        BestIsGold = goldSongs.Contains(song.Id),
                        CriteriaSatisfied =
                            tier.UnlockType == UnlockType.CompletionCount && completed ||
                            tier.UnlockType == UnlockType.StarCount && bestStars >= 5,
                    };

                    var contract = CareerContracts.For(tier, song);
                    songResult.HasContract = contract is not null;
                    if (songResult.Completed)
                    {
                        ApplyBestFans(songResult, tier, song, contract, rowsBySong[song.Id]);
                    }

                    songResult.Done = evaluation.FanGated
                        ? songResult.Completed && songResult.Fans >=
                            CareerFans.SongFans(tier, song, CareerFans.GOLD_STARS, contract?.BonusPercent ?? 0)
                        : tier.UnlockType == UnlockType.CompletionCount && songResult.Completed
                            || tier.UnlockType == UnlockType.StarCount && songResult.BestStars >= 5;

                    result.Songs.Add(songResult);
                }

                // Every song of the set, or as many as the players pick (encores never count towards it).
                result.Completed = CareerSetlists.IsComplete(tier, songIndex => result.Songs[songIndex].Completed);
                result.StarSum = result.Songs.Sum(song => song.BestStars);
                result.Fans = result.Songs.Sum(song => song.Fans);
                result.CompletedSongs = result.Songs.Count(song => song.Completed);
                result.Stars = result.Songs.Count == 0 ? 0 : (float) result.StarSum / result.Songs.Count;

                // The first tier is always unlocked. Otherwise, the previous *regular* tier must meet
                // this tier's unlock criteria: bonus tiers are excluded from unlocking other tiers.
                if (tierIndex == 0)
                {
                    result.Unlocked = true;
                }
                else if (tier.UnlockType == UnlockType.FanCount)
                {
                    // Needs the save's total fans, so it is decided below, after every tier.
                }
                else if (previousRegularTier is not null)
                {
                    switch (tier.UnlockType)
                    {
                        case UnlockType.CompletionCount:
                        {
                            result.Unlocked = completedCount >= tier.UnlockCriteria;

                            result.RemainingToUnlock = Math.Max(0, tier.UnlockCriteria - completedCount);
                            break;
                        }
                        case UnlockType.StarCount:
                        {
                            result.Unlocked = starSum >= tier.UnlockCriteria;
                            result.RemainingToUnlock = Math.Max(0, tier.UnlockCriteria - starSum);
                            break;
                        }
                    }
                }
                else
                {
                    // Only bonus tiers precede this one, and bonus tiers do not count towards
                    // unlocks, so no progress can reach it. Left locked; the menu reports the full
                    // criteria as still required. Content should not be authored this way.
                    result.RemainingToUnlock = Math.Max(0, tier.UnlockCriteria);
                }

                if (!tier.IsBonus)
                {
                    completedCount += result.Songs.Count(song => song.Completed);
                    starSum += result.StarSum;
                    previousRegularTier = result;
                }

                evaluation.Tiers.Add(result);
            }

            // Fan-count tiers unlock against the save's total fans rather than the tiers before them,
            // so several can be open at once and the players pick their path.
            evaluation.TotalFans = evaluation.Tiers.Sum(tier => tier.Fans);
            for (var tierIndex = 1; tierIndex < career.Tiers.Count; tierIndex++)
            {
                var tier = career.Tiers[tierIndex];
                if (tier.UnlockType == UnlockType.FanCount)
                {
                    var result = evaluation.Tiers[tierIndex];
                    result.Unlocked = evaluation.TotalFans >= tier.UnlockCriteria;
                    result.RemainingToUnlock = Math.Max(0, tier.UnlockCriteria - evaluation.TotalFans);
                }
            }

            return evaluation;
        }

        /// <summary>
        /// The song's fans: of all its results, the one worth the most, where a result that met the
        /// song's contract (as the content declares it now) earns the contract bonus. So a lower-star
        /// result that met the contract counts instead of a better one that missed it whenever it is worth
        /// more fans; on a tie, the one that met the contract counts.
        /// </summary>
        private static void ApplyBestFans(SongResult songResult, CareerTier tier, CareerSong song,
            CareerContract contract, List<CareerSongCompletions> rows)
        {
            songResult.Fans = -1;
            foreach (var row in rows)
            {
                var met = CareerContracts.RowMet(contract, row);
                var fans = CareerFans.SongFans(tier, song, row.IsGold ? CareerFans.GOLD_STARS : row.BandStars,
                    met ? contract.BonusPercent : 0);

                songResult.ContractEverMet |= met;
                if (fans > songResult.Fans || fans == songResult.Fans && met && !songResult.ContractMet)
                {
                    songResult.Fans = fans;
                    songResult.ContractMet = met;
                    songResult.CountedCompletionId = row.Id;
                }
            }

            songResult.Fans = Math.Max(0, songResult.Fans);
        }
    }
}
