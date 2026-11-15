using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Scores;

namespace YARG.Career.Tests
{
    public class CareerFansTests
    {
        private static CareerTier Tier(int songs, UnlockType unlockType = UnlockType.StarCount, int criteria = 0,
            VenueSize venue = VenueSize.Starter, bool bonus = false, float? multiplier = null)
        {
            var tier = new CareerTier
            {
                Id = Guid.NewGuid(),
                Name = "Tier",
                UnlockType = unlockType,
                UnlockCriteria = criteria,
                VenueSize = venue,
                IsBonus = bonus,
                FanMultiplier = multiplier,
                Songs = new CareerSong[songs],
            };

            for (var i = 0; i < songs; i++)
            {
                tier.Songs[i] = new CareerSong { Id = Guid.NewGuid(), Parent = tier };
            }

            return tier;
        }

        private static CareerPreset Career(params CareerTier[] tiers)
        {
            var career = new CareerPreset(Guid.NewGuid(), "Test", "Test career", false);
            foreach (var tier in tiers)
            {
                career.AddTier(tier);
            }

            return career;
        }

        // A completion row as the score path writes it: gold is stored as 5 stars plus IsGold.
        private static CareerSongCompletions Completion(CareerSong song, int stars)
        {
            return new CareerSongCompletions
            {
                CareerSongId = song.Id,
                BandStars = Math.Min(stars, 5),
                IsGold = stars >= CareerFans.GOLD_STARS,
            };
        }

        private static CareerEvaluation Evaluate(CareerPreset career, params CareerSongCompletions[] rows)
        {
            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.AddRange(rows);
            return CareerEvaluation.Evaluate(career, snapshot);
        }

        [TestCase(VenueSize.Starter, 5, 100)]
        [TestCase(VenueSize.NotSpecified, 5, 100)]
        [TestCase(VenueSize.Small, 5, 250)]
        [TestCase(VenueSize.Medium, 5, 600)]
        [TestCase(VenueSize.Large, 5, 1500)]
        [TestCase(VenueSize.Epic, 5, 4000)]
        [TestCase(VenueSize.Small, 6, 313)] // gold: 250 * 1.25 = 312.5, rounded up
        [TestCase(VenueSize.Large, 4, 1125)]
        [TestCase(VenueSize.Large, 3, 750)]
        [TestCase(VenueSize.Large, 2, 375)]
        [TestCase(VenueSize.Large, 1, 375)]
        [TestCase(VenueSize.Large, 0, 0)]
        [TestCase(VenueSize.Epic, 6, 5000)]
        public void SongFansFollowTheTable(VenueSize venue, int stars, int expected)
        {
            Assert.That(CareerFans.SongFans(Tier(1, venue: venue), stars), Is.EqualTo(expected));
        }

        [Test]
        public void FanMultiplierOverridesVenueSize()
        {
            // 10x the Starter base, whatever the venue says.
            Assert.That(CareerFans.SongFans(Tier(1, venue: VenueSize.Epic, multiplier: 10f), 5), Is.EqualTo(1000));
            Assert.That(CareerFans.SongFans(Tier(1, venue: VenueSize.Epic, multiplier: 10f), 3), Is.EqualTo(500));
        }

        [Test]
        public void BonusTiersEarnHalfAndCountTowardTotal()
        {
            var main = Tier(1, venue: VenueSize.Small);
            var bonus = Tier(2, UnlockType.FanCount, 0, VenueSize.Small, bonus: true);
            var career = Career(main, bonus);

            var evaluation = Evaluate(career,
                Completion(main.Songs[0], 5),
                Completion(bonus.Songs[0], 5),
                Completion(bonus.Songs[1], 6));

            Assert.That(evaluation.Tiers[0].Fans, Is.EqualTo(250));
            Assert.That(evaluation.Tiers[1].Songs[0].Fans, Is.EqualTo(125));
            Assert.That(evaluation.Tiers[1].Songs[1].Fans, Is.EqualTo(156)); // 250 * 1.25 / 2 = 156.25
            Assert.That(evaluation.Tiers[1].Fans, Is.EqualTo(281));
            Assert.That(evaluation.TotalFans, Is.EqualTo(531));
        }

