using Newtonsoft.Json;
using NUnit.Framework;
using SQLite;
using YARG.Core;
using YARG.Core.Game;
using YARG.Scores;

namespace YARG.Career.Tests
{
    /// <summary>
    /// Gig contracts: the Phase A judge (every type, met and missed, solo and edge cases), which result
    /// counts for fans, the strikethrough rule, content JSON, and the completion row columns.
    /// </summary>
    public class CareerContractsTests
    {
        private static readonly Guid Alice = Guid.NewGuid();
        private static readonly Guid Bob = Guid.NewGuid();
        private static readonly Guid Carol = Guid.NewGuid();

        private static CareerContract Contract(CareerContractType type, float parameter = 0, int bonus = 25)
        {
            return new CareerContract { Type = type, Parameter = parameter, BonusPercent = bonus };
        }

        private static GameRecord Record(StarAmount stars = StarAmount.Star5)
        {
            return new GameRecord { BandStars = stars };
        }

        private static PlayerScoreRecord Player(Guid id, Difficulty difficulty = Difficulty.Expert, bool fc = false,
            int hit = 100, int missed = 0, float? percent = -1)
        {
            return new PlayerScoreRecord
            {
                PlayerId = id,
                Difficulty = difficulty,
                IsFc = fc,
                NotesHit = hit,
                NotesMissed = missed,
                // -1: as the game writes it (hit / total, or 1 with no notes, like BaseStats.Percent)
                Percent = percent < 0 ? (hit + missed == 0 ? 1f : (float) hit / (hit + missed)) : percent,
            };
        }

        private static bool Met(CareerContract contract, GameRecord record, params PlayerScoreRecord[] players)
        {
            return CareerContracts.IsMet(contract, record, players, true);
        }

        #region Judge

        [Test]
        public void NoContractIsNeverMet()
        {
            Assert.That(Met(null, Record(), Player(Alice, fc: true)), Is.False);
            Assert.That(Met(Contract(CareerContractType.None), Record(), Player(Alice, fc: true)), Is.False);
            Assert.That(Met(Contract((CareerContractType) 99), Record(), Player(Alice, fc: true)), Is.False,
                "a type from newer content is never met");
        }

        [Test]
        public void MinStars()
        {
            var four = Contract(CareerContractType.MinStars, 4);
            Assert.That(Met(four, Record(StarAmount.Star4), Player(Alice)), Is.True);
            Assert.That(Met(four, Record(StarAmount.Star5), Player(Alice)), Is.True);
            Assert.That(Met(four, Record(StarAmount.Star3), Player(Alice)), Is.False);
            Assert.That(Met(four, Record(StarAmount.StarGold), Player(Alice)), Is.True);
            Assert.That(Met(four, null, Player(Alice)), Is.False, "no game record: missing stat");

            var gold = Contract(CareerContractType.MinStars, CareerFans.GOLD_STARS);
            Assert.That(Met(gold, Record(StarAmount.StarGold), Player(Alice)), Is.True);
            Assert.That(Met(gold, Record(StarAmount.Star5), Player(Alice)), Is.False);
        }

        [Test]
        public void MinAccuracyIsTheAverageOfThePlayers()
        {
            var ninety = Contract(CareerContractType.MinAccuracy, 90);
            var alice = Player(Alice, hit: 95, missed: 5);  // 95%
            var bob = Player(Bob, hit: 85, missed: 15);     // 85%

            Assert.That(Met(ninety, Record(), alice, bob), Is.True, "average 90%");
            Assert.That(Met(Contract(CareerContractType.MinAccuracy, 91), Record(), alice, bob), Is.False);
            Assert.That(Met(ninety, Record(), alice), Is.True, "solo");
            Assert.That(Met(ninety, Record(), bob), Is.False, "solo");
        }

