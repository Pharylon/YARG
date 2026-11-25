using NUnit.Framework;
using SQLite;
using YARG.Scores;

namespace YARG.Career.Tests
{
    /// <summary>
    /// The band credit rule and band fan totals (pure), and band save resolution against a real
    /// in-memory SQLite database (the game's own sqlite-net code and native library).
    /// </summary>
    public class CareerBandsTests
    {
        private static readonly Guid Alice = Guid.NewGuid();
        private static readonly Guid Bob = Guid.NewGuid();
        private static readonly Guid Carol = Guid.NewGuid();

        #region Credit rule

        [Test]
        public void NoBandCreditsTheExactPlayerSet()
        {
            var credit = CareerCredit.Decide(null, null, new[] { Alice, Bob, Alice }, false);

            Assert.That(credit.Counts, Is.True);
            Assert.That(credit.BandId, Is.Null);
            Assert.That(credit.ProfileIds, Is.EqualTo(new[] { Alice, Bob }));
        }

        [Test]
        public void BandCountsWhenOneMemberPlaysAndOthersMaySitIn()
        {
            var band = Guid.NewGuid();
            var credit = CareerCredit.Decide(band, new[] { Alice, Bob }, new[] { Alice, Carol }, false);

            Assert.That(credit.Counts, Is.True);
            Assert.That(credit.BandId, Is.EqualTo(band));
            Assert.That(credit.MembersPlaying, Is.EqualTo(new[] { Alice }));
            Assert.That(credit.ProfileIds, Is.Empty, "a band save is not owned by a profile set");
        }

        [Test]
        public void BandDoesNotCountWithoutAMember()
        {
            var credit = CareerCredit.Decide(Guid.NewGuid(), new[] { Alice }, new[] { Bob, Carol }, false);

            Assert.That(credit.Counts, Is.False);
            Assert.That(credit.Block, Is.EqualTo(CareerCreditBlock.NoMemberPlaying));
        }

        [Test]
        public void BotsBlockCreditWithOrWithoutBand()
        {
            Assert.That(CareerCredit.Decide(null, null, new[] { Alice }, true).Block,
                Is.EqualTo(CareerCreditBlock.BotPlaying));
            Assert.That(CareerCredit.Decide(Guid.NewGuid(), new[] { Alice }, new[] { Alice }, true).Block,
                Is.EqualTo(CareerCreditBlock.BotPlaying));
        }

        [Test]
        public void NoHumansOrMissingBandDoNotCount()
        {
            Assert.That(CareerCredit.Decide(null, null, Array.Empty<Guid>(), false).Block,
                Is.EqualTo(CareerCreditBlock.NoPlayers));
            Assert.That(CareerCredit.Decide(Guid.NewGuid(), null, new[] { Alice }, false).Block,
                Is.EqualTo(CareerCreditBlock.BandMissing));
        }

        [Test]
        public void OnePersonBandIsFine()
        {
            var band = Guid.NewGuid();
            var credit = CareerCredit.Decide(band, new[] { Alice }, new[] { Alice }, false);

            Assert.That(credit.Counts, Is.True);
            Assert.That(credit.BandId, Is.EqualTo(band));
        }

        #endregion

        #region Band fans

        private static CareerPreset FanCareer(out CareerSong song)
        {
            var career = new CareerPreset(Guid.NewGuid(), "Fans", "Fan career", false);
            var first = Tier(VenueSize.Small);
            career.AddTier(first);
            var second = Tier(VenueSize.Medium);
            second.UnlockType = UnlockType.FanCount;
            second.UnlockCriteria = 1000;
            career.AddTier(second);
            song = first.Songs[0];
            return career;
        }

        private static CareerPreset StarCareer(out CareerSong song)
        {
            var career = new CareerPreset(Guid.NewGuid(), "Stars", "Star career", false);
            var tier = Tier(VenueSize.Large);
            career.AddTier(tier);
            song = tier.Songs[0];
            return career;
        }

        private static CareerTier Tier(VenueSize venue)
        {
            var tier = new CareerTier
            {
                Id = Guid.NewGuid(),
                Name = "Tier",
                VenueSize = venue,
                Songs = new CareerSong[2],
            };

            for (var i = 0; i < tier.Songs.Length; i++)
            {
                tier.Songs[i] = new CareerSong { Id = Guid.NewGuid(), Parent = tier };
            }

            return tier;
        }

