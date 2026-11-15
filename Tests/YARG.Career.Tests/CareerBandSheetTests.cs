using NUnit.Framework;
using SQLite;
using YARG.Core;
using YARG.Core.Game;
using YARG.Scores;

namespace YARG.Career.Tests
{
    /// <summary>
    /// Band identity: rank titles from fans, the band sheet's totals, per-member records and gig history
    /// (pure <see cref="CareerBandSheet.Build"/>), the hometown match, and the thin database load.
    /// </summary>
    public class CareerBandSheetTests
    {
        private static readonly Guid Alice = Guid.NewGuid();
        private static readonly Guid Bob = Guid.NewGuid();
        private static readonly Guid Carol = Guid.NewGuid();

        #region Ranks

        [Test]
        public void RankComesFromFans()
        {
            Assert.That(CareerFans.Rank(0), Is.EqualTo(CareerRank.GarageBand));
            Assert.That(CareerFans.Rank(499), Is.EqualTo(CareerRank.GarageBand));
            Assert.That(CareerFans.Rank(500), Is.EqualTo(CareerRank.LocalAct));
            Assert.That(CareerFans.Rank(1_999), Is.EqualTo(CareerRank.LocalAct));
            Assert.That(CareerFans.Rank(2_000), Is.EqualTo(CareerRank.ClubRegulars));
            Assert.That(CareerFans.Rank(7_500), Is.EqualTo(CareerRank.TouringBand));
            Assert.That(CareerFans.Rank(20_000), Is.EqualTo(CareerRank.Headliners));
            Assert.That(CareerFans.Rank(50_000), Is.EqualTo(CareerRank.ArenaLegends));
            Assert.That(CareerFans.Rank(10_000_000), Is.EqualTo(CareerRank.ArenaLegends));
        }

        [Test]
        public void NextRankSaysHowManyFansAreMissing()
        {
            Assert.That(CareerFans.NextRank(0), Is.EqualTo((CareerRank.LocalAct, 500)));
            Assert.That(CareerFans.NextRank(450), Is.EqualTo((CareerRank.LocalAct, 50)));
            Assert.That(CareerFans.NextRank(500), Is.EqualTo((CareerRank.ClubRegulars, 1_500)));
            Assert.That(CareerFans.NextRank(49_999), Is.EqualTo((CareerRank.ArenaLegends, 1)));
            Assert.That(CareerFans.NextRank(50_000), Is.Null);
        }

        [Test]
        public void RankThresholdsRise()
        {
            var ranks = Enum.GetValues<CareerRank>();
            Assert.That(CareerFans.RankFans(ranks[0]), Is.EqualTo(0));
            for (var i = 1; i < ranks.Length; i++)
            {
                Assert.That(CareerFans.RankFans(ranks[i]), Is.GreaterThan(CareerFans.RankFans(ranks[i - 1])));
                Assert.That(CareerFans.Rank(CareerFans.RankFans(ranks[i])), Is.EqualTo(ranks[i]));
            }
        }

        #endregion

        #region Hometown

        [Test]
        public void HometownMatchesIgnoringCaseAndSpaces()
        {
            Assert.That(CareerBandSheet.IsHometown(" springfield ", "Springfield"), Is.True);
            Assert.That(CareerBandSheet.IsHometown("Springfield", "SPRINGFIELD  "), Is.True);
            Assert.That(CareerBandSheet.IsHometown("Shelbyville", "Springfield"), Is.False);
            Assert.That(CareerBandSheet.IsHometown(null, "Springfield"), Is.False);
            Assert.That(CareerBandSheet.IsHometown("Springfield", ""), Is.False);
            Assert.That(CareerBandSheet.IsHometown("  ", "  "), Is.False, "two blanks are not a match");
        }

        #endregion

        #region Sheet (pure)

