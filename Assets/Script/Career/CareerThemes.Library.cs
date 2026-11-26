using System.Linq;
using YARG.Core.Song;
using YARG.Song;

namespace YARG.Career
{
    // The theme rule on songs of the library.
    public static partial class CareerThemes
    {
        public static bool Matches(CareerGigTheme theme, SongEntry song)
        {
            if (song is null)
            {
                return false;
            }

            if (theme is null || theme.IsEmpty)
            {
                return true;
            }

            var listed = theme.Songs?.Any(own => own?.SongEntry is { } entry && entry.Hash.Equals(song.Hash)) ?? false;
            var source = song.Source.Original;
            return Matches(theme, new SongFacts
            {
                Artist = song.Artist.Original,
                Genre = song.Genre.Original,
                SourceId = source,
                SourceName = SongSources.SourceToGameName(source),
                // A song without a year has int.MaxValue.
                Year = song.YearAsNumber is > 0 and < 10000 ? song.YearAsNumber : 0,
            }, listed);
        }

        /// <summary>
        /// The filter the music library applies while picking a song for a themed gig. Null: no theme.
        /// </summary>
        public static System.Func<SongEntry, bool> Filter(CareerGigTheme theme)
        {
            return theme is null || theme.IsEmpty ? null : song => Matches(theme, song);
        }
    }
}
