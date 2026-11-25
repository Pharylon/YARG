using NUnit.Framework;

namespace YARG.Career.Tests
{
    public class CareerBandHomeLayoutTests
    {
        private const float EPSILON = 1e-3f;
        private const float MAX_STAR = 48f;
        private const float MIN_STAR = 16f;

        private static readonly int[] STAR_COUNTS = { 1, 5, 26, 99, 105, 326, 999, 5000 };
        private static readonly float[] STAR_WIDTHS = { 120f, 300f, 640f, 1600f };
        private static readonly int[] FAN_COUNTS = { 1, 10, 188, 2_000, 20_000, 50_000, 1_000_000 };

        // (width, height, maxSize, minSize, aspect)
        private static readonly (float w, float h, float max, float min, float aspect)[] AREAS =
        {
            (800f, 220f, 90f, 30f, 0.667f),
            (320f, 120f, 60f, 20f, 0.667f),
            (1600f, 400f, 120f, 40f, 0.5f),
            (90f, 40f, 60f, 20f, 0.667f), // too small for the minimum size
        };

        [TestCase(0, 0, 0)]
        [TestCase(-5, 0, 0)]
        [TestCase(1, 0, 1)]
        [TestCase(99, 0, 99)]
        [TestCase(100, 1, 0)]
        [TestCase(326, 3, 26)]
        [TestCase(5000, 50, 0)]
        public void SplitStars_OneGoldPerHundred(int total, int gold, int regular)
        {
            Assert.That(CareerBandHomeLayout.SplitStars(total), Is.EqualTo((gold, regular)));
        }

        [Test]
        public void Stars_NoneIsEmpty()
        {
            Assert.That(CareerBandHomeLayout.Stars(0, 300f, MAX_STAR, MIN_STAR), Is.Empty);
            Assert.That(CareerBandHomeLayout.Stars(-3, 300f, MAX_STAR, MIN_STAR), Is.Empty);
        }

        [Test]
        public void Stars_AlwaysInsideWidth()
        {
            foreach (var count in STAR_COUNTS)
            {
                foreach (var width in STAR_WIDTHS)
                {
                    var stars = CareerBandHomeLayout.Stars(count, width, MAX_STAR, MIN_STAR);
                    var (gold, regular) = CareerBandHomeLayout.SplitStars(count);
                    Assert.That(stars, Has.Count.EqualTo(gold + regular), $"{count} stars");
                    foreach (var star in stars)
                    {
                        Assert.That(star.X - star.Size / 2f, Is.GreaterThanOrEqualTo(-width / 2f - EPSILON),
                            $"{count} stars in {width}");
                        Assert.That(star.X + star.Size / 2f, Is.LessThanOrEqualTo(width / 2f + EPSILON),
                            $"{count} stars in {width}");
                    }
                }
            }
        }

        [Test]
        public void Stars_FitEvenWhenNarrowerThanMinimum()
        {
            var stars = CareerBandHomeLayout.Stars(326, 10f, MAX_STAR, MIN_STAR);
            foreach (var star in stars)
            {
                Assert.That(Math.Abs(star.X) + star.Size / 2f, Is.LessThanOrEqualTo(5f + EPSILON));
            }
        }

        [Test]
        public void Stars_GoldFirstThenLeftToRight()
        {
            var stars = CareerBandHomeLayout.Stars(326, 640f, MAX_STAR, MIN_STAR);
            Assert.That(stars.Take(3).All(s => s.Gold), Is.True);
            Assert.That(stars.Skip(3).Any(s => s.Gold), Is.False);
            for (var i = 1; i < stars.Count; i++)
            {
                Assert.That(stars[i].X, Is.GreaterThanOrEqualTo(stars[i - 1].X));
            }
        }