        // A fan career: tier 0 Small (250 fans for 5 stars) with a MinStars 4 contract (+20%) and two
        // songs, tier 1 gated on fans. A star career with one Large tier (no fans shown).
        private static CareerPreset FanCareer()
        {
            var career = new CareerPreset(Guid.NewGuid(), "Fans", "Fan career", false);
            var first = Tier(VenueSize.Small, 2);
            first.Contract = new CareerContract { Type = CareerContractType.MinStars, Parameter = 4, BonusPercent = 20 };
            first.City = "Springfield";
            career.AddTier(first);
            var second = Tier(VenueSize.Medium, 1);
            second.UnlockType = UnlockType.FanCount;
            second.UnlockCriteria = 100_000;
            career.AddTier(second);
            return career;
        }

        private static CareerPreset StarCareer()
        {
            var career = new CareerPreset(Guid.NewGuid(), "Stars", "Star career", false);
            career.AddTier(Tier(VenueSize.Large, 2));
            return career;
        }

        private static CareerTier Tier(VenueSize venue, int songs)
        {
            var tier = new CareerTier
            {
                Id = Guid.NewGuid(),
                Name = "Tier",
                VenueSize = venue,
                Songs = new CareerSong[songs],
            };

            for (var i = 0; i < tier.Songs.Length; i++)
            {
                tier.Songs[i] = new CareerSong { Id = Guid.NewGuid(), Parent = tier };
            }

            return tier;
        }

        private static int _nextId = 1;

        private static CareerSongCompletions Row(CareerPreset career, int tier, int song, int stars, int recordId,
            DateTime at, bool gold = false, bool? met = null)
        {
            var content = career.Tiers[tier];
            return new CareerSongCompletions
            {
                Id = _nextId++,
                CareerId = career.Id,
                TierId = content.Id,
                TierIndex = tier,
                CareerSongId = content.Songs[song].Id,
                SongIndex = song,
                BandStars = stars,
                IsGold = gold,
                GameRecordId = recordId,
                CompletedAt = at,
                ContractType = met is null ? null : CareerContractType.MinStars,
                ContractMet = met ?? false,
            };
        }

        private static GameRecord Record(int id, string song = "Song")
        {
            return new GameRecord { Id = id, SongName = song, BandStars = StarAmount.Star5 };
        }

        private static PlayerScoreRecord Score(int recordId, Guid player, Instrument instrument, float percent,
            bool fc = false, int notes = 100)
        {
            var hit = (int) Math.Round(percent * notes);
            return new PlayerScoreRecord
            {
                GameRecordId = recordId,
                PlayerId = player,
                Instrument = instrument,
                NotesHit = hit,
                NotesMissed = notes - hit,
                Percent = notes == 0 ? 1f : percent,
                IsFc = fc,
            };
        }

        private static CareerBandSheet.Save Save(CareerPreset career, params CareerSongCompletions[] rows)
        {
            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.AddRange(rows);
            return new CareerBandSheet.Save { Career = career, Snapshot = snapshot };
        }

        [Test]
        public void SheetTotalsAndHistory()
        {
            var fans = FanCareer();
            var stars = StarCareer();
            var day = new DateTime(2026, 11, 15, 12, 0, 0);

            // Fan career, song 0: 3 stars missed the contract, then gold met it (replay). Song 1: 4 stars met.
            var first = Row(fans, 0, 0, 3, 1, day, met: false);
            var gold = Row(fans, 0, 0, 5, 2, day.AddMinutes(1), gold: true, met: true);
            var other = Row(fans, 0, 1, 4, 3, day.AddMinutes(2), met: true);
            // Star career: one 5-star song (no contract), from a debug grant (no game record).
            var grant = Row(stars, 0, 0, 5, 0, day.AddMinutes(3));

            var sheet = CareerBandSheet.Build(new[] { Alice },
                new[] { Save(fans, first, gold, other), Save(stars, grant), Save(StarCareer()) },
                new[] { Record(1), Record(2), Record(3) },
                Array.Empty<PlayerScoreRecord>());

            // Fans: gold 250 * 1.25 * 1.2 = 375; 4 stars 250 * 0.75 * 1.2 = 225. The star career shows none.
            Assert.That(sheet.Fans, Is.EqualTo(375 + 225));
            Assert.That(sheet.Rank, Is.EqualTo(CareerRank.LocalAct));
            Assert.That(sheet.NextRank, Is.EqualTo((CareerRank.ClubRegulars, 2_000 - 600)));

            Assert.That(sheet.CareersPlayed, Is.EqualTo(2), "a save without gigs is not a career played");
            Assert.That(sheet.GigsPlayed, Is.EqualTo(4), "every play counts, replays included");
            Assert.That(sheet.TotalStars, Is.EqualTo(5 + 4 + 5), "best stars per song, gold as 5");
            Assert.That(sheet.ContractsMet, Is.EqualTo(2));
            Assert.That(sheet.ContractGigs, Is.EqualTo(3));

            // Newest first.
            Assert.That(sheet.History.Select(gig => gig.Row), Is.EqualTo(new[] { grant, other, gold, first }));

            var history = sheet.History.ToDictionary(gig => gig.Row);
            Assert.That(history[first].Fans, Is.EqualTo(125), "3 stars, contract missed: 250 * 0.5");
            Assert.That(history[first].Counts, Is.False);
            Assert.That(history[gold].Fans, Is.EqualTo(375));
            Assert.That(history[gold].Gold, Is.True);
            Assert.That(history[gold].Counts, Is.True);
            Assert.That(history[other].Fans, Is.EqualTo(225));
            Assert.That(history[other].Counts, Is.True);
            Assert.That(history[grant].UsesFans, Is.False);
            Assert.That(history[grant].Record, Is.Null);
            Assert.That(history[gold].Record?.Id, Is.EqualTo(2));
            Assert.That(history[gold].Tier, Is.SameAs(fans.Tiers[0]));
        }