        [Test]
        public void OnlyTheBestResultPerSongCounts()
        {
            var tier = Tier(2, venue: VenueSize.Medium);
            var career = Career(tier, Tier(1, UnlockType.FanCount, 10_000));

            // Played three times: 3, 5, then a worse replay. The best counts once.
            var evaluation = Evaluate(career,
                Completion(tier.Songs[0], 3),
                Completion(tier.Songs[0], 5),
                Completion(tier.Songs[0], 2));

            Assert.That(evaluation.Tiers[0].Songs[0].BestStars, Is.EqualTo(5));
            Assert.That(evaluation.Tiers[0].Songs[0].Fans, Is.EqualTo(600));
            Assert.That(evaluation.Tiers[0].Songs[1].Fans, Is.EqualTo(0));
            Assert.That(evaluation.TotalFans, Is.EqualTo(600));
        }

        [Test]
        public void GoldCountsForFansButShowsAsFiveStars()
        {
            var tier = Tier(1, venue: VenueSize.Large);
            var career = Career(tier, Tier(1, UnlockType.FanCount, 10_000));

            var evaluation = Evaluate(career, Completion(tier.Songs[0], 5), Completion(tier.Songs[0], 6));

            var song = evaluation.Tiers[0].Songs[0];
            Assert.That(song.BestStars, Is.EqualTo(5));
            Assert.That(song.BestIsGold, Is.True);
            Assert.That(song.Fans, Is.EqualTo(1875));
            Assert.That(evaluation.Tiers[0].StarSum, Is.EqualTo(5));
        }

        [Test]
        public void FanCountUnlocksAgainstTotalSoSeveralTiersOpenAtOnce()
        {
            var start = Tier(3, venue: VenueSize.Starter);
            var bonus = Tier(1, UnlockType.FanCount, 100, VenueSize.Starter, bonus: true);
            var club = Tier(1, UnlockType.FanCount, 200, VenueSize.Small);
            var bar = Tier(1, UnlockType.FanCount, 250, VenueSize.Small);
            var theater = Tier(1, UnlockType.FanCount, 1000, VenueSize.Large);
            var career = Career(start, bonus, club, bar, theater);

            var evaluation = Evaluate(career);
            Assert.That(evaluation.UsesFans, Is.True);
            Assert.That(evaluation.TotalFans, Is.EqualTo(0));
            Assert.That(evaluation.Tiers.Select(t => t.Unlocked), Is.EqualTo(new[] { true, false, false, false, false }));
            Assert.That(evaluation.Tiers[2].RemainingToUnlock, Is.EqualTo(200));

            // 75 + 100 + 125 = 300 fans: the bonus tier and both 200/250 tiers open together.
            evaluation = Evaluate(career,
                Completion(start.Songs[0], 4),
                Completion(start.Songs[1], 5),
                Completion(start.Songs[2], 6));

            Assert.That(evaluation.TotalFans, Is.EqualTo(300));
            Assert.That(evaluation.Tiers.Select(t => t.Unlocked), Is.EqualTo(new[] { true, true, true, true, false }));
            Assert.That(evaluation.Tiers[3].RemainingToUnlock, Is.EqualTo(0));
            Assert.That(evaluation.Tiers[4].RemainingToUnlock, Is.EqualTo(700));

            // Fans from any tier count, including a later one played before an earlier one.
            evaluation = Evaluate(career,
                Completion(start.Songs[0], 4),
                Completion(start.Songs[1], 5),
                Completion(start.Songs[2], 6),
                Completion(bar.Songs[0], 5),
                Completion(bonus.Songs[0], 5));

            Assert.That(evaluation.TotalFans, Is.EqualTo(300 + 250 + 50));
            Assert.That(evaluation.Tiers[4].RemainingToUnlock, Is.EqualTo(400));
            Assert.That(evaluation.Tiers[2].Completed, Is.False);
            Assert.That(evaluation.Tiers[3].Completed, Is.True);
        }

