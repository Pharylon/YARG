using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace YARG.Career.Tests
{
    public class CareerThemesTests
    {
        private static CareerThemes.SongFacts Song(string artist = "Band", string genre = "Rock",
            string source = "rb1", string sourceName = "Rock Band 1", int year = 1985)
        {
            return new CareerThemes.SongFacts
            {
                Artist = artist,
                Genre = genre,
                SourceId = source,
                SourceName = sourceName,
                Year = year,
            };
        }

        [Test]
        public void NoThemeFitsEverything()
        {
            Assert.That(CareerThemes.Matches(null, Song(), false), Is.True);
            Assert.That(CareerThemes.Matches(new CareerGigTheme(), Song(), false), Is.True);
        }

        [Test]
        public void YearRangeIsInclusiveAndNeedsAYear()
        {
            var theme = new CareerGigTheme { YearMin = 1980, YearMax = 1989 };
            Assert.That(CareerThemes.Matches(theme, Song(year: 1980), false), Is.True);
            Assert.That(CareerThemes.Matches(theme, Song(year: 1989), false), Is.True);
            Assert.That(CareerThemes.Matches(theme, Song(year: 1990), false), Is.False);
            Assert.That(CareerThemes.Matches(theme, Song(year: 0), false), Is.False);
            Assert.That(CareerThemes.Matches(new CareerGigTheme { YearMin = 2000 }, Song(year: 2024), false), Is.True);
        }

        [Test]
        public void SourceMatchesIdOrName()
        {
            Assert.That(CareerThemes.Matches(new CareerGigTheme { Sources = new[] { "RB1" } }, Song(), false), Is.True);
            Assert.That(CareerThemes.Matches(new CareerGigTheme { Sources = new[] { "rock band 1" } }, Song(), false), Is.True);
            Assert.That(CareerThemes.Matches(new CareerGigTheme { Sources = new[] { "rb2" } }, Song(), false), Is.False);
        }

        [Test]
        public void GenreOrArtist()
        {
            var theme = new CareerGigTheme { Genres = new[] { "Pop Rock" }, Artists = new[] { "Nirvana" } };
            Assert.That(CareerThemes.Matches(theme, Song(genre: "Pop/Rock"), false), Is.True);
            Assert.That(CareerThemes.Matches(theme, Song(genre: "Grunge", artist: "nirvana "), false), Is.True);
            Assert.That(CareerThemes.Matches(theme, Song(genre: "Grunge", artist: "Pearl Jam"), false), Is.False);
        }

        [Test]
        public void EveryPartThatIsSetMustHold()
        {
            var theme = new CareerGigTheme { YearMin = 1990, Sources = new[] { "rb1" }, Genres = new[] { "Rock" } };
            Assert.That(CareerThemes.Matches(theme, Song(year: 1995), false), Is.True);
            Assert.That(CareerThemes.Matches(theme, Song(year: 1985), false), Is.False, "year");
            Assert.That(CareerThemes.Matches(theme, Song(year: 1995, source: "rb2", sourceName: "Rock Band 2"), false), Is.False, "source");
            Assert.That(CareerThemes.Matches(theme, Song(year: 1995, genre: "Country"), false), Is.False, "genre");
        }

        [Test]
        public void ListedSongsAlwaysFit()
        {
            var theme = new CareerGigTheme { YearMin = 2020, Sources = new[] { "yarg" }, Genres = new[] { "Jazz" } };
            Assert.That(CareerThemes.Matches(theme, Song(), true), Is.True);
            Assert.That(CareerThemes.Matches(theme, Song(), false), Is.False);
        }

        [Test]
        public void UserPicksAreSlotsThatAreNotSaved()
        {
            var json = @"{ ""Name"": ""Picks"", ""Id"": ""3f6b2f67-0941-4a2e-934d-54fbc01e63ce"", ""Songs"": [], ""UserPicks"": 2,
                ""Theme"": { ""Sources"": [""rb1""] } }";
            var tier = JsonConvert.DeserializeObject<CareerTier>(json);

            Assert.That(tier.Songs.Length, Is.EqualTo(2));
            Assert.That(tier.Songs.All(song => song.IsUserPick), Is.True);
            Assert.That(tier.Songs[0].Id, Is.EqualTo(CareerTier.UserPickId(tier.Id, 0)));
            Assert.That(tier.Songs[0].Id, Is.Not.EqualTo(tier.Songs[1].Id));

            var saved = JsonConvert.SerializeObject(tier);
            Assert.That(saved, Does.Contain(@"""Songs"":[]"));
            Assert.That(saved, Does.Contain(@"""UserPicks"":2"));
            Assert.That(tier.Songs.Length, Is.EqualTo(2), "the slots are back after saving");

            var again = JsonConvert.DeserializeObject<CareerTier>(saved);
            Assert.That(again.Songs.Select(song => song.Id), Is.EqualTo(tier.Songs.Select(song => song.Id)));
        }

        [Test]
        public void OwnSongsComeBeforePicks()
        {
            var tier = new CareerTier { Id = System.Guid.NewGuid(), Songs = System.Array.Empty<CareerSong>(), UserPicks = 1 };
            tier.RefreshUserPicks();
            tier.AddSong(new CareerSong { Id = System.Guid.NewGuid() });

            Assert.That(tier.Songs.Length, Is.EqualTo(2));
            Assert.That(tier.Songs[0].IsUserPick, Is.False);
            Assert.That(tier.Songs[1].IsUserPick, Is.True);
        }
    }
}