        [Test]
        public void MinAccuracyEdgeCases()
        {
            var ninety = Contract(CareerContractType.MinAccuracy, 90);

            // A player with no notes (Percent 1 as the game writes it) is left out of the average.
            Assert.That(Met(ninety, Record(), Player(Alice, hit: 0, missed: 0), Player(Bob, hit: 80, missed: 20)),
                Is.False);
            // Nobody has notes: no accuracy at all.
            Assert.That(Met(ninety, Record(), Player(Alice, hit: 0, missed: 0)), Is.False);
            Assert.That(Met(ninety, Record()), Is.False, "no players");
            // Older rows have no Percent: computed from the notes.
            Assert.That(Met(ninety, Record(), Player(Alice, hit: 9, missed: 1, percent: null)), Is.True);
            Assert.That(Met(ninety, Record(), Player(Alice, hit: 8, missed: 2, percent: null)), Is.False);
            // 100% asks for every note.
            Assert.That(Met(Contract(CareerContractType.MinAccuracy, 100), Record(), Player(Alice, hit: 100)), Is.True);
            Assert.That(Met(Contract(CareerContractType.MinAccuracy, 100), Record(), Player(Alice, hit: 999, missed: 1)),
                Is.False);
        }

        [Test]
        public void FullComboByEveryPlayer()
        {
            var all = Contract(CareerContractType.FullCombo, 0);

            Assert.That(Met(all, Record(), Player(Alice, fc: true), Player(Bob, fc: true)), Is.True);
            Assert.That(Met(all, Record(), Player(Alice, fc: true), Player(Bob, fc: false)), Is.False);
            Assert.That(Met(all, Record(), Player(Alice, fc: true)), Is.True, "solo");
            Assert.That(Met(all, Record(), Player(Alice, fc: false)), Is.False, "solo");
            Assert.That(Met(all, Record()), Is.False, "no players");
        }

        [Test]
        public void FullComboByAnyPlayer()
        {
            var any = Contract(CareerContractType.FullCombo, 1);

            Assert.That(Met(any, Record(), Player(Alice, fc: false), Player(Bob, fc: true)), Is.True);
            Assert.That(Met(any, Record(), Player(Alice, fc: false), Player(Bob, fc: false)), Is.False);
            Assert.That(Met(any, Record(), Player(Alice, fc: true)), Is.True, "solo");

            var two = Contract(CareerContractType.FullCombo, 2);
            Assert.That(Met(two, Record(), Player(Alice, fc: true), Player(Bob, fc: true), Player(Carol)), Is.True);
            Assert.That(Met(two, Record(), Player(Alice, fc: true)), Is.False, "solo cannot give two full combos");
        }

        [Test]
        public void FullComboIgnoresPlayersWithoutNotes()
        {
            // A player with no notes cannot full combo, and does not hold back "every player".
            var noNotes = Player(Bob, fc: true, hit: 0, missed: 0);

            Assert.That(Met(Contract(CareerContractType.FullCombo, 1), Record(), noNotes), Is.False);
            Assert.That(Met(Contract(CareerContractType.FullCombo, 0), Record(), noNotes), Is.False);
            Assert.That(Met(Contract(CareerContractType.FullCombo, 0), Record(),
                Player(Alice, fc: true), Player(Bob, fc: false, hit: 0, missed: 0)), Is.True);
        }

        [Test]
        public void MinDifficultyAppliesToEveryPlayer()
        {
            var hard = Contract(CareerContractType.MinDifficulty, (float) Difficulty.Hard);

            Assert.That(Met(hard, Record(), Player(Alice, Difficulty.Expert), Player(Bob, Difficulty.Hard)), Is.True);
            Assert.That(Met(hard, Record(), Player(Alice, Difficulty.Expert), Player(Bob, Difficulty.Medium)), Is.False);
            Assert.That(Met(hard, Record(), Player(Alice, Difficulty.Hard)), Is.True, "solo");
            Assert.That(Met(hard, Record(), Player(Alice, Difficulty.Easy)), Is.False, "solo");
            Assert.That(Met(hard, Record()), Is.False, "no players");

            var expert = Contract(CareerContractType.MinDifficulty, (float) Difficulty.Expert);
            Assert.That(Met(expert, Record(), Player(Alice, Difficulty.ExpertPlus)), Is.True, "Expert+ is above Expert");
        }

        [Test]
        public void NoFailOffNeedsTheSettingOff()
        {
            var clean = Contract(CareerContractType.NoFailOff);
            var players = new[] { Player(Alice) };

            Assert.That(CareerContracts.IsMet(clean, Record(), players, true), Is.True);
            Assert.That(CareerContracts.IsMet(clean, Record(), players, false), Is.False);
            Assert.That(CareerContracts.IsMet(clean, Record(), players, null), Is.False, "unknown setting");
        }

