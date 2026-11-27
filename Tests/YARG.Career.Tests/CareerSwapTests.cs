using System;
using System.Linq;
using NUnit.Framework;
using YARG.Scores;

namespace YARG.Career.Tests
{
    public class CareerSwapTests
    {
        private static readonly byte[] Original = { 1, 2, 3 };
        private static readonly byte[] Swapped = { 9, 8, 7 };
        private static readonly DateTime Start = new(2026, 11, 26, 18, 0, 0);

        private static CareerSongCompletions Row(int id, byte[] song, int stars, int minutes)
        {
            return new CareerSongCompletions
            {
                Id = id,
                CareerSongId = Guid.Empty,
                SongChecksum = song,
                BandStars = stars,
                CompletedAt = Start.AddMinutes(minutes),
            };
        }

        [Test]
        public void TheLastSongPlayedInASlotIsTheOneThatCounts()
        {
            var rows = new[] { Row(1, Original, 4, 0), Row(2, Swapped, 3, 10) }.ToList();
            Assert.That(CareerEvaluation.CurrentSongRows(rows).Select(row => row.Id), Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void PlayingTheOriginalAgainTradesBack()
        {
            var rows = new[] { Row(1, Original, 5, 0), Row(2, Swapped, 3, 10), Row(3, Original, 2, 20) }.ToList();
            Assert.That(CareerEvaluation.CurrentSongRows(rows).Select(row => row.Id), Is.EqualTo(new[] { 1, 3 }),
                "the original's earlier, better result counts again");
        }

        [Test]
        public void ReplaysOfTheSameSongKeepTheBest()
        {
            var rows = new[] { Row(1, Swapped, 5, 0), Row(2, Swapped, 2, 10) }.ToList();
            Assert.That(CareerEvaluation.CurrentSongRows(rows).Count, Is.EqualTo(2));
        }

        [Test]
        public void RowsWithoutAChecksumAreOneSong()
        {
            var rows = new[] { Row(1, null, 5, 0), Row(2, Array.Empty<byte>(), 2, 10) }.ToList();
            Assert.That(CareerEvaluation.CurrentSongRows(rows).Count, Is.EqualTo(2));
        }
    }
}