        [Test]
        public void ResultWorthMoreWithItsContractIsTheOneThatCounts()
        {
            var fans = FanCareer();
            var day = new DateTime(2026, 11, 15);

            // 5 stars missed the contract (250); 4 stars met it (225): the 5-star row counts.
            var five = Row(fans, 0, 0, 5, 1, day, met: false);
            var four = Row(fans, 0, 0, 4, 2, day.AddMinutes(1), met: true);
            var sheet = CareerBandSheet.Build(null, new[] { Save(fans, five, four) }, null, null);

            var history = sheet.History.ToDictionary(gig => gig.Row);
            Assert.That(history[five].Fans, Is.EqualTo(250));
            Assert.That(history[five].Counts, Is.True);
            Assert.That(history[four].Fans, Is.EqualTo(225));
            Assert.That(history[four].Counts, Is.False);
            Assert.That(sheet.Fans, Is.EqualTo(250));
        }

        [Test]
        public void MemberRecordsComeFromTheirScoreRows()
        {
            var fans = FanCareer();
            var day = new DateTime(2026, 11, 15);

            var rows = new[]
            {
                Row(fans, 0, 0, 5, 1, day),
                Row(fans, 0, 1, 4, 2, day.AddMinutes(1)),
                Row(fans, 0, 1, 5, 3, day.AddMinutes(2)),
                Row(fans, 0, 0, 5, 0, day.AddMinutes(3)), // grant: no score rows
            };

            var scores = new[]
            {
                Score(1, Alice, Instrument.FiveFretGuitar, 1f, fc: true),
                Score(1, Bob, Instrument.FourLaneDrums, 0.8f),
                Score(2, Alice, Instrument.FiveFretGuitar, 0.9f),
                Score(2, Carol, Instrument.Vocals, 0.5f), // sit-in, not a member
                Score(3, Alice, Instrument.FiveFretBass, 0.95f, fc: true),
                Score(3, Bob, Instrument.Vocals, 0.7f, notes: 0), // no notes: no accuracy, no full combo
                Score(99, Alice, Instrument.Keys, 0.1f), // another game record: not the band's
            };

            var nobodyId = Guid.NewGuid();
            var sheet = CareerBandSheet.Build(new[] { Alice, Bob, nobodyId },
                new[] { Save(fans, rows) }, new[] { Record(1), Record(2), Record(3) }, scores);

            Assert.That(sheet.Members, Has.Count.EqualTo(3));

            var alice = sheet.Members[0];
            Assert.That(alice.ProfileId, Is.EqualTo(Alice));
            Assert.That(alice.Gigs, Is.EqualTo(3));
            Assert.That(alice.Instruments, Is.EqualTo(new[]
            {
                (Instrument.FiveFretGuitar, 2),
                (Instrument.FiveFretBass, 1),
            }));
            Assert.That(alice.AverageAccuracy, Is.EqualTo((1f + 0.9f + 0.95f) / 3).Within(0.0001f));
            Assert.That(alice.FullCombos, Is.EqualTo(2));

            var bob = sheet.Members[1];
            Assert.That(bob.Gigs, Is.EqualTo(2));
            Assert.That(bob.Instruments, Is.EqualTo(new[]
            {
                (Instrument.FourLaneDrums, 1),
                (Instrument.Vocals, 1),
            }));
            Assert.That(bob.AverageAccuracy, Is.EqualTo(0.8f).Within(0.0001f), "the no-note gig has no accuracy");
            Assert.That(bob.FullCombos, Is.EqualTo(0));

            var nobody = sheet.Members[2];
            Assert.That(nobody.ProfileId, Is.EqualTo(nobodyId));
            Assert.That(nobody.Gigs, Is.EqualTo(0));
            Assert.That(nobody.AverageAccuracy, Is.Null);
            Assert.That(nobody.Instruments, Is.Empty);
        }

