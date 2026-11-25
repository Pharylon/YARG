using NUnit.Framework;

namespace YARG.Career.Tests
{
    public class CareerBandAnthemTests
    {
        [Test]
        public void EachSongIsPickedOncePerStar()
        {
            var weights = new[] { 4, 3, 0, 4 };
            Assert.That(CareerBandAnthem.Total(weights), Is.EqualTo(11));

            var picks = new int[weights.Length];
            for (var roll = 0; roll < 11; roll++)
            {
                picks[CareerBandAnthem.Pick(weights, roll)]++;
            }

            Assert.That(picks, Is.EqualTo(weights));
        }

        [Test]
        public void NothingToPick()
        {
            Assert.That(CareerBandAnthem.Pick(new int[0], 0), Is.EqualTo(-1));
            Assert.That(CareerBandAnthem.Pick(new[] { 0, -2 }, 0), Is.EqualTo(-1));
            Assert.That(CareerBandAnthem.Pick(new[] { 3 }, 3), Is.EqualTo(-1));
            Assert.That(CareerBandAnthem.Pick(null, 0), Is.EqualTo(-1));
        }
    }
}