        [Test]
        public void FullBandCountsDistinctPlayers()
        {
            var two = Contract(CareerContractType.FullBand, 2);

            Assert.That(Met(two, Record(), Player(Alice), Player(Bob)), Is.True);
            Assert.That(Met(two, Record(), Player(Alice), Player(Bob), Player(Carol)), Is.True);
            Assert.That(Met(two, Record(), Player(Alice)), Is.False, "solo");
            Assert.That(Met(two, Record(), Player(Alice), Player(Alice)), Is.False, "one player, two rows");
            Assert.That(Met(Contract(CareerContractType.FullBand, 1), Record(), Player(Alice)), Is.True);
            Assert.That(Met(Contract(CareerContractType.FullBand, 1), Record()), Is.False, "no players");
        }

        [Test]
        public void ApplyWritesTheRawOutcome()
        {
            var commit = new CareerSongCompletionCommit();
            CareerContracts.Apply(commit, Contract(CareerContractType.FullBand, 2), Record(),
                new[] { Player(Alice), Player(Bob) }, true);
            Assert.That(commit.ContractType, Is.EqualTo(CareerContractType.FullBand));
            Assert.That(commit.ContractMet, Is.True);

            CareerContracts.Apply(commit, null, Record(), new[] { Player(Alice) }, true);
            Assert.That(commit.ContractType, Is.Null);
            Assert.That(commit.ContractMet, Is.False);
        }

        #endregion

        #region Content