        [Test]
        public void EmptyBandHasAnEmptySheet()
        {
            var sheet = CareerBandSheet.Build(new[] { Alice }, Array.Empty<CareerBandSheet.Save>(), null, null);

            Assert.That(sheet.Fans, Is.EqualTo(0));
            Assert.That(sheet.Rank, Is.EqualTo(CareerRank.GarageBand));
            Assert.That(sheet.NextRank, Is.EqualTo((CareerRank.LocalAct, 500)));
            Assert.That(sheet.CareersPlayed, Is.EqualTo(0));
            Assert.That(sheet.GigsPlayed, Is.EqualTo(0));
            Assert.That(sheet.TotalStars, Is.EqualTo(0));
            Assert.That(sheet.ContractsMet, Is.EqualTo(0));
            Assert.That(sheet.History, Is.Empty);
            Assert.That(sheet.Members.Single().Gigs, Is.EqualTo(0));
        }

        [Test]
        public void RowsOfSongsNoLongerInTheContentStillShow()
        {
            var fans = FanCareer();
            var row = Row(fans, 0, 0, 5, 1, new DateTime(2026, 11, 15));
            row.CareerSongId = Guid.NewGuid();

            var sheet = CareerBandSheet.Build(null, new[] { Save(fans, row) }, new[] { Record(1, "Gone") }, null);

            var gig = sheet.History.Single();
            Assert.That(gig.Tier, Is.SameAs(fans.Tiers[0]));
            Assert.That(gig.Song, Is.Null);
            Assert.That(gig.Fans, Is.EqualTo(0));
            Assert.That(gig.Counts, Is.False);
            Assert.That(gig.Record.SongName, Is.EqualTo("Gone"));
        }

        #endregion

        #region Database

        private SQLiteConnection _connection;
        private CareerDatabase _careers;

        [SetUp]
        public void OpenDatabase()
        {
            _connection = new SQLiteConnection(":memory:");
            _connection.CreateTable<GameRecord>();
            _connection.CreateTable<PlayerScoreRecord>();
            _careers = new CareerDatabase(_connection);
            _careers.Initialize();
        }

        [TearDown]
        public void CloseDatabase()
        {
            _connection.Dispose();
        }

        private int Play(int saveId, CareerPreset career, int tier, int song, int stars, params PlayerScoreRecord[] players)
        {
            var record = new GameRecord { Date = DateTime.Now, SongName = $"t{tier}s{song}", BandStars = StarAmount.Star5 };
            _connection.Insert(record);
            foreach (var player in players)
            {
                player.GameRecordId = record.Id;
                _connection.Insert(player);
            }

            _careers.CommitSongCompletion(new CareerSongCompletionCommit
            {
                CareerSaveId = saveId,
                CareerId = career.Id,
                TierId = career.Tiers[tier].Id,
                TierIndex = tier,
                CareerSongId = career.Tiers[tier].Songs[song].Id,
                SongIndex = song,
                SongChecksum = Array.Empty<byte>(),
                GameRecordId = record.Id,
                BandStars = stars,
                CompletedAt = DateTime.Now,
            });
            return record.Id;
        }

