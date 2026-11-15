using System;
using System.Collections.Generic;
using YARG.Scores;

namespace YARG.Career
{
    /// <summary>
    /// What one committed career song changed: fans gained and tiers unlocked, from the evaluations
    /// before and after the commit. Nothing is stored; the score path keeps the latest one in
    /// <see cref="Last"/> for the score screen.
    /// </summary>
    public class CareerSongOutcome
    {
        public Guid CareerId;
        public Guid CareerSongId;
        public bool UsesFans;

        public int FansBefore;
        public int FansAfter;
        public int FansGained => FansAfter - FansBefore;

        // The tiers this result unlocked, in career order.
        public List<int> UnlockedTiers = new();
        public List<string> UnlockedTierNames = new();

        /// <summary>
        /// The outcome of the career song committed last by the score path, for the score screen that
        /// follows. Cleared whenever a score is recorded without a career commit; check that
        /// <see cref="CareerSongId"/> matches the song shown.
        /// </summary>
        public static CareerSongOutcome Last { get; set; }

        /// <summary>
        /// Commit a song completion, sync tier progress, and remember the outcome in <see cref="Last"/>.
        /// </summary>
        public static CareerSongOutcome Commit(CareerPreset career, CareerDatabase careers,
            CareerSongCompletionCommit commit)
        {
            var before = CareerEvaluation.Evaluate(career, careers.LoadProgressSnapshot(commit.CareerSaveId));

            careers.CommitSongCompletion(commit);
            var after = career.SyncProgress(careers, commit.CareerSaveId);

            Last = after is null ? null : Compare(career, commit.CareerSongId, before, after);
            return Last;
        }

        /// <summary>
        /// The outcome a result would have, without committing it: the snapshot plus one more row.
        /// </summary>
        public static CareerSongOutcome Preview(CareerPreset career, CareerProgressSnapshot snapshot,
            CareerSongCompletions completion)
        {
            var before = CareerEvaluation.Evaluate(career, snapshot);

            var withResult = new CareerProgressSnapshot
            {
                CareerSaveId = snapshot.CareerSaveId,
                Completions = new List<CareerSongCompletions>(snapshot.Completions) { completion },
                TierProgress = snapshot.TierProgress,
            };

            return Compare(career, completion.CareerSongId, before, CareerEvaluation.Evaluate(career, withResult));
        }

        public static CareerSongOutcome Compare(CareerPreset career, Guid careerSongId, CareerEvaluation before,
            CareerEvaluation after)
        {
            var outcome = new CareerSongOutcome
            {
                CareerId = career.Id,
                CareerSongId = careerSongId,
                UsesFans = after.UsesFans,
                FansBefore = before.TotalFans,
                FansAfter = after.TotalFans,
            };

            for (var i = 0; i < after.Tiers.Count && i < before.Tiers.Count; i++)
            {
                if (after.Tiers[i].Unlocked && !before.Tiers[i].Unlocked)
                {
                    outcome.UnlockedTiers.Add(i);
                    outcome.UnlockedTierNames.Add(i < career.Tiers.Count ? career.Tiers[i].Name : null);
                }
            }

            return outcome;
        }
    }
}