        private static CareerEvaluation Evaluate(CareerPreset career, CareerSong song, int stars)
        {
            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.Add(new CareerSongCompletions { CareerSongId = song.Id, BandStars = stars });
            return CareerEvaluation.Evaluate(career, snapshot);
        }

        [Test]
        public void BandFansSumOverEveryCareer()
        {
            var fanCareer = FanCareer(out var fanSong);
            var otherFanCareer = FanCareer(out var otherFanSong);
            var starCareer = StarCareer(out var starSong);

            var total = CareerFans.BandFans(new[]
            {
                Evaluate(fanCareer, fanSong, 5),           // Small venue, 5 stars: 250
                Evaluate(otherFanCareer, otherFanSong, 3), // Small venue, 3 stars: 125
                Evaluate(starCareer, starSong, 5),         // no fan-count unlocks, still counted: 1,500
                null,
            });

            Assert.That(total, Is.EqualTo(250 + 125 + 1500));
            Assert.That(CareerFans.BandFans(Array.Empty<CareerEvaluation>()), Is.EqualTo(0));
        }

        #endregion

        #region Save resolution (database)

        private SQLiteConnection _connection;
        private CareerDatabase _careers;

        [SetUp]
        public void OpenDatabase()
        {
            _connection = new SQLiteConnection(":memory:");
            _careers = new CareerDatabase(_connection);
            _careers.Initialize();
        }

        [TearDown]
        public void CloseDatabase()
        {
            _connection.Dispose();
        }

        private static CareerPreset Career() => new(Guid.NewGuid(), "Career", "A career", false);

        private void Complete(int saveId, CareerPreset career, CareerSong song, int stars)
        {
            _careers.CommitSongCompletion(new CareerSongCompletionCommit
            {
                CareerSaveId = saveId,
                CareerId = career.Id,
                CareerSongId = song.Id,
                SongChecksum = Array.Empty<byte>(),
                BandStars = stars,
                CompletedAt = DateTime.Now,
            });
        }

        [Test]
        public void BandSaveAndProfileSetSaveAreSeparate()
        {
            var career = Career();
            var band = _careers.CreateBand("The Testers", "Austin", "Rock", new[] { Alice });

            var bandCredit = CareerBands.Credit(_careers, band.Id, new[] { Alice }, false);
            var soloCredit = CareerBands.Credit(_careers, null, new[] { Alice }, false);

            Assert.That(CareerBands.FindSaveId(_careers, career, bandCredit), Is.EqualTo(0));

            var bandSave = CareerBands.GetOrCreateSaveId(_careers, career, bandCredit);
            Assert.That(CareerBands.FindSaveId(_careers, career, soloCredit), Is.EqualTo(0),
                "the band's save must not be found as Alice's own save");

            var soloSave = CareerBands.GetOrCreateSaveId(_careers, career, soloCredit);
            Assert.That(soloSave, Is.Not.EqualTo(bandSave));
            Assert.That(CareerBands.FindSaveId(_careers, career, bandCredit), Is.EqualTo(bandSave));
            Assert.That(CareerBands.FindSaveId(_careers, career, soloCredit), Is.EqualTo(soloSave));

            // Same players, same career: both resolve to the same save every time.
            Assert.That(CareerBands.GetOrCreateSaveId(_careers, career, bandCredit), Is.EqualTo(bandSave));
            Assert.That(CareerBands.GetOrCreateSaveId(_careers, career, soloCredit), Is.EqualTo(soloSave));
        }

        [Test]
        public void SitInDoesNotChangeTheBandSave()
        {
            var career = Career();
            var band = _careers.CreateBand("The Testers", null, null, new[] { Alice, Bob });

            var alone = CareerBands.GetOrCreateSaveId(_careers, career,
                CareerBands.Credit(_careers, band.Id, new[] { Alice }, false));
            var withGuest = CareerBands.GetOrCreateSaveId(_careers, career,
                CareerBands.Credit(_careers, band.Id, new[] { Bob, Carol }, false));

            Assert.That(withGuest, Is.EqualTo(alone));
        }