        [Test]
        public void LoadUsesTheBandsActiveSavesOnly()
        {
            var fans = FanCareer();
            var stars = StarCareer();
            var band = _careers.CreateBand("The Testers", "Springfield", "Rock", new[] { Alice, Bob });

            var fanSave = _careers.GetOrCreateSave(fans.Id, fans.Version, band.Id);
            var starSave = _careers.GetOrCreateSave(stars.Id, stars.Version, band.Id);
            Play(fanSave, fans, 0, 0, 5, Score(0, Alice, Instrument.FiveFretGuitar, 0.9f), Score(0, Bob, Instrument.FourLaneDrums, 0.8f));
            Play(fanSave, fans, 0, 0, 4, Score(0, Alice, Instrument.FiveFretGuitar, 0.7f));
            Play(starSave, stars, 0, 1, 3, Score(0, Bob, Instrument.Vocals, 0.6f));

            // Alice on her own (profile-set save) and another band: not this band's.
            var solo = _careers.GetOrCreateSave(fans.Id, fans.Version, new[] { Alice });
            Play(solo, fans, 0, 1, 5, Score(0, Alice, Instrument.Keys, 0.1f));
            var other = _careers.CreateBand("Others", null, null, new[] { Alice });
            Play(_careers.GetOrCreateSave(fans.Id, fans.Version, other.Id), fans, 0, 1, 5,
                Score(0, Alice, Instrument.Keys, 0.1f));

            var all = new[] { fans, stars };
            var sheet = CareerBandSheet.Load(_careers, band.Id, all);

            Assert.That(sheet.GigsPlayed, Is.EqualTo(3));
            Assert.That(sheet.CareersPlayed, Is.EqualTo(2));
            Assert.That(sheet.TotalStars, Is.EqualTo(5 + 3));
            Assert.That(sheet.Fans, Is.EqualTo(250));
            Assert.That(sheet.History.All(gig => gig.Record is not null), Is.True);

            var alice = sheet.Members.Single(member => member.ProfileId == Alice);
            Assert.That(alice.Gigs, Is.EqualTo(2));
            Assert.That(alice.Instruments, Is.EqualTo(new[] { (Instrument.FiveFretGuitar, 2) }));
            Assert.That(alice.AverageAccuracy, Is.EqualTo(0.8f).Within(0.0001f));
            var bob = sheet.Members.Single(member => member.ProfileId == Bob);
            Assert.That(bob.Gigs, Is.EqualTo(2));
            Assert.That(bob.AverageAccuracy, Is.EqualTo(0.7f).Within(0.0001f));

            // A reset career leaves the sheet; an uninstalled career is skipped.
            _careers.SoftDeleteSave(fanSave);
            sheet = CareerBandSheet.Load(_careers, band.Id, all);
            Assert.That(sheet.GigsPlayed, Is.EqualTo(1));
            Assert.That(sheet.Fans, Is.EqualTo(0));
            Assert.That(CareerBandSheet.Load(_careers, band.Id, new[] { fans }).GigsPlayed, Is.EqualTo(0));
        }

        [Test]
        public void HometownAndGenreCanBeChanged()
        {
            var band = _careers.CreateBand("The Testers", "Springfield", "Rock", new[] { Alice });

            _careers.SetBandHometown(band.Id, "Shelbyville");
            _careers.SetBandGenre(band.Id, "");

            var loaded = _careers.GetBand(band.Id);
            Assert.That(loaded.Hometown, Is.EqualTo("Shelbyville"));
            Assert.That(loaded.Genre, Is.EqualTo(""));
            Assert.That(loaded.Name, Is.EqualTo("The Testers"));
        }

        [Test]
        public void NewBandLoadsAnEmptySheet()
        {
            var band = _careers.CreateBand("Fresh", null, null, new[] { Alice });

            var sheet = CareerBandSheet.Load(_careers, band.Id, new[] { FanCareer() });

            Assert.That(sheet.GigsPlayed, Is.EqualTo(0));
            Assert.That(sheet.Members.Single().Gigs, Is.EqualTo(0));
        }

        #endregion
    }
}
