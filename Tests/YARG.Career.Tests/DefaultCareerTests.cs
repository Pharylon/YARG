using NUnit.Framework;
using YARG.Scores;

namespace YARG.Career.Tests
{
    /// <summary>
    /// The shipped careers. "YARG Setlist Headliner" (the fan-count career) is tuned so that a player
    /// averaging 4 stars reaches the final tier without contracts, a player averaging 3 stars reaches it
    /// only by playing most songs, and every tier can be reached. This plays it through the real
    /// <see cref="CareerEvaluation"/>, one simulated result at a time. The original two careers are checked
    /// to be what they were before the third was added.
    /// </summary>
    public class DefaultCareerTests
    {
        private static CareerPreset Headliner => CareerPreset.YargSetlistHeadliner;

        private sealed class Run
        {
            public readonly List<CareerSongCompletions> Rows = new();
            public readonly HashSet<Guid> Played = new();
            public CareerEvaluation Evaluation;
            // Tier index -> songs played when it opened.
            public readonly Dictionary<int, int> OpenedAfter = new();
            // Every set of tiers that opened with the same result.
            public readonly List<List<int>> OpenedTogether = new();
        }

        private static CareerEvaluation Evaluate(CareerPreset career, IEnumerable<CareerSongCompletions> rows)
        {
            var snapshot = new CareerProgressSnapshot { CareerSaveId = 1 };
            snapshot.Completions.AddRange(rows);
            return CareerEvaluation.Evaluate(career, snapshot);
        }

        /// <summary>
        /// Plays songs one at a time at <paramref name="stars"/>, no contracts. With
        /// <paramref name="greedy"/>, always the open song worth the most fans (the fewest songs to get
        /// anywhere); otherwise the next open song in list order. Stops when <paramref name="until"/> is open
        /// (if given), or when no open song is left unplayed.
        /// </summary>
        private static Run Play(CareerPreset career, int stars, bool greedy, int? until = null)
        {
            var run = new Run();
            run.Evaluation = Evaluate(career, run.Rows);
            for (var i = 0; i < career.Tiers.Count; i++)
            {
                if (run.Evaluation.Tiers[i].Unlocked)
                {
                    run.OpenedAfter[i] = 0;
                }
            }

            while (until is null || !run.Evaluation.Tiers[until.Value].Unlocked)
            {
                var candidates = career.Tiers
                    .Select((tier, index) => (tier, index))
                    .Where(t => run.Evaluation.Tiers[t.index].Unlocked && t.index != until)
                    .SelectMany(t => t.tier.Songs.Select(song => (t.tier, song)))
                    // An encore is only played after a set that earned it (every set song at these stars).
                    .Where(c => !c.song.IsEncore || CareerSetlists.EncoreEarned(c.tier, new[] { stars }))
                    .Where(c => !run.Played.Contains(c.song.Id))
                    .ToList();
                if (candidates.Count == 0)
                {
                    break;
                }

                var (_, next) = greedy
                    ? candidates.OrderByDescending(c => CareerFans.SongFans(c.tier, stars)).First()
                    : candidates[0];

                run.Played.Add(next.Id);
                run.Rows.Add(new CareerSongCompletions
                {
                    Id = run.Rows.Count + 1,
                    CareerSongId = next.Id,
                    BandStars = Math.Min(stars, 5),
                    IsGold = stars >= CareerFans.GOLD_STARS,
                });

                run.Evaluation = Evaluate(career, run.Rows);
                var opened = new List<int>();
                for (var i = 0; i < career.Tiers.Count; i++)
                {
                    if (run.Evaluation.Tiers[i].Unlocked && !run.OpenedAfter.ContainsKey(i))
                    {
                        run.OpenedAfter[i] = run.Played.Count;
                        opened.Add(i);
                    }
                }

                if (opened.Count > 0)
                {
                    run.OpenedTogether.Add(opened);
                }
            }

            return run;
        }

        private static int FinalTier => Headliner.Tiers.Count - 1;

        private static int SongsBeforeFinal => Headliner.Tiers.Take(FinalTier).Sum(tier => tier.Songs.Length);