        private static CareerTier Tier(int songs, VenueSize venue = VenueSize.Large,
            UnlockType unlockType = UnlockType.StarCount, int criteria = 0)
        {
            var tier = new CareerTier
            {
                Id = Guid.NewGuid(),
                Name = "Tier",
                VenueSize = venue,
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

        private static CareerPreset Career(params CareerTier[] tiers)
        {
            var career = new CareerPreset(Guid.NewGuid(), "Test", "Test career", false);
            foreach (var tier in tiers)
            {
                career.AddTier(tier);
            }

            return career;
        }

        // A tier that makes the career use fans, so fan rules apply.
        private static CareerTier FanGate() => Tier(1, VenueSize.Starter, UnlockType.FanCount, 1_000_000);

        [Test]
        public void SongContractOverridesTheTier()
        {
            var tier = Tier(3);
            tier.Contract = Contract(CareerContractType.NoFailOff);
            tier.Songs[1].Contract = Contract(CareerContractType.MinStars, 4);
            tier.Songs[2].Contract = Contract(CareerContractType.None);

            Assert.That(CareerContracts.For(tier, tier.Songs[0])?.Type, Is.EqualTo(CareerContractType.NoFailOff));
            Assert.That(CareerContracts.For(tier, tier.Songs[1])?.Type, Is.EqualTo(CareerContractType.MinStars));
            Assert.That(CareerContracts.For(tier, tier.Songs[2]), Is.Null, "None cancels the tier's contract");
            Assert.That(CareerContracts.For(Tier(1), null), Is.Null);

            var career = Career(tier);
            Assert.That(CareerContracts.For(career, tier.Id, 0, tier.Songs[1].Id)?.Type,
                Is.EqualTo(CareerContractType.MinStars));
            Assert.That(CareerContracts.For(career, tier.Id, 5, tier.Songs[0].Id)?.Type,
                Is.EqualTo(CareerContractType.NoFailOff), "found by id when the index is stale");
            Assert.That(CareerContracts.For(career, Guid.NewGuid(), 0, tier.Songs[0].Id), Is.Null);
        }

        [Test]
        public void ContractJsonRoundTripAndOldContent()
        {
            // Content written before contracts still loads, without one, and saves without one.
            const string old = @"{ ""Name"": ""Old"", ""Id"": ""9a3c1c43-6ad5-4c4b-9f05-0c1a3a0f3b11"",
                ""Songs"": [], ""VenueSize"": ""Small"", ""UnlockType"": ""StarCount"", ""UnlockCriteria"": 3 }";
            var oldTier = JsonConvert.DeserializeObject<CareerTier>(old);
            Assert.That(oldTier.Contract, Is.Null);
            Assert.That(JsonConvert.SerializeObject(oldTier), Does.Not.Contain("Contract"));

            var tier = Tier(0);
            tier.Contract = new CareerContract
            {
                Type = CareerContractType.MinAccuracy, Parameter = 92.5f, BonusPercent = 30, Text = "Tight!",
            };

            var json = JsonConvert.SerializeObject(tier);
            Assert.That(json, Does.Contain(@"""Type"":""MinAccuracy"""));

            var loaded = JsonConvert.DeserializeObject<CareerTier>(json);
            Assert.That(loaded.Contract.Type, Is.EqualTo(CareerContractType.MinAccuracy));
            Assert.That(loaded.Contract.Parameter, Is.EqualTo(92.5f));
            Assert.That(loaded.Contract.BonusPercent, Is.EqualTo(30));
            Assert.That(loaded.Contract.Text, Is.EqualTo("Tight!"));

            // No text: left out.
            Assert.That(JsonConvert.SerializeObject(Contract(CareerContractType.NoFailOff)), Does.Not.Contain("Text"));

            // Copies are deep.
            var copy = new CareerTier(loaded);
            Assert.That(copy.Contract, Is.Not.SameAs(loaded.Contract));
            Assert.That(copy.Contract.Type, Is.EqualTo(CareerContractType.MinAccuracy));
            Assert.That(copy.Contract.Text, Is.EqualTo("Tight!"));

            var song = new CareerSong { Id = Guid.NewGuid(), Contract = Contract(CareerContractType.FullBand, 3) };
            var songCopy = new CareerSong(song);
            Assert.That(songCopy.Contract, Is.Not.SameAs(song.Contract));
            Assert.That(songCopy.Contract.Type, Is.EqualTo(CareerContractType.FullBand));
            Assert.That(songCopy.Contract.Parameter, Is.EqualTo(3f));
            Assert.That(new CareerSong(new CareerSong()).Contract, Is.Null);
        }

        #endregion

        #region Fans and evaluation

        private static CareerSongCompletions Row(CareerSong song, int stars, CareerContractType? type = null,
            bool met = false)
        {
            return new CareerSongCompletions
            {
                CareerSongId = song.Id,
                BandStars = Math.Min(stars, 5),
                IsGold = stars >= CareerFans.GOLD_STARS,
                ContractType = type,
                ContractMet = met,
            };
        }

        private static CareerEvaluation Evaluate(CareerPreset career, params CareerSongCompletions[] rows)
        {
            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.AddRange(rows);
            return CareerEvaluation.Evaluate(career, snapshot);
        }

        [Test]
        public void BonusOnlyForAResultThatMetTheContract()
        {
            var tier = Tier(2);
            tier.Contract = Contract(CareerContractType.NoFailOff, bonus: 25);
            var career = Career(tier, FanGate());

            var evaluation = Evaluate(career,
                Row(tier.Songs[0], 5, CareerContractType.NoFailOff, met: true),
                Row(tier.Songs[1], 5, CareerContractType.NoFailOff, met: false));

            var songs = evaluation.Tiers[0].Songs;
            Assert.That(songs[0].Fans, Is.EqualTo(1875), "Large 5 stars 1,500 + 25%");
            Assert.That(songs[0].ContractMet, Is.True);
            Assert.That(songs[1].Fans, Is.EqualTo(1500));
            Assert.That(songs[1].ContractMet, Is.False);
            Assert.That(songs[1].HasContract, Is.True);
            Assert.That(evaluation.TotalFans, Is.EqualTo(1875 + 1500));
        }

        [Test]
        public void TheResultWorthMoreFansCounts()
        {
            // The best-star result missed the contract, a lower-star one met it: whichever is worth more
            // fans counts, bonus included. Stars (for star unlocks) still take the best stars.
            var tier = Tier(1);
            var song = tier.Songs[0];
            var career = Career(tier, FanGate());

            tier.Contract = Contract(CareerContractType.MinAccuracy, 95, bonus: 50);
            var evaluation = Evaluate(career,
                Row(song, 5, CareerContractType.MinAccuracy, met: false),  // 1,500
                Row(song, 4, CareerContractType.MinAccuracy, met: true));  // 1,125 * 1.5 = 1,687.5
            var result = evaluation.Tiers[0].Songs[0];
            Assert.That(result.Fans, Is.EqualTo(1688));
            Assert.That(result.ContractMet, Is.True);
            Assert.That(result.BestStars, Is.EqualTo(5));

            tier.Contract = Contract(CareerContractType.MinAccuracy, 95, bonus: 25);
            evaluation = Evaluate(career,
                Row(song, 5, CareerContractType.MinAccuracy, met: false),  // 1,500
                Row(song, 4, CareerContractType.MinAccuracy, met: true));  // 1,125 * 1.25 = 1,406.25
            result = evaluation.Tiers[0].Songs[0];
            Assert.That(result.Fans, Is.EqualTo(1500));
            Assert.That(result.ContractMet, Is.False, "the counted result did not meet it");
            Assert.That(result.ContractEverMet, Is.True);

            // A tie goes to the result that met the contract.
            tier.Contract = Contract(CareerContractType.MinAccuracy, 95, bonus: 0);
            evaluation = Evaluate(career,
                Row(song, 5, CareerContractType.MinAccuracy, met: false),
                Row(song, 5, CareerContractType.MinAccuracy, met: true));
            Assert.That(evaluation.Tiers[0].Songs[0].ContractMet, Is.True);
        }

        [Test]
        public void ChangedContractTypeDropsTheOldResult()
        {
            var tier = Tier(1);
            tier.Contract = Contract(CareerContractType.FullCombo, 0, bonus: 25);
            var career = Career(tier, FanGate());

            // Met under MinStars, content now asks for a full combo.
            var evaluation = Evaluate(career, Row(tier.Songs[0], 5, CareerContractType.MinStars, met: true));
            Assert.That(evaluation.Tiers[0].Songs[0].Fans, Is.EqualTo(1500));
            Assert.That(evaluation.Tiers[0].Songs[0].ContractMet, Is.False);

            // Rows from before contracts existed (no type) never earn a bonus.
            evaluation = Evaluate(career, Row(tier.Songs[0], 5));
            Assert.That(evaluation.Tiers[0].Songs[0].Fans, Is.EqualTo(1500));
        }

        [Test]
        public void SongWithoutContractIsUnchanged()
        {
            var tier = Tier(2);
            tier.Songs[1].Contract = Contract(CareerContractType.NoFailOff, bonus: 100);
            var career = Career(tier, FanGate());

            var evaluation = Evaluate(career, Row(tier.Songs[0], 4), Row(tier.Songs[1], 4, CareerContractType.NoFailOff, true));
            Assert.That(evaluation.Tiers[0].Songs[0].Fans, Is.EqualTo(CareerFans.SongFans(tier, 4)));
            Assert.That(evaluation.Tiers[0].Songs[0].HasContract, Is.False);
            Assert.That(evaluation.Tiers[0].Songs[1].Fans, Is.EqualTo(2 * CareerFans.SongFans(tier, 4)));
        }

        [Test]
        public void MissingAContractNeverBlocksProgress()
        {
            var first = Tier(2);
            first.Contract = Contract(CareerContractType.FullBand, 4, bonus: 50);
            var second = Tier(1, criteria: 10);
            var fanTier = Tier(1, VenueSize.Starter, UnlockType.FanCount, 3000);
            var career = Career(first, second, fanTier);

            var evaluation = Evaluate(career,
                Row(first.Songs[0], 5, CareerContractType.FullBand, false),
                Row(first.Songs[1], 5, CareerContractType.FullBand, false));

            Assert.That(evaluation.Tiers[0].Completed, Is.True);
            Assert.That(evaluation.Tiers[1].Unlocked, Is.True, "10 stars");
            Assert.That(evaluation.Tiers[2].Unlocked, Is.True, "3,000 fans without any bonus");
        }

        [Test]
        public void ContractBonusFansAddToTheFanTable()
        {
            Assert.That(CareerFans.SongFans(Tier(0, VenueSize.Starter), 5, 25), Is.EqualTo(125));
            Assert.That(CareerFans.SongFans(Tier(0, VenueSize.Large), 4, 10), Is.EqualTo(1238), "1,237.5 rounds up");
            Assert.That(CareerFans.SongFans(Tier(0, VenueSize.Small), CareerFans.GOLD_STARS, 0), Is.EqualTo(313));
            Assert.That(CareerFans.SongFans(Tier(0, VenueSize.Small), 5, -50), Is.EqualTo(250), "negative is 0");
        }

        #endregion

        #region Strikethrough (SongResult.Done)

        [Test]
        public void DoneInFanCareersMeansNoMoreFansToEarn()
        {
            var tier = Tier(3, VenueSize.Small, UnlockType.FanCount, 0);
            tier.Songs[2].Contract = Contract(CareerContractType.NoFailOff, bonus: 25);
            var starter = Tier(1, VenueSize.Starter);
            var career = Career(starter, tier);

            var evaluation = Evaluate(career,
                Row(starter.Songs[0], 5),     // a star tier in a fan career: gold still earns more
                Row(tier.Songs[0], 5),
                Row(tier.Songs[1], CareerFans.GOLD_STARS),
                Row(tier.Songs[2], CareerFans.GOLD_STARS, CareerContractType.NoFailOff, met: false));

            Assert.That(evaluation.Tiers[0].Songs[0].Done, Is.False);
            Assert.That(evaluation.Tiers[1].Songs[0].Done, Is.False, "5 stars, gold earns more");
            Assert.That(evaluation.Tiers[1].Songs[1].Done, Is.True, "gold, no contract");
            Assert.That(evaluation.Tiers[1].Songs[2].Done, Is.False, "gold, but the contract bonus is still open");

            evaluation = Evaluate(career, Row(tier.Songs[2], CareerFans.GOLD_STARS, CareerContractType.NoFailOff, met: true));
            Assert.That(evaluation.Tiers[1].Songs[2].Done, Is.True);
        }

        [Test]
        public void DoneInOtherCareersFollowsTheTierUnlockType()
        {
            var stars = Tier(2);
            var completion = Tier(1, criteria: 1);
            completion.UnlockType = UnlockType.CompletionCount;
            stars.Contract = Contract(CareerContractType.NoFailOff, bonus: 25);
            var career = Career(stars, completion);

            var evaluation = Evaluate(career, Row(stars.Songs[0], 5), Row(stars.Songs[1], 4), Row(completion.Songs[0], 1));

            Assert.That(evaluation.UsesFans, Is.False);
            Assert.That(evaluation.Tiers[0].Songs[0].Done, Is.True, "5 stars, contract irrelevant without fans");
            Assert.That(evaluation.Tiers[0].Songs[1].Done, Is.False);
            Assert.That(evaluation.Tiers[1].Songs[0].Done, Is.True, "completed");
        }

        [Test]
        public void DoneDoesNotCarryOverToAnotherSave()
        {
            // The old sticky flag lived on the shared content object; the result is per evaluation now.
            var tier = Tier(1);
            var career = Career(tier);

            Assert.That(Evaluate(career, Row(tier.Songs[0], 5)).Tiers[0].Songs[0].Done, Is.True);
            Assert.That(Evaluate(career).Tiers[0].Songs[0].Done, Is.False, "another (or a reset) save");
        }

        #endregion

        #region Completion row columns (database)

        [Test]
        public void CompletionRowStoresTheContractOutcome()
        {
            using var connection = new SQLiteConnection(":memory:");
            var careers = new CareerDatabase(connection);
            careers.Initialize();

            var saveId = careers.GetOrCreateSave(Guid.NewGuid(), 1, new List<Guid> { Alice });
            careers.CommitSongCompletion(new CareerSongCompletionCommit
            {
                CareerSaveId = saveId, CareerSongId = Guid.NewGuid(), SongChecksum = Array.Empty<byte>(),
                BandStars = 4, ContractType = CareerContractType.MinDifficulty, ContractMet = true,
                CompletedAt = DateTime.Now,
            });
            careers.CommitSongCompletion(new CareerSongCompletionCommit
            {
                CareerSaveId = saveId, CareerSongId = Guid.NewGuid(), SongChecksum = Array.Empty<byte>(),
                BandStars = 3, CompletedAt = DateTime.Now,
            });

            var rows = careers.GetSongCompletions(saveId).OrderBy(row => row.Id).ToList();
            Assert.That(rows[0].ContractType, Is.EqualTo(CareerContractType.MinDifficulty));
            Assert.That(rows[0].ContractMet, Is.True);
            Assert.That(rows[1].ContractType, Is.Null);
            Assert.That(rows[1].ContractMet, Is.False);
        }

        #endregion
    }
}
