using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Career;
using YARG.Settings.Types;
using YARG.Song;

namespace YARG.Settings.Metadata
{
    /// <summary>
    /// Career editor fields for a gig's theme (see CareerGigTheme) and its user picks. Lists are typed
    /// comma separated; the theme's own songs as "Artist - Title" separated by semicolons, looked up
    /// in the library when entered. Clearing every part removes the theme.
    /// </summary>
    public partial class CareerPresetSubTab
    {
        private IEnumerable<CareerPresetPreviewBuilder.Field> TierThemeFields(CareerTier tier, bool editable)
        {
            // 0 = no limit.
            yield return new CareerPresetPreviewBuilder.Field(FieldText("ThemeYearMin"),
                new IntSetting(tier.Theme?.YearMin ?? 0, min: 0, max: 2100, onChange: value =>
                {
                    EditTheme(tier, theme => theme.YearMin = value <= 0 ? null : value);
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ThemeYearMax"),
                new IntSetting(tier.Theme?.YearMax ?? 0, min: 0, max: 2100, onChange: value =>
                {
                    EditTheme(tier, theme => theme.YearMax = value <= 0 ? null : value);
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ThemeGenres"),
                new StringSetting(JoinList(tier.Theme?.Genres), value =>
                {
                    EditTheme(tier, theme => theme.Genres = SplitList(value, ','));
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ThemeArtists"),
                new StringSetting(JoinList(tier.Theme?.Artists), value =>
                {
                    EditTheme(tier, theme => theme.Artists = SplitList(value, ','));
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ThemeSources"),
                new StringSetting(JoinList(tier.Theme?.Sources), value =>
                {
                    EditTheme(tier, theme => theme.Sources = SplitList(value, ','));
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ThemeSongs"),
                new StringSetting(FormatThemeSongs(tier.Theme?.Songs), value =>
                {
                    var songs = ParseThemeSongs(value);
                    if (songs is null)
                    {
                        // A song that is not in the library: keep what was there.
                        return;
                    }

                    EditTheme(tier, theme => theme.Songs = songs.Length == 0 ? null : songs);
                }), editable);

            // 0 = none.
            yield return new CareerPresetPreviewBuilder.Field(FieldText("UserPicks"),
                new IntSetting(tier.UserPicks ?? 0, min: 0, max: CareerTier.MAX_USER_PICKS, onChange: value =>
                {
                    tier.UserPicks = value <= 0 ? null : value;
                    tier.RefreshUserPicks();
                    Save();
                }), editable);
        }

        private void EditTheme(CareerTier tier, Action<CareerGigTheme> edit)
        {
            var theme = tier.Theme ?? new CareerGigTheme();
            edit(theme);
            tier.Theme = theme.IsEmpty ? null : theme;
            Save();
        }

        private static string JoinList(string[] values)
        {
            return values is null ? string.Empty : string.Join(", ", values);
        }

        private static string[] SplitList(string value, char separator)
        {
            var values = (value ?? string.Empty).Split(separator)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
            return values.Length == 0 ? null : values;
        }

        private static string FormatThemeSongs(CareerSong[] songs)
        {
            return songs is null
                ? string.Empty
                : string.Join("; ", songs.Select(song => song.SongEntry is { } entry
                    ? $"{entry.Artist.Original} - {entry.Name.Original}"
                    : song.Description ?? song.Id.ToString()));
        }

        /// <summary>
        /// "Artist - Title; Artist - Title" to songs of the library. Null if one is not found.
        /// </summary>
        private static CareerSong[] ParseThemeSongs(string value)
        {
            var songs = new List<CareerSong>();
            foreach (var part in SplitList(value, ';') ?? Array.Empty<string>())
            {
                var dash = part.IndexOf(" - ", StringComparison.Ordinal);
                if (dash <= 0)
                {
                    return null;
                }

                var artist = part[..dash].Trim();
                var title = part[(dash + 3)..].Trim();
                var entry = SongContainer.Songs.FirstOrDefault(song =>
                    string.Equals(song.Artist.Original, artist, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(song.Name.Original, title, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return null;
                }

                var song = CareerSong.FromSongEntry(entry, CareerSongIdentifier.SongTuple);
                song.Description = $"{entry.Artist.Original} - {entry.Name.Original}";
                songs.Add(song);
            }

            return songs.ToArray();
        }
    }
}
