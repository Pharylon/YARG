using NUnit.Framework;

namespace YARG.Career.Tests
{
    /// <summary>
    /// Content warnings (<see cref="CareerValidation"/>): unreachable tiers, fan tiers with nothing to earn
    /// fans from, contract parameters out of range, and the other cheap checks.
    /// </summary>
    public class CareerValidationTests
    {
        private static CareerTier Tier(int songs, UnlockType unlockType = UnlockType.FanCount, int criteria = 0,
            VenueSize venue = VenueSize.Starter, bool bonus = false, float? multiplier = null,
            CareerContract contract = null)
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
                Contract = contract,
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

        private static CareerContract Contract(CareerContractType type, float parameter, int bonus = 25)
        {
            return new CareerContract { Type = type, Parameter = parameter, BonusPercent = bonus };
        }

        private static List<CareerIssue> Issues(CareerPreset career) => CareerValidation.Validate(career);

        [Test]
        public void ShippedCareersHaveNoWarnings()
        {
            Assert.That(CareerPreset.Defaults, Has.Count.EqualTo(3));
            foreach (var career in CareerPreset.Defaults)
            {
                Assert.That(Issues(career).Select(issue => issue.Describe(career)), Is.Empty, career.Name);
            }
        }

        [Test]
        public void FanTierAboveTheMostThatCanBeEarnedIsUnreachable()
        {
            // Tier 0: 3 Starter songs, at most 3 * 1,250 (gold) = 3,750 fans.
            var career = Career(Tier(3, UnlockType.StarCount), Tier(2, criteria: 3_750), Tier(2, criteria: 3_751 + 2_500));

            var issues = Issues(career);
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Type, Is.EqualTo(CareerIssueType.UnreachableTier));
            Assert.That(issues[0].TierIndex, Is.EqualTo(2));
            Assert.That(issues[0].Required, Is.EqualTo(6_251));
            Assert.That(issues[0].Available, Is.EqualTo(6_250), "tiers 0 and 1, all gold");
            Assert.That(issues[0].Describe(career), Does.Contain($"{6_251:N0} fans").And.Contain($"{6_250:N0}"));
        }

        [Test]
        public void ContractBonusesAndBonusTiersCountTowardsTheMaximum()
        {
            // Gold on 2 Starter songs = 2,500; +20% on both = 3,000. A bonus tier at Small: 2 * 10,000 * 0.5.
            var tier0 = Tier(2, UnlockType.StarCount, contract: Contract(CareerContractType.MinStars, 4, bonus: 20));
            var bonus = Tier(2, criteria: 0, venue: VenueSize.Small, bonus: true);
            var bonusFans = 2 * CareerFans.SongFans(bonus, CareerFans.GOLD_STARS);

            Assert.That(Issues(Career(tier0, bonus, Tier(1, criteria: 3_000 + bonusFans))), Is.Empty);
            Assert.That(Issues(Career(tier0, bonus, Tier(1, criteria: 3_001 + bonusFans))).Single().Type,
                Is.EqualTo(CareerIssueType.UnreachableTier));
        }

