using System;
using System.Linq;
using Newtonsoft.Json;

namespace YARG.Career
{
    /// <summary>
    /// A gig's theme: which songs of the library fit it. Every part is optional. A song fits when it is
    /// one of <see cref="Songs"/>, or when it meets every part that is set: its year is in
    /// <see cref="YearMin"/>..<see cref="YearMax"/>, it is from one of <see cref="Sources"/>, and it is of
    /// one of <see cref="Genres"/> or by one of <see cref="Artists"/> (either one, when both are set).
    /// Songs played in a themed gig in place of its own (see RetailMode) and its user picks
    /// (<see cref="CareerTier.UserPicks"/>) must fit (see <see cref="CareerThemes"/>).
    /// </summary>
    public class CareerGigTheme
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? YearMin;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? YearMax;

        // Matched ignoring case, spaces and punctuation ("Pop Rock" = "Pop/Rock" = "pop-rock").
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[] Genres;

        // Matched ignoring case and surrounding spaces.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[] Artists;

        // A song source's id ("yarg", "rb1", "rb3dlc", ...) or its display name ("Rock Band 1"),
        // ignoring case.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[] Sources;

        // Always fit the theme, whatever the other parts say.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public CareerSong[] Songs;

        public CareerGigTheme()
        {
        }

        public CareerGigTheme(CareerGigTheme other)
        {
            YearMin = other.YearMin;
            YearMax = other.YearMax;
            Genres = other.Genres?.ToArray();
            Artists = other.Artists?.ToArray();
            Sources = other.Sources?.ToArray();
            Songs = other.Songs?.Select(song => new CareerSong(song) { Id = song.Id }).ToArray();
        }

        /// <summary>Nothing is set: every song fits.</summary>
        [JsonIgnore]
        public bool IsEmpty => YearMin is null && YearMax is null && IsBlank(Genres) && IsBlank(Artists) &&
            IsBlank(Sources) && (Songs is null || Songs.Length == 0);

        private static bool IsBlank(string[] values) => values is null || values.All(string.IsNullOrWhiteSpace);
    }

    /// <summary>
    /// Whether a song fits a gig's theme (see <see cref="CareerGigTheme"/>). The rule itself is pure, on
    /// plain song facts, so it is covered by Tests/YARG.Career.Tests.
    /// </summary>
    public static partial class CareerThemes
    {
        /// <summary>What the rule looks at in a song.</summary>
        public struct SongFacts
        {
            public string Artist;
            public string Genre;
            // The source's id and display name; either may match.
            public string SourceId;
            public string SourceName;
            // 0 or less: unknown (fails a year range).
            public int Year;
        }

        /// <param name="listed">The song is one of the theme's own songs.</param>
        public static bool Matches(CareerGigTheme theme, SongFacts song, bool listed)
        {
            if (theme is null || theme.IsEmpty || listed)
            {
                return true;
            }

            if (theme.YearMin is not null || theme.YearMax is not null)
            {
                if (song.Year <= 0 || song.Year < (theme.YearMin ?? int.MinValue) ||
                    song.Year > (theme.YearMax ?? int.MaxValue))
                {
                    return false;
                }
            }

            var sources = Values(theme.Sources);
            if (sources.Length > 0 && !sources.Any(source =>
                    Same(source, song.SourceId) || Same(source, song.SourceName)))
            {
                return false;
            }

            var genres = Values(theme.Genres);
            var artists = Values(theme.Artists);
            if (genres.Length == 0 && artists.Length == 0)
            {
                return true;
            }

            var genre = NormalizeGenre(song.Genre);
            return genres.Any(wanted => genre.Length > 0 && NormalizeGenre(wanted) == genre) ||
                artists.Any(wanted => Same(wanted, song.Artist));
        }

        /// <summary>Lower case, letters and digits only: "Pop/Rock" and "Pop Rock" are the same genre.</summary>
        public static string NormalizeGenre(string genre)
        {
            return string.IsNullOrEmpty(genre)
                ? string.Empty
                : new string(genre.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static string[] Values(string[] values)
        {
            return values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray()
                ?? Array.Empty<string>();
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