        [Test]
        public void ExistingStyleCareerIsUnaffected()
        {
            // Star and completion unlocks with a bonus tier in between, as the default careers are built.
            var first = Tier(3);
            var bonus = Tier(2, UnlockType.StarCount, 5, bonus: true);
            var second = Tier(2, UnlockType.StarCount, 10);
            var third = Tier(2, UnlockType.CompletionCount, 5);
            var career = Career(first, bonus, second, third);

            var evaluation = Evaluate(career,
                Completion(first.Songs[0], 6), // gold: still 5 stars for stars and unlocks
                Completion(first.Songs[1], 3),
                Completion(first.Songs[1], 1),
                Completion(bonus.Songs[0], 5),
                Completion(bonus.Songs[1], 5));

            Assert.That(evaluation.UsesFans, Is.False);
            Assert.That(evaluation.Tiers.Select(t => t.Unlocked), Is.EqualTo(new[] { true, true, false, false }));
            Assert.That(evaluation.Tiers.Select(t => t.StarSum), Is.EqualTo(new[] { 8, 10, 0, 0 }));
            // Bonus stars do not count toward the next tier.
            Assert.That(evaluation.Tiers[2].RemainingToUnlock, Is.EqualTo(2));
            // Completion unlocks count regular-tier songs completed: 2 of 5.
            Assert.That(evaluation.Tiers[3].RemainingToUnlock, Is.EqualTo(3));
            Assert.That(evaluation.Tiers[0].Songs[0].BestStars, Is.EqualTo(5));
            Assert.That(first.Songs[0].IsCompleted, Is.True);
            Assert.That(first.Songs[1].IsCompleted, Is.False);

            evaluation = Evaluate(career,
                Completion(first.Songs[0], 5),
                Completion(first.Songs[1], 3),
                Completion(first.Songs[2], 2));

            Assert.That(evaluation.Tiers[2].Unlocked, Is.True);
            // 3 of 5 now.
            Assert.That(evaluation.Tiers[3].Unlocked, Is.False);
            Assert.That(evaluation.Tiers[3].RemainingToUnlock, Is.EqualTo(2));
            Assert.That(evaluation.Tiers[0].Completed, Is.True);
        }

        [Test]
        public void PreviewReportsFansGainedAndUnlocks()
        {
            var start = Tier(2);
            var club = Tier(1, UnlockType.FanCount, 150, VenueSize.Small);
            var career = Career(start, club);

            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.Add(Completion(start.Songs[0], 5));

            var outcome = CareerSongOutcome.Preview(career, snapshot, Completion(start.Songs[1], 4));
            Assert.That(outcome.UsesFans, Is.True);
            Assert.That(outcome.FansGained, Is.EqualTo(75));
            Assert.That(outcome.UnlockedTiers, Is.EqualTo(new[] { 1 }));

            // A worse replay gains nothing.
            outcome = CareerSongOutcome.Preview(career, snapshot, Completion(start.Songs[0], 2));
            Assert.That(outcome.FansGained, Is.EqualTo(0));
            Assert.That(outcome.UnlockedTiers, Is.Empty);
        }

        [Test]
        public void TierJsonRoundTripAndOldContent()
        {
            // Content written before fans existed still loads, with no fan fields set.
            const string old = @"{ ""Name"": ""Old"", ""Id"": ""9a3c1c43-6ad5-4c4b-9f05-0c1a3a0f3b11"",
                ""Songs"": [], ""VenueSize"": ""Small"", ""UnlockType"": ""CompletionCount"",
                ""UnlockCriteria"": 3, ""IsBonus"": true }";
            var oldTier = JsonConvert.DeserializeObject<CareerTier>(old);
            Assert.That(oldTier.UnlockType, Is.EqualTo(UnlockType.CompletionCount));
            Assert.That(oldTier.FanMultiplier, Is.Null);
            Assert.That(oldTier.City, Is.Null);
            Assert.That(oldTier.VenueName, Is.Null);
            // Unset fan fields are left out when saved again.
            Assert.That(JsonConvert.SerializeObject(oldTier), Does.Not.Contain("FanMultiplier"));

            var tier = Tier(0, UnlockType.FanCount, 2400, VenueSize.Large, multiplier: 12.5f);
            tier.City = "Springfield";
            tier.VenueName = "The Grand";

            var json = JsonConvert.SerializeObject(tier);
            Assert.That(json, Does.Contain(@"""UnlockType"":""FanCount"""));

            var loaded = JsonConvert.DeserializeObject<CareerTier>(json);
            Assert.That(loaded.UnlockType, Is.EqualTo(UnlockType.FanCount));
            Assert.That(loaded.UnlockCriteria, Is.EqualTo(2400));
            Assert.That(loaded.FanMultiplier, Is.EqualTo(12.5f));
            Assert.That(loaded.City, Is.EqualTo("Springfield"));
            Assert.That(loaded.VenueName, Is.EqualTo("The Grand"));

            var copy = new CareerTier(loaded);
            Assert.That(copy.FanMultiplier, Is.EqualTo(12.5f));
            Assert.That(copy.City, Is.EqualTo("Springfield"));
            Assert.That(copy.VenueName, Is.EqualTo("The Grand"));
            Assert.That(copy.UnlockType, Is.EqualTo(UnlockType.FanCount));
        }
    }
}