        [Test]
        public void ATierOnlyAnUnreachableTierCouldOpenIsUnreachableToo()
        {
            // Tier 2 would be reachable with tier 1's fans, but tier 1 can never open.
            var career = Career(Tier(1, UnlockType.StarCount), Tier(5, criteria: 10_000, venue: VenueSize.Epic),
                Tier(1, criteria: 5_000));

            var issues = Issues(career);
            Assert.That(issues.Select(issue => issue.TierIndex), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(issues[1].Available, Is.EqualTo(1_250), "only tier 0 can open before it");
        }

        [Test]
        public void TiersOpenInAnyOrderForTheMaximum()
        {
            // The second tier needs fans that only the later (cheaper) tier gives: still reachable.
            var career = Career(Tier(2, UnlockType.StarCount), Tier(1, criteria: 4_000), Tier(2, criteria: 1_000));
            Assert.That(Issues(career), Is.Empty);
        }

        [Test]
        public void FanTierWithNoFanSongsBeforeIt()
        {
            var noSongs = Career(Tier(0, UnlockType.StarCount), Tier(2, criteria: 100));
            Assert.That(Issues(noSongs).Single().Type, Is.EqualTo(CareerIssueType.NoFanSongs));

            var noFans = Career(Tier(3, UnlockType.StarCount, multiplier: 0f), Tier(2, criteria: 1));
            var issue = Issues(noFans).Single();
            Assert.That(issue.Type, Is.EqualTo(CareerIssueType.NoFanSongs));
            Assert.That(issue.Describe(noFans), Does.Contain("no tier that can open before it"));

            // Needing no fans is fine (it opens right away).
            Assert.That(Issues(Career(Tier(0, UnlockType.StarCount), Tier(2, criteria: 0))), Is.Empty);
        }

        [Test]
        public void StarAndCompletionTiers()
        {
            // 3 regular songs before: at most 15 stars and 3 completions. Bonus tiers do not count.
            var tiers = new[]
            {
                Tier(3, UnlockType.StarCount),
                Tier(4, UnlockType.StarCount, criteria: 0, bonus: true),
            };

            Assert.That(Issues(Career(tiers[0], tiers[1], Tier(1, UnlockType.StarCount, criteria: 15))), Is.Empty);
            var stars = Issues(Career(tiers[0], tiers[1], Tier(1, UnlockType.StarCount, criteria: 16))).Single();
            Assert.That((stars.Type, stars.Required, stars.Available), Is.EqualTo((CareerIssueType.UnreachableTier, 16, 15)));

            Assert.That(Issues(Career(tiers[0], tiers[1], Tier(1, UnlockType.CompletionCount, criteria: 3))), Is.Empty);
            Assert.That(Issues(Career(tiers[0], tiers[1], Tier(1, UnlockType.CompletionCount, criteria: 4))).Single().Type,
                Is.EqualTo(CareerIssueType.UnreachableTier));

            // A star tier with only bonus tiers before it stays locked (see CareerEvaluation).
            var afterBonus = Career(Tier(2, UnlockType.StarCount), Tier(2, UnlockType.StarCount, bonus: true),
                Tier(1, UnlockType.StarCount));
            Assert.That(Issues(afterBonus), Is.Empty, "tier 0 is regular, so tier 2 counts it");
            var onlyBonus = Career(Tier(2, UnlockType.StarCount, bonus: true), Tier(1, UnlockType.StarCount));
            Assert.That(Issues(onlyBonus).Single().TierIndex, Is.EqualTo(1));
        }

        [TestCase(CareerContractType.MinStars, 0f, false)]
        [TestCase(CareerContractType.MinStars, 1f, true)]
        [TestCase(CareerContractType.MinStars, 6f, true)]
        [TestCase(CareerContractType.MinStars, 7f, false)]
        [TestCase(CareerContractType.MinStars, 4.5f, false)]
        [TestCase(CareerContractType.MinAccuracy, 0f, true)]
        [TestCase(CareerContractType.MinAccuracy, 92.5f, true)]
        [TestCase(CareerContractType.MinAccuracy, 100f, true)]
        [TestCase(CareerContractType.MinAccuracy, 101f, false)]
        [TestCase(CareerContractType.MinAccuracy, -1f, false)]
        [TestCase(CareerContractType.FullCombo, 0f, true)]
        [TestCase(CareerContractType.FullCombo, 3f, true)]
        [TestCase(CareerContractType.FullCombo, -1f, false)]
        [TestCase(CareerContractType.FullCombo, 1.5f, false)]
        [TestCase(CareerContractType.MinDifficulty, 0f, true)]
        [TestCase(CareerContractType.MinDifficulty, 5f, true)]
        [TestCase(CareerContractType.MinDifficulty, 6f, false)]
        [TestCase(CareerContractType.MinDifficulty, -1f, false)]
        [TestCase(CareerContractType.NoFailOff, 0f, true)]
        [TestCase(CareerContractType.NoFailOff, 42f, true)]
        [TestCase(CareerContractType.FullBand, 1f, true)]
        [TestCase(CareerContractType.FullBand, 4f, true)]
        [TestCase(CareerContractType.FullBand, 0f, false)]
        public void ContractParameterRange(CareerContractType type, float parameter, bool valid)
        {
            Assert.That(CareerValidation.ParameterInRange(type, parameter), Is.EqualTo(valid));

            var issues = Issues(Career(Tier(1, UnlockType.StarCount, contract: Contract(type, parameter))));
            if (valid)
            {
                Assert.That(issues, Is.Empty);
                return;
            }

            var issue = issues.Single();
            Assert.That(issue.Type, Is.EqualTo(CareerIssueType.ContractParameterOutOfRange));
            Assert.That((issue.TierIndex, issue.SongIndex, issue.ContractType, issue.Value),
                Is.EqualTo((0, -1, type, parameter)));
        }

        [Test]
        public void NoContractIsFineWhateverItsNumbers()
        {
            // A None contract's parameter and bonus mean nothing.
            var career = Career(Tier(3, UnlockType.StarCount, contract: Contract(CareerContractType.None, -5, bonus: -5)));
            Assert.That(Issues(career), Is.Empty);
        }

        [Test]
        public void NegativeBonusAndMultiplier()
        {
            var career = Career(Tier(2, UnlockType.StarCount, multiplier: -1f,
                contract: Contract(CareerContractType.MinStars, 4, bonus: -10)));
            var issues = Issues(career);

            Assert.That(issues.Select(issue => issue.Type), Is.EquivalentTo(new[]
            {
                CareerIssueType.NegativeFanMultiplier, CareerIssueType.NegativeContractBonus,
            }));
        }

        [Test]
        public void DuplicateIds()
        {
            var career = Career(Tier(2, UnlockType.StarCount), Tier(2, UnlockType.StarCount, criteria: 1));
            career.Tiers[1].Songs[0].Id = career.Tiers[0].Songs[1].Id;
            career.Tiers[1].Id = career.Tiers[0].Id;

            var issues = Issues(career);
            Assert.That(issues.Select(issue => (issue.Type, issue.TierIndex, issue.SongIndex)), Is.EquivalentTo(new[]
            {
                (CareerIssueType.DuplicateTierId, 1, -1),
                (CareerIssueType.DuplicateSongId, 1, 0),
            }));
        }

        [Test]
        public void EmptyCareerHasNoWarnings()
        {
            Assert.That(Issues(Career()), Is.Empty);
            Assert.That(CareerValidation.Validate(null), Is.Empty);
        }
    }
}
