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
            // Tier 0: 3 Starter songs, at most 3 * 125 (gold) = 375 fans.
            var career = Career(Tier(3, UnlockType.StarCount), Tier(2, criteria: 375), Tier(2, criteria: 376 + 250));

            var issues = Issues(career);
            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Type, Is.EqualTo(CareerIssueType.UnreachableTier));
            Assert.That(issues[0].TierIndex, Is.EqualTo(2));
            Assert.That(issues[0].Required, Is.EqualTo(626));
            Assert.That(issues[0].Available, Is.EqualTo(625), "tiers 0 and 1, all gold");
            Assert.That(issues[0].Describe(career), Does.Contain("626 fans").And.Contain("625"));
        }

        [Test]
        public void ContractBonusesAndBonusTiersCountTowardsTheMaximum()
        {
            // Gold on 2 Starter songs = 250; +20% on one song = 275. A bonus tier at Small: 2 * 312.5 * 0.5.
            var tier0 = Tier(2, UnlockType.StarCount);
            tier0.Songs[0].Contract = Contract(CareerContractType.MinStars, 4, bonus: 20);
            var bonus = Tier(2, criteria: 0, venue: VenueSize.Small, bonus: true);
            var bonusFans = 2 * CareerFans.SongFans(bonus, CareerFans.GOLD_STARS);

            Assert.That(Issues(Career(tier0, bonus, Tier(1, criteria: 275 + bonusFans))), Is.Empty);
            Assert.That(Issues(Career(tier0, bonus, Tier(1, criteria: 276 + bonusFans))).Single().Type,
                Is.EqualTo(CareerIssueType.UnreachableTier));
        }

        [Test]
        public void ATierOnlyAnUnreachableTierCouldOpenIsUnreachableToo()
        {
            // Tier 2 would be reachable with tier 1's fans, but tier 1 can never open.
            var career = Career(Tier(1, UnlockType.StarCount), Tier(5, criteria: 1000, venue: VenueSize.Epic),
                Tier(1, criteria: 500));

            var issues = Issues(career);
            Assert.That(issues.Select(issue => issue.TierIndex), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(issues[1].Available, Is.EqualTo(125), "only tier 0 can open before it");
        }

        [Test]
        public void TiersOpenInAnyOrderForTheMaximum()
        {
            // The second tier needs fans that only the later (cheaper) tier gives: still reachable.
            var career = Career(Tier(2, UnlockType.StarCount), Tier(1, criteria: 400), Tier(2, criteria: 100));
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

            var onTier = Career(Tier(1, UnlockType.StarCount, contract: Contract(type, parameter)));
            var onSong = Career(Tier(2, UnlockType.StarCount));
            onSong.Tiers[0].Songs[1].Contract = Contract(type, parameter);

            foreach (var (career, songIndex) in new[] { (onTier, -1), (onSong, 1) })
            {
                var issues = Issues(career);
                if (valid)
                {
                    Assert.That(issues, Is.Empty);
                    continue;
                }

                var issue = issues.Single();
                Assert.That(issue.Type, Is.EqualTo(CareerIssueType.ContractParameterOutOfRange));
                Assert.That((issue.TierIndex, issue.SongIndex, issue.ContractType, issue.Value),
                    Is.EqualTo((0, songIndex, type, parameter)));
            }
        }

        [Test]
        public void SongContractsWithoutATierContractAndExemptionsAreFine()
        {
            var career = Career(Tier(3, UnlockType.StarCount, contract: Contract(CareerContractType.FullBand, 2)),
                Tier(2, criteria: 100));
            // An exemption (None) with a parameter that means nothing, and a song contract on a tier without one.
            career.Tiers[0].Songs[0].Contract = Contract(CareerContractType.None, -5, bonus: -5);
            career.Tiers[1].Songs[1].Contract = Contract(CareerContractType.MinAccuracy, 90);

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