        [Test]
        public void ReachableFansTierByTier()
        {
            // Fans per tier at each average (every song played at that many stars, no contracts), and when
            // each tier opens playing in list order. Printed so tuning can be read off the test output.
            foreach (var stars in new[] { 3, 4, 5 })
            {
                var run = Play(Headliner, stars, greedy: false);
                TestContext.Out.WriteLine($"{stars} stars: {run.Evaluation.TotalFans} fans in total");
                foreach (var tier in run.Evaluation.Tiers)
                {
                    var content = Headliner.Tiers[tier.TierIndex];
                    TestContext.Out.WriteLine($"  {content.Name,-22} needs {content.UnlockCriteria,6:N0}  " +
                        $"earns {tier.Fans,6:N0}  opened after {run.OpenedAfter.GetValueOrDefault(tier.TierIndex, -1)} songs");
                }

                Assert.That(run.Evaluation.Tiers.All(tier => tier.Unlocked), Is.True,
                    $"every tier opens at {stars} stars when every song is played");
            }
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EveryTierIsReachable(int stars)
        {
            var run = Play(Headliner, stars, greedy: true);
            var locked = run.Evaluation.Tiers.Where(tier => !tier.Unlocked)
                .Select(tier => Headliner.Tiers[tier.TierIndex].Name);
            Assert.That(locked, Is.Empty, $"{stars} stars");
        }

        [Test]
        public void FourStarsReachTheFinalTierComfortably()
        {
            var fewest = Play(Headliner, 4, greedy: true, until: FinalTier);
            Assert.That(fewest.Evaluation.Tiers[FinalTier].Unlocked, Is.True);
            TestContext.Out.WriteLine($"4 stars: final tier after {fewest.Played.Count} of {SongsBeforeFinal} songs");
            Assert.That(fewest.Played.Count, Is.LessThanOrEqualTo(SongsBeforeFinal * 2 / 3),
                "a 4-star player can skip a third of the songs (or a whole path) and still reach the final tier");
        }

        [Test]
        public void ThreeStarsReachTheFinalTierOnlyByPlayingMostSongs()
        {
            var fewest = Play(Headliner, 3, greedy: true, until: FinalTier);
            Assert.That(fewest.Evaluation.Tiers[FinalTier].Unlocked, Is.True, "reachable at 3 stars");
            TestContext.Out.WriteLine($"3 stars: final tier after {fewest.Played.Count} of {SongsBeforeFinal} songs");
            Assert.That(fewest.Played.Count, Is.GreaterThan(SongsBeforeFinal * 3 / 4),
                "even playing the songs worth the most first, a 3-star player needs more than three quarters of them");

            // Below 3 stars the final tier is out of reach until results improve.
            Assert.That(Play(Headliner, 2, greedy: false).Evaluation.Tiers[FinalTier].Unlocked, Is.False);
        }

        [Test]
        public void FiveStarsNeedFewerSongsThanFour()
        {
            var four = Play(Headliner, 4, greedy: true, until: FinalTier).Played.Count;
            var five = Play(Headliner, 5, greedy: true, until: FinalTier).Played.Count;
            Assert.That(five, Is.LessThan(four));
        }

        [Test]
        public void AtLeastTwoPointsOpenTwoGigsAtOnce()
        {
            var run = Play(Headliner, 4, greedy: false);
            var choices = run.OpenedTogether
                .Select(opened => opened.Where(i => !Headliner.Tiers[i].IsBonus).ToList())
                .Where(opened => opened.Count >= 2)
                .ToList();

            foreach (var choice in choices)
            {
                TestContext.Out.WriteLine("opened together: " +
                    string.Join(", ", choice.Select(i => Headliner.Tiers[i].Name)));
            }

            Assert.That(choices, Has.Count.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void HeadlinerContent()
        {
            var career = Headliner;
            Assert.That(CareerFans.UsesFans(career), Is.True);
            Assert.That(career.DefaultPreset, Is.True);

            // Fan-count unlocks throughout; venues rising from Starter to Epic; flavor on every tier.
            Assert.That(career.Tiers.All(tier => tier.UnlockType == UnlockType.FanCount), Is.True);
            var regular = career.Tiers.Where(tier => !tier.IsBonus).Select(tier => tier.VenueSize).ToList();
            Assert.That(regular, Is.Ordered);
            Assert.That(regular.First(), Is.EqualTo(VenueSize.Starter));
            Assert.That(regular.Last(), Is.EqualTo(VenueSize.Epic));
            Assert.That(career.Tiers.All(tier => !string.IsNullOrEmpty(tier.City) && !string.IsNullOrEmpty(tier.VenueName)),
                Is.True);
            Assert.That(career.Tiers.Count(tier => tier.IsBonus), Is.InRange(1, 2));

            // Every Phase A contract type, on tiers and on single songs.
            var tierContracts = career.Tiers.Select(tier => tier.Contract).Where(c => c is { IsActive: true }).ToList();
            var songContracts = career.Tiers.SelectMany(tier => tier.Songs).Select(song => song.Contract)
                .Where(c => c is { IsActive: true }).ToList();
            Assert.That(tierContracts, Is.Not.Empty);
            Assert.That(songContracts, Is.Not.Empty);
            var types = tierContracts.Concat(songContracts).Select(c => c.Type).Distinct();
            Assert.That(types, Is.EquivalentTo(Enum.GetValues<CareerContractType>().Where(t => t != CareerContractType.None)));

            // Fresh, stable ids: career song ids are not the library song ids the other careers use.
            var ids = career.Tiers.Select(tier => tier.Id)
                .Concat(career.Tiers.SelectMany(tier => tier.Songs).Select(song => song.Id))
                .Append(career.Id).ToList();
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Does.Not.Contain(Guid.Empty));
            var librarySongIds = career.Tiers.SelectMany(tier => tier.Songs).Select(song => song.SongId!.Value).ToList();
            Assert.That(librarySongIds, Is.Unique);
            Assert.That(ids.Intersect(librarySongIds), Is.Empty);

            // Built only from songs the other shipped careers already use.
            var shipped = CareerPreset.YargRock.Tiers.Concat(CareerPreset.YargSetlistPop.Tiers)
                .SelectMany(tier => tier.Songs).Select(song => song.SongId!.Value).ToHashSet();
            Assert.That(librarySongIds.Where(id => !shipped.Contains(id)), Is.Empty);
        }

        [Test]
        public void TheOriginalCareersAreUnchanged()
        {
            Assert.That(CareerPreset.Defaults.Take(2), Is.EqualTo(new[] { CareerPreset.YargRock, CareerPreset.YargSetlistPop }));
            Assert.That(CareerPreset.YargRock.Id, Is.EqualTo(Guid.Parse("1cb75c48-ef17-52aa-bb40-f48fe0fcf2d4")));
            Assert.That(CareerPreset.YargSetlistPop.Id, Is.EqualTo(Guid.Parse("7e9ba306-2dc8-4dbf-83f4-80325844b1c4")));

            // Same tier ids, unlock rules, bonus flags and songs as before; nothing of the new fields.
            var expectedCriteria = new[] { 0, 18, 18, 36, 54, 54, 72, 90, 90, 108, 126, 126 };
            var expectedBonus = new[] { 1, 4, 7, 10 };
            foreach (var career in new[] { CareerPreset.YargRock, CareerPreset.YargSetlistPop })
            {
                Assert.That(career.Tiers.Select(tier => tier.UnlockCriteria), Is.EqualTo(expectedCriteria), career.Name);
                Assert.That(career.Tiers.All(tier => tier.UnlockType == UnlockType.StarCount), Is.True);
                Assert.That(Enumerable.Range(0, 12).Where(i => career.Tiers[i].IsBonus), Is.EqualTo(expectedBonus));
                Assert.That(career.Tiers.All(tier => tier.Contract is null && tier.FanMultiplier is null &&
                    tier.City is null && tier.VenueName is null), Is.True);
                Assert.That(career.Tiers.SelectMany(tier => tier.Songs).All(song => song.Id == song.SongId &&
                    song.Contract is null), Is.True);
                // Fans from song difficulty (no venue sizes, no fan-count unlocks), shown like any career's.
                Assert.That(career.Tiers.All(tier => tier.VenueSize == VenueSize.NotSpecified), Is.True);
                Assert.That(CareerFans.FanGated(career), Is.False);
                Assert.That(CareerFans.UsesFans(career), Is.True);
            }

            Assert.That(CareerPreset.YargRock.Tiers.Select(tier => tier.Id.ToString()[..8]), Is.EqualTo(new[]
            {
                "92aa777c", "dcf2e5b2", "239b6ac6", "512bf8db", "560ef7da", "76c85f05",
                "c0ef3129", "095ae288", "54b25571", "02f5d98e", "a38b05e2", "89904050",
            }));
            Assert.That(CareerPreset.YargSetlistPop.Tiers.Select(tier => tier.Id),
                Is.EqualTo(CareerPreset.YargRock.Tiers.Select(tier => tier.Id)));
            Assert.That(CareerPreset.YargRock.Tiers.Sum(tier => tier.Songs.Length), Is.EqualTo(80));
            Assert.That(CareerPreset.YargSetlistPop.Tiers.Sum(tier => tier.Songs.Length), Is.EqualTo(80));
        }
    }
}