        [Test]
        public void Stars_SizesWithinLimits()
        {
            foreach (var count in STAR_COUNTS)
            {
                foreach (var width in STAR_WIDTHS)
                {
                    var stars = CareerBandHomeLayout.Stars(count, width, MAX_STAR, MIN_STAR);
                    foreach (var star in stars)
                    {
                        float scale = star.Gold ? 1.25f : 1f;
                        Assert.That(star.Size, Is.InRange(MIN_STAR * scale - EPSILON, MAX_STAR * scale + EPSILON),
                            $"{count} stars in {width}");
                        Assert.That(Math.Abs(star.Rotation), Is.LessThanOrEqualTo(10f));
                        Assert.That(Math.Abs(star.Y), Is.LessThanOrEqualTo(0.12f * star.Size + EPSILON));
                    }
                }
            }
        }

        [Test]
        public void Stars_FewAreFullSizeAndCentered()
        {
            var stars = CareerBandHomeLayout.Stars(5, 640f, MAX_STAR, MIN_STAR);
            Assert.That(stars.All(s => Math.Abs(s.Size - MAX_STAR) < EPSILON), Is.True);
            Assert.That(stars[0].X + stars[^1].X, Is.EqualTo(0f).Within(EPSILON));
        }

        [Test]
        public void Stars_GoldLargerThanRegular()
        {
            var stars = CareerBandHomeLayout.Stars(105, 640f, MAX_STAR, MIN_STAR);
            Assert.That(stars[0].Size, Is.EqualTo(stars[1].Size * 1.25f).Within(EPSILON));
        }

        [Test]
        public void Stars_Deterministic()
        {
            var a = CareerBandHomeLayout.Stars(326, 300f, MAX_STAR, MIN_STAR);
            var b = CareerBandHomeLayout.Stars(326, 300f, MAX_STAR, MIN_STAR);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.All(s => s.Y == 0f && s.Rotation == 0f), "a straight, upright line");
        }

