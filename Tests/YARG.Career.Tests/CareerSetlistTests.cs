using Newtonsoft.Json;
using NUnit.Framework;
using YARG.Core.Game;
using YARG.Scores;

namespace YARG.Career.Tests
{
    /// <summary>
    /// Phase B gig conditions: gigs played as a show, and the setlist rules that go with them
    /// (<see cref="CareerSetlists"/>), plus their content model and validation.
    /// </summary>
    public class CareerSetlistTests
    {
        private static CareerTier Tier(int songs, CareerCompletionMode mode = CareerCompletionMode.SingleSong,
            UnlockType unlockType = UnlockType.FanCount, int criteria = 0, VenueSize venue = VenueSize.Starter)
        {
            var tier = new CareerTier
            {
                Id = Guid.NewGuid(),
                Name = "Tier",
                UnlockType = unlockType,
                UnlockCriteria = criteria,
                VenueSize = venue,
                CompletionMode = mode,
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

        private static CareerSongCompletions Completion(CareerSong song, int stars,
            CareerCompletionSource source = CareerCompletionSource.FullTierPlaylist)
        {
            return new CareerSongCompletions
            {
                CareerSongId = song.Id,
                BandStars = Math.Min(stars, 5),
                IsGold = stars >= CareerFans.GOLD_STARS,
                Source = source,
            };
        }

        private static CareerEvaluation Evaluate(CareerPreset career, params CareerSongCompletions[] rows)
        {
            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.AddRange(rows);
            return CareerEvaluation.Evaluate(career, snapshot);
        }

        [Test]
        public void OnlyFullTierPlaylistIsPlayedAsAShow()
        {
            Assert.That(CareerSetlists.IsShow(Tier(3, CareerCompletionMode.FullTierPlaylist)), Is.True);
            Assert.That(CareerSetlists.IsShow(Tier(3)), Is.False);
            // Not supported: played song by song (and validation warns).
            Assert.That(CareerSetlists.IsShow(Tier(3, CareerCompletionMode.FullCareerPlaylist)), Is.False);
            Assert.That(CareerSetlists.IsShow(null), Is.False);
        }

        [Test]
        public void AFullShowPlaysEverySongInOrder()
        {
            var tier = Tier(4, CareerCompletionMode.FullTierPlaylist);
            Assert.That(CareerSetlists.ShowSongIndexes(tier), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(CareerSetlists.ShowSongIndexes(Tier(0, CareerCompletionMode.FullTierPlaylist)), Is.Empty);
        }

        [Test]
        public void ShowSongsCountLikeSingleSongs()
        {
            // Every song of a show is its own completion row; a show left early keeps the songs it finished.
            var show = Tier(3, CareerCompletionMode.FullTierPlaylist);
            var next = Tier(1, criteria: 150);
            var career = Career(show, next);

            var partial = Evaluate(career, Completion(show.Songs[0], 5), Completion(show.Songs[1], 4));
            Assert.That(partial.Tiers[0].Songs.Count(song => song.Completed), Is.EqualTo(2));
            Assert.That(partial.Tiers[0].Completed, Is.False);
            Assert.That(partial.TotalFans, Is.EqualTo(100 + 75));
            Assert.That(partial.Tiers[1].Unlocked, Is.True);

            var full = Evaluate(career, Completion(show.Songs[0], 5), Completion(show.Songs[1], 4),
                Completion(show.Songs[2], 3));
            Assert.That(full.Tiers[0].Completed, Is.True);
        }

        [Test]
        public void CompletionModeJsonRoundTripAndOldContent()
        {
            const string old = @"{ ""Name"": ""Old"", ""Id"": ""9a3c1c43-6ad5-4c4b-9f05-0c1a3a0f3b11"",
                ""Songs"": [], ""UnlockType"": ""StarCount"", ""UnlockCriteria"": 0 }";
            var oldTier = JsonConvert.DeserializeObject<CareerTier>(old);
            Assert.That(oldTier.CompletionMode, Is.EqualTo(CareerCompletionMode.SingleSong));
            Assert.That(CareerSetlists.IsShow(oldTier), Is.False);

            var tier = Tier(2, CareerCompletionMode.FullTierPlaylist);
            var json = JsonConvert.SerializeObject(tier);
            Assert.That(json, Does.Contain(@"""CompletionMode"":""FullTierPlaylist"""));
            Assert.That(JsonConvert.DeserializeObject<CareerTier>(json).CompletionMode,
                Is.EqualTo(CareerCompletionMode.FullTierPlaylist));
            Assert.That(new CareerTier(tier).CompletionMode, Is.EqualTo(CareerCompletionMode.FullTierPlaylist));
        }

        [Test]
        public void CareerPlaylistIsReportedAsUnsupported()
        {
            var career = Career(Tier(2, CareerCompletionMode.FullTierPlaylist), Tier(2, CareerCompletionMode.FullCareerPlaylist));
            var issues = CareerValidation.Validate(career);
            Assert.That(issues.Select(issue => (issue.Type, issue.TierIndex)),
                Is.EqualTo(new[] { (CareerIssueType.CareerPlaylistUnsupported, 1) }));
        }

        // ---- Gig rules (forced modifiers, minimum speed) ----

        [Test]
        public void ForcedModifiersAddToThePlayersOwnWhereTheInstrumentAllowsThem()
        {
            var (guitar, _) = YARG.Core.GameMode.FiveFretGuitar.PossibleModifiers(YARG.Core.Instrument.FiveFretGuitar);
            var (drums, _) = YARG.Core.GameMode.FourLaneDrums.PossibleModifiers(YARG.Core.Instrument.FourLaneDrums);

            var forced = Modifier.AllStrums | Modifier.NoKicks;
            Assert.That(CareerGigRules.PlayerModifiers(Modifier.None, forced, guitar), Is.EqualTo(Modifier.AllStrums));
            Assert.That(CareerGigRules.PlayerModifiers(Modifier.NoDynamics, forced, drums),
                Is.EqualTo(Modifier.NoDynamics | Modifier.NoKicks));
            // A forced modifier replaces own ones it conflicts with, as picking it in the menu would.
            Assert.That(CareerGigRules.PlayerModifiers(Modifier.AllHopos | Modifier.RangeCompress, Modifier.AllStrums, guitar),
                Is.EqualTo(Modifier.AllStrums | Modifier.RangeCompress));
            Assert.That(CareerGigRules.PlayerModifiers(Modifier.AllHopos, Modifier.None, guitar), Is.EqualTo(Modifier.AllHopos));
        }

        [Test]
        public void MinimumSongSpeedOnlyRaisesTheSpeed()
        {
            var tier = Tier(1);
            Assert.That(CareerGigRules.SongSpeed(tier, 1f), Is.EqualTo(1f));
            Assert.That(CareerGigRules.HasRules(tier), Is.False);

            tier.MinSongSpeed = 125;
            Assert.That(CareerGigRules.HasRules(tier), Is.True);
            Assert.That(CareerGigRules.SongSpeed(tier, 1f), Is.EqualTo(1.25f));
            Assert.That(CareerGigRules.SongSpeed(tier, 2f), Is.EqualTo(2f));

            // Below 100% no score would be saved: ignored (and validation warns).
            tier.MinSongSpeed = 80;
            Assert.That(CareerGigRules.SongSpeed(tier, 1f), Is.EqualTo(1f));
            Assert.That(CareerValidation.Validate(Career(tier)).Single().Type, Is.EqualTo(CareerIssueType.SongSpeedOutOfRange));
        }

        // ---- Make your own setlist ----

        [Test]
        public void APickedSetCompletesTheTierAfterThatManySongs()
        {
            var tier = Tier(4, CareerCompletionMode.FullTierPlaylist);
            tier.SetlistSize = 2;
            var career = Career(tier, Tier(1, unlockType: UnlockType.CompletionCount, criteria: 2));

            Assert.That(CareerSetlists.PicksSongs(tier), Is.True);
            Assert.That(CareerSetlists.RequiredSongs(tier), Is.EqualTo(2));

            var one = Evaluate(career, Completion(tier.Songs[3], 3));
            Assert.That(one.Tiers[0].Completed, Is.False);

            var two = Evaluate(career, Completion(tier.Songs[3], 3), Completion(tier.Songs[1], 4));
            Assert.That(two.Tiers[0].Completed, Is.True);
            // Completion-count unlocks still count completed songs, as before.
            Assert.That(two.Tiers[1].Unlocked, Is.True);
            // Playing more of the pool still earns fans: each song counts on its own.
            var three = Evaluate(career, Completion(tier.Songs[3], 3), Completion(tier.Songs[1], 4), Completion(tier.Songs[0], 5));
            Assert.That(three.TotalFans, Is.EqualTo(50 + 75 + 100));
        }

        [Test]
        public void PicksMustBeTheRightNumberOfDifferentSetSongs()
        {
            var tier = Tier(4, CareerCompletionMode.FullTierPlaylist);
            tier.SetlistSize = 2;
            tier.Songs[3].IsEncore = true;

            Assert.That(CareerSetlists.ShowSongIndexes(tier, new[] { 2, 0 }), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(CareerSetlists.ShowSongIndexes(tier, new[] { 0 }), Is.Null);
            Assert.That(CareerSetlists.ShowSongIndexes(tier, new[] { 0, 0 }), Is.Null);
            // An encore is never picked.
            Assert.That(CareerSetlists.ShowSongIndexes(tier, new[] { 0, 3 }), Is.Null);
            Assert.That(CareerSetlists.ShowSongIndexes(tier, new[] { 0, 7 }), Is.Null);
        }

        [Test]
        public void SetlistSizeOfTheWholeSetOrMoreIsNoPick()
        {
            var tier = Tier(3, CareerCompletionMode.FullTierPlaylist);
            tier.SetlistSize = 3;
            Assert.That(CareerSetlists.PicksSongs(tier), Is.False);
            Assert.That(CareerValidation.Validate(Career(tier)), Is.Empty);

            tier.SetlistSize = 5;
            Assert.That(CareerSetlists.RequiredSongs(tier), Is.EqualTo(3));
            var issue = CareerValidation.Validate(Career(tier)).Single();
            Assert.That(issue.Type, Is.EqualTo(CareerIssueType.SetlistSizeOutOfRange));
            Assert.That(issue.Available, Is.EqualTo(3));

            tier.SetlistSize = 0;
            Assert.That(CareerValidation.Validate(Career(tier)).Single().Type, Is.EqualTo(CareerIssueType.SetlistSizeOutOfRange));
        }

        // ---- Encore ----

        [Test]
        public void TheEncoreIsEarnedByTheSetsAverageStars()
        {
            var tier = Tier(4, CareerCompletionMode.FullTierPlaylist);
            tier.Songs[3].IsEncore = true;
            tier.EncoreMinStars = 4f;

            Assert.That(CareerSetlists.SetSongIndexes(tier), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(CareerSetlists.ShowSongIndexes(tier), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(CareerSetlists.EncoreSongIndexes(tier), Is.EqualTo(new[] { 3 }));
            Assert.That(CareerSetlists.HasEncore(tier), Is.True);

            Assert.That(CareerSetlists.EncoreEarned(tier, new[] { 4, 4, 4 }), Is.True);
            Assert.That(CareerSetlists.EncoreEarned(tier, new[] { 5, 5, 2 }), Is.True);
            Assert.That(CareerSetlists.EncoreEarned(tier, new[] { 5, 4, 2 }), Is.False);
            // A skipped or unsaved song counts as 0; gold counts as 5.
            Assert.That(CareerSetlists.EncoreEarned(tier, new[] { 5, 5, 0 }), Is.False);
            Assert.That(CareerSetlists.EncoreEarned(tier, new[] { 6, 6, 0 }), Is.False);
            Assert.That(CareerSetlists.EncoreEarned(tier, Array.Empty<int>()), Is.False);

            // Not a show: no encore.
            tier.CompletionMode = CareerCompletionMode.SingleSong;
            Assert.That(CareerSetlists.EncoreEarned(tier, new[] { 5, 5, 5 }), Is.False);
        }

        [Test]
        public void EncoresAreHiddenUntilPlayedAndNeverNeededToCompleteTheTier()
        {
            var tier = Tier(3, CareerCompletionMode.FullTierPlaylist);
            tier.Songs[2].IsEncore = true;
            tier.EncoreMinStars = 4f;
            var career = Career(tier);

            Assert.That(CareerSetlists.IsHidden(tier, tier.Songs[2], false), Is.True);
            Assert.That(CareerSetlists.IsHidden(tier, tier.Songs[2], true), Is.False);
            Assert.That(CareerSetlists.IsHidden(tier, tier.Songs[0], false), Is.False);

            var set = Evaluate(career, Completion(tier.Songs[0], 4), Completion(tier.Songs[1], 4));
            Assert.That(set.Tiers[0].Completed, Is.True);

            // The encore's fans count like any song's.
            var withEncore = Evaluate(career, Completion(tier.Songs[0], 4), Completion(tier.Songs[1], 4),
                Completion(tier.Songs[2], 5));
            Assert.That(withEncore.TotalFans, Is.EqualTo(75 + 75 + 100));
        }

        [Test]
        public void EncoresThatCanNeverBePlayedAreReportedAndEarnNoFansForReachability()
        {
            var tier = Tier(2);
            tier.Songs[1].IsEncore = true;
            var next = Tier(1, criteria: 200);
            var career = Career(tier, next);

            var issues = CareerValidation.Validate(career);
            Assert.That(issues.Select(issue => issue.Type), Is.EquivalentTo(new[]
            {
                CareerIssueType.EncoreNeverPlayed,
                // Only the set song can earn fans: 125 at gold is not 200.
                CareerIssueType.UnreachableTier,
            }));
            Assert.That(CareerValidation.MaxTierFans(tier), Is.EqualTo(125));

            tier.CompletionMode = CareerCompletionMode.FullTierPlaylist;
            tier.EncoreMinStars = 4.5f;
            Assert.That(CareerValidation.Validate(career), Is.Empty);
            Assert.That(CareerValidation.MaxTierFans(tier), Is.EqualTo(250));

            tier.EncoreMinStars = 6f;
            Assert.That(CareerValidation.Validate(career).Select(issue => issue.Type),
                Does.Contain(CareerIssueType.EncoreBarInvalid).And.Contain(CareerIssueType.EncoreNeverPlayed));

            var noSong = Tier(2, CareerCompletionMode.FullTierPlaylist);
            noSong.EncoreMinStars = 3f;
            Assert.That(CareerValidation.Validate(Career(noSong)).Single().Type, Is.EqualTo(CareerIssueType.EncoreBarInvalid));
        }

        // ---- Mystery setlist ----

        [Test]
        public void MysterySongsAreHiddenUntilCompleted()
        {
            var tier = Tier(2, CareerCompletionMode.FullTierPlaylist);
            Assert.That(CareerSetlists.IsHidden(tier, tier.Songs[0], false), Is.False);

            tier.Mystery = true;
            Assert.That(CareerSetlists.IsHidden(tier, tier.Songs[0], false), Is.True);
            Assert.That(CareerSetlists.IsHidden(tier, tier.Songs[0], true), Is.False);
        }

        // ---- Content model ----

        [Test]
        public void PhaseBFieldsJsonRoundTripCopyAndOldContent()
        {
            const string old = @"{ ""Name"": ""Old"", ""Id"": ""9a3c1c43-6ad5-4c4b-9f05-0c1a3a0f3b11"",
                ""Songs"": [ { ""Id"": ""2f0c9a4e-5b1d-4c7a-8e3f-1a2b3c4d5e6f"", ""Identifier"": ""SongId"" } ],
                ""UnlockType"": ""StarCount"", ""UnlockCriteria"": 0 }";
            var oldTier = JsonConvert.DeserializeObject<CareerTier>(old);
            Assert.That(oldTier.ForcedModifiers, Is.Null);
            Assert.That(oldTier.MinSongSpeed, Is.Null);
            Assert.That(oldTier.Mystery, Is.False);
            Assert.That(oldTier.SetlistSize, Is.Null);
            Assert.That(oldTier.EncoreMinStars, Is.Null);
            Assert.That(oldTier.Songs[0].IsEncore, Is.False);

            // Unset fields stay out of saved content.
            var oldJson = JsonConvert.SerializeObject(oldTier);
            foreach (var field in new[] { "ForcedModifiers", "MinSongSpeed", "Mystery", "SetlistSize", "EncoreMinStars", "IsEncore" })
            {
                Assert.That(oldJson, Does.Not.Contain(field));
            }

            var tier = Tier(3, CareerCompletionMode.FullTierPlaylist);
            tier.ForcedModifiers = Modifier.AllStrums | Modifier.NoKicks;
            tier.MinSongSpeed = 125;
            tier.Mystery = true;
            tier.SetlistSize = 2;
            tier.EncoreMinStars = 4.5f;
            tier.Songs[2].IsEncore = true;

            var json = JsonConvert.SerializeObject(tier);
            Assert.That(json, Does.Contain(@"""ForcedModifiers"":""AllStrums, NoKicks"""));

            var loaded = JsonConvert.DeserializeObject<CareerTier>(json);
            foreach (var copy in new[] { loaded, new CareerTier(loaded) })
            {
                Assert.That(copy.ForcedModifiers, Is.EqualTo(Modifier.AllStrums | Modifier.NoKicks));
                Assert.That(copy.MinSongSpeed, Is.EqualTo(125));
                Assert.That(copy.Mystery, Is.True);
                Assert.That(copy.SetlistSize, Is.EqualTo(2));
                Assert.That(copy.EncoreMinStars, Is.EqualTo(4.5f));
                Assert.That(copy.Songs[2].IsEncore, Is.True);
                Assert.That(copy.Songs[0].IsEncore, Is.False);
            }
        }
    }
}