        [Test]
        public void TwoBandsHaveSeparateProgressOnTheSameCareer()
        {
            var career = Career();
            var first = _careers.CreateBand("First", null, null, new[] { Alice });
            var second = _careers.CreateBand("Second", null, null, new[] { Alice });

            var firstSave = _careers.GetOrCreateSave(career.Id, career.Version, first.Id);
            var secondSave = _careers.GetOrCreateSave(career.Id, career.Version, second.Id);

            Assert.That(secondSave, Is.Not.EqualTo(firstSave));
            Assert.That(_careers.GetBandsForProfile(Alice).Select(b => b.Name), Is.EqualTo(new[] { "First", "Second" }));
        }

        [Test]
        public void ResetGivesTheBandAFreshSave()
        {
            var career = Career();
            var band = _careers.CreateBand("The Testers", null, null, new[] { Alice });

            var save = _careers.GetOrCreateSave(career.Id, career.Version, band.Id);
            _careers.SoftDeleteSave(save);
            var fresh = _careers.GetOrCreateSave(career.Id, career.Version, band.Id);

            Assert.That(fresh, Is.Not.EqualTo(save));
            Assert.That(_careers.GetActiveSavesForBand(band.Id).Select(s => s.Id), Is.EqualTo(new[] { fresh }));
        }

        [Test]
        public void RenameMembersAndSoftDelete()
        {
            var band = _careers.CreateBand("Old Name", "Here", "Jazz", new[] { Alice });

            _careers.RenameBand(band.Id, "New Name");
            _careers.AddBandMember(band.Id, Bob);
            _careers.AddBandMember(band.Id, Bob);
            _careers.RemoveBandMember(band.Id, Alice);

            var loaded = _careers.GetBand(band.Id);
            Assert.That(loaded.Name, Is.EqualTo("New Name"));
            Assert.That(loaded.Hometown, Is.EqualTo("Here"));
            Assert.That(_careers.GetBandMembers(band.Id), Is.EqualTo(new[] { Bob }));
            Assert.That(_careers.GetBandsForProfile(Alice), Is.Empty);

            _careers.SoftDeleteBand(band.Id);
            Assert.That(_careers.GetBand(band.Id), Is.Null);
            Assert.That(_careers.GetBands(), Is.Empty);
            Assert.That(_careers.GetBandsForProfile(Bob), Is.Empty);
            Assert.That(CareerBands.Credit(_careers, band.Id, new[] { Bob }, false).Block,
                Is.EqualTo(CareerCreditBlock.BandMissing));
        }

        [Test]
        public void AdoptedSaveMovesFromThePlayersToTheBand()
        {
            var career = Career();
            var soloSave = _careers.GetOrCreateSave(career.Id, career.Version, new[] { Alice, Bob });
            var band = _careers.CreateBand("The Testers", null, null, new[] { Alice, Bob });

            Assert.That(_careers.AdoptSave(soloSave, band.Id), Is.True);
            Assert.That(_careers.FindActiveSaveForBand(career.Id, band.Id)?.Id, Is.EqualTo(soloSave));
            Assert.That(_careers.FindActiveSaveForProfiles(career.Id, new[] { Alice, Bob }), Is.Null);
            Assert.That(_careers.AdoptSave(soloSave, band.Id), Is.False, "already a band save");
        }

        [Test]
        public void BandFansComeFromTheBandsActiveSaves()
        {
            var fanCareer = FanCareer(out var fanSong);
            var starCareer = StarCareer(out var starSong);
            var band = _careers.CreateBand("The Testers", null, null, new[] { Alice });

            Complete(_careers.GetOrCreateSave(fanCareer.Id, fanCareer.Version, band.Id), fanCareer, fanSong, 5);
            Complete(_careers.GetOrCreateSave(starCareer.Id, starCareer.Version, band.Id), starCareer, starSong, 5);
            // Alice on her own: not the band's fans.
            Complete(_careers.GetOrCreateSave(fanCareer.Id, fanCareer.Version, new[] { Alice }), fanCareer, fanSong, 5);

            var careers = new[] { fanCareer, starCareer };
            Assert.That(CareerBands.BandFans(_careers, band.Id, careers), Is.EqualTo(250 + 1500));

            // A reset save no longer counts.
            _careers.SoftDeleteSave(_careers.FindActiveSaveForBand(fanCareer.Id, band.Id).Id);
            Assert.That(CareerBands.BandFans(_careers, band.Id, careers), Is.EqualTo(1500));
        }

        #endregion
    }
}
