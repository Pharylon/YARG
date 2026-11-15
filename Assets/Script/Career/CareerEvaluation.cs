using System;
using System.Collections.Generic;
using System.Linq;

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
            // Fans from the best result (see CareerFans); 0 if not completed
            public int Fans;
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
            public int RemainingToUnlock;
            // Sum of the tier's songs' fans
            public int Fans;
            public List<SongResult> Songs = new();
        }

        public int CareerSaveId;
        public List<TierResult> Tiers = new();

        // Whether the career opts in to fans (CareerFans.UsesFans). Fans are computed either way, but
        // only shown for careers that use them.
        public bool UsesFans;
        // The save's fans: every song's best result, bonus tiers included (at their reduced rate).
        public int TotalFans;

        public static CareerEvaluation Evaluate(CareerPreset career, CareerProgressSnapshot snapshot)
        {
            var evaluation = new CareerEvaluation
            {
                CareerSaveId = snapshot.CareerSaveId,
                UsesFans = CareerFans.UsesFans(career),
            };

            // Best stars per song: max BandStars across all committed completions for that song.
            // Gold is tracked beside it (a gold row stores 5 in BandStars), for fans only.
            var bestStarsBySong = new Dictionary<Guid, int>();
            var goldSongs = new HashSet<Guid>();
            foreach (var completion in snapshot.Completions)
            {
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
                    var songResult = new SongResult
                    {
                        CareerSongId = song.Id,
                        SongIndex = songIndex,
                        Completed = bestStarsBySong.ContainsKey(song.Id),
                        BestStars = bestStarsBySong.GetValueOrDefault(song.Id),
                        BestIsGold = goldSongs.Contains(song.Id),
                    };

                    if (songResult.Completed)
                    {
                        songResult.Fans = CareerFans.SongFans(tier,
                            songResult.BestIsGold ? CareerFans.GOLD_STARS : songResult.BestStars);
                    }

                    result.Songs.Add(songResult);

                    // A fan-count tier's song can still earn more fans until it is gold.
                    if (tier.UnlockType == UnlockType.CompletionCount && songResult.Completed
                        || tier.UnlockType == UnlockType.StarCount && songResult.BestStars >= 5
                        || tier.UnlockType == UnlockType.FanCount && songResult.BestIsGold)
                    {
                        song.IsCompleted = true;
                    }
                }

                result.Completed = result.Songs.Count > 0 && result.Songs.All(song => song.Completed);
                result.StarSum = result.Songs.Sum(song => song.BestStars);
                result.Fans = result.Songs.Sum(song => song.Fans);
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
    }
}
