using NUnit.Framework;
using YARG.Career;
using YARG.Scores;

namespace YARG.Career.Tests
{
    public class CareerEvaluationTests
    {
        private static CareerTier Tier(int songs, UnlockType unlockType = UnlockType.StarCount, int criteria = 0)
        {
            var tier = new CareerTier
            {
                Id = Guid.NewGuid(),
                Name = "Tier",
                UnlockType = unlockType,
                UnlockCriteria = criteria,
                Songs = new CareerSong[songs],
            };

            for (var i = 0; i < songs; i++)
            {
                tier.Songs[i] = new CareerSong { Id = Guid.NewGuid(), Parent = tier };
            }

            return tier;
        }

        private static CareerSongCompletions Completion(CareerSong song, int stars)
        {
            return new CareerSongCompletions { CareerSongId = song.Id, BandStars = stars };
        }

        [Test]
        public void StarSumOfPreviousTiersUnlocksNextTier()
        {
            var career = new CareerPreset(Guid.NewGuid(), "Test", "Test career", false);
            var first = Tier(3);
            var second = Tier(3, UnlockType.StarCount, 10);
            career.AddTier(first);
            career.AddTier(second);

            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.Add(Completion(first.Songs[0], 5));
            snapshot.Completions.Add(Completion(first.Songs[1], 3));
            // A replay with fewer stars never lowers the best result.
            snapshot.Completions.Add(Completion(first.Songs[1], 1));

            var evaluation = CareerEvaluation.Evaluate(career, snapshot);

            Assert.That(evaluation.Tiers[0].Unlocked, Is.True);
            Assert.That(evaluation.Tiers[0].StarSum, Is.EqualTo(8));
            Assert.That(evaluation.Tiers[1].Unlocked, Is.False);
            Assert.That(evaluation.Tiers[1].RemainingToUnlock, Is.EqualTo(2));

            snapshot.Completions.Add(Completion(first.Songs[2], 2));
            evaluation = CareerEvaluation.Evaluate(career, snapshot);

            Assert.That(evaluation.Tiers[1].Unlocked, Is.True);
            Assert.That(evaluation.Tiers[1].RemainingToUnlock, Is.EqualTo(0));
        }
    }
}