        [TestCase(0, 0)]
        [TestCase(-10, 0)]
        [TestCase(1, 1)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        [TestCase(188, 14)]
        [TestCase(50_000, 224)]
        [TestCase(57_600, 240)]
        [TestCase(1_000_000, 240)]
        [TestCase(int.MaxValue, 240)]
        public void CrowdSize_SquareRootCapped(int fans, int expected)
        {
            Assert.That(CareerBandHomeLayout.CrowdSize(fans), Is.EqualTo(expected));
        }

        [Test]
        public void Crowd_CountMatchesCrowdSizeAndInsideArea()
        {
            foreach (var area in AREAS)
            {
                foreach (var fans in FAN_COUNTS)
                {
                    var crowd = CareerBandHomeLayout.Crowd(fans, area.w, area.h, 6, area.max, area.min,
                        area.aspect);
                    var label = $"{fans} fans in {area.w}x{area.h}";
                    Assert.That(crowd, Has.Count.EqualTo(CareerBandHomeLayout.CrowdSize(fans)), label);
                    foreach (var icon in crowd)
                    {
                        float halfWidth = icon.Size * area.aspect / 2f;
                        Assert.That(icon.Size, Is.GreaterThan(0f), label);
                        Assert.That(icon.Size, Is.LessThanOrEqualTo(area.max + EPSILON), label);
                        Assert.That(icon.X - halfWidth, Is.GreaterThanOrEqualTo(-EPSILON), label);
                        Assert.That(icon.X + halfWidth, Is.LessThanOrEqualTo(area.w + EPSILON), label);
                        Assert.That(icon.Y, Is.GreaterThanOrEqualTo(-EPSILON), label);
                        Assert.That(icon.Y + icon.Size, Is.LessThanOrEqualTo(area.h + EPSILON), label);
                        Assert.That(Math.Abs(icon.Rotation), Is.LessThanOrEqualTo(4f), label);
                    }
                }
            }
        }

        [Test]
        public void Crowd_DrawOrderBackToFrontLeftToRight()
        {
            foreach (var area in AREAS)
            {
                foreach (var fans in FAN_COUNTS)
                {
                    var crowd = CareerBandHomeLayout.Crowd(fans, area.w, area.h, 6, area.max, area.min,
                        area.aspect);
                    var label = $"{fans} fans in {area.w}x{area.h}";
                    for (var i = 1; i < crowd.Count; i++)
                    {
                        Assert.That(crowd[i].Depth, Is.LessThanOrEqualTo(crowd[i - 1].Depth), label);
                    }

                    Assert.That(crowd.All(c => c.Depth >= 0f && c.Depth <= 1f), Is.True, label);
                    if (crowd.Count > 0)
                    {
                        Assert.That(crowd[^1].Depth, Is.EqualTo(0f), label);
                    }
                }
            }
        }

        [Test]
        public void Crowd_SmallIsOneCenteredRowAtFullSize()
        {
            var crowd = CareerBandHomeLayout.Crowd(10, 800f, 220f, 6, 90f, 30f, 0.667f);
            Assert.That(crowd, Has.Count.EqualTo(4));
            Assert.That(crowd.All(c => c.Depth == 0f && Math.Abs(c.Size - 90f) < EPSILON), Is.True);
            Assert.That(crowd.Average(c => c.X), Is.EqualTo(400f).Within(0.5f * 90f));
            Assert.That(crowd.All(c => c.Y <= 0.04f * 90f + EPSILON), Is.True);
        }

        [Test]
        public void Crowd_LargeHasSeveralRowsWithSmallerBackRow()
        {
            var crowd = CareerBandHomeLayout.Crowd(50_000, 800f, 220f, 6, 90f, 30f, 0.667f);
            Assert.That(crowd[0].Depth, Is.EqualTo(1f).Within(EPSILON));
            Assert.That(crowd[0].Size, Is.LessThan(crowd[^1].Size));
        }

        [Test]
        public void Crowd_VariantsInRangeAndNeighboursDiffer()
        {
            foreach (var variants in new[] { 1, 2, 5, 9 })
            {
                var crowd = CareerBandHomeLayout.Crowd(20_000, 800f, 220f, variants, 90f, 30f, 0.667f);
                Assert.That(crowd.All(c => c.Variant >= 0 && c.Variant < variants), Is.True);
                if (variants > 1)
                {
                    for (var i = 1; i < crowd.Count; i++)
                    {
                        if (crowd[i].Depth == crowd[i - 1].Depth)
                        {
                            Assert.That(crowd[i].Variant, Is.Not.EqualTo(crowd[i - 1].Variant));
                        }
                    }

                    Assert.That(crowd.Select(c => c.Variant).Distinct().Count(), Is.EqualTo(variants));
                }
            }
        }

        [Test]
        public void Crowd_Deterministic()
        {
            var a = CareerBandHomeLayout.Crowd(2_000, 800f, 220f, 6, 90f, 30f, 0.667f);
            var b = CareerBandHomeLayout.Crowd(2_000, 800f, 220f, 6, 90f, 30f, 0.667f);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void Crowd_GuardsBadInputs()
        {
            Assert.That(CareerBandHomeLayout.Crowd(0, 800f, 220f, 6, 90f, 30f, 0.667f), Is.Empty);
            Assert.That(CareerBandHomeLayout.Crowd(100, 0f, 220f, 6, 90f, 30f, 0.667f), Is.Empty);
            Assert.That(CareerBandHomeLayout.Crowd(100, 800f, -1f, 6, 90f, 30f, 0.667f), Is.Empty);

            var crowd = CareerBandHomeLayout.Crowd(2_000, 400f, 150f, 0, -5f, -5f, 0f);
            Assert.That(crowd, Has.Count.EqualTo(CareerBandHomeLayout.CrowdSize(2_000)));
            foreach (var icon in crowd)
            {
                Assert.That(icon.Variant, Is.EqualTo(0));
                Assert.That(icon.Size, Is.GreaterThan(0f));
                Assert.That(icon.Y + icon.Size, Is.LessThanOrEqualTo(150f + EPSILON));
            }
        }
    }
}
