using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using YARG.Career;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// Text for how a gig is played (full set, pick your set, encore, mystery setlist, gig rules), for the
    /// career list, the instrument select and the score screen (lang <c>Menu.Career.Show.*</c>).
    /// </summary>
    public static class CareerSetlistText
    {
        /// <summary>"Full set · Pick 2 of 4 · Encore at 4 stars average · Mystery setlist · All Strums", or null.</summary>
        public static string TierLine(CareerTier tier)
        {
            if (tier is null)
            {
                return null;
            }

            var parts = new List<string>();
            var set = CareerSetlists.SetSongIndexes(tier).Count;
            if (CareerSetlists.IsShow(tier))
            {
                parts.Add(CareerSetlists.PicksSongs(tier)
                    ? Localize.KeyFormat("Menu.Career.Show.Pick", CareerSetlists.RequiredSongs(tier), set)
                    : Localize.Key("Menu.Career.Show.Tier"));
            }
            else if (CareerSetlists.PicksSongs(tier))
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Show.Any", CareerSetlists.RequiredSongs(tier), set));
            }

            if (CareerSetlists.HasEncore(tier))
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Show.Encore", Stars(tier.EncoreMinStars.Value)));
            }

            if (ThemeLine(tier.Theme) is { } theme)
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Theme.Line", theme));
            }

            if (tier.UserPicks is > 0 and var picks)
            {
                parts.Add(Localize.KeyFormat(picks == 1 ? "Menu.Career.Theme.PicksOne" : "Menu.Career.Theme.Picks", picks));
            }

            if (tier.Mystery)
            {
                parts.Add(Localize.Key("Menu.Career.Show.Mystery"));
            }

            if (RulesLine(tier) is { } rules)
            {
                parts.Add(rules);
            }

            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
        }

        /// <summary>
        /// "1980-1989, Metal or Punk, Rock Band 1, or 3 listed songs", or null without a theme.
        /// </summary>
        public static string ThemeLine(CareerGigTheme theme)
        {
            if (theme is null || theme.IsEmpty)
            {
                return null;
            }

            var parts = new List<string>();
            if (theme.YearMin is { } from && theme.YearMax is { } to)
            {
                parts.Add(from == to ? from.ToString() : Localize.KeyFormat("Menu.Career.Theme.Years", from, to));
            }
            else if (theme.YearMin is { } since)
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Theme.Since", since));
            }
            else if (theme.YearMax is { } until)
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Theme.Until", until));
            }

            var who = (theme.Genres ?? Array.Empty<string>()).Concat(theme.Artists ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
            if (who.Count > 0)
            {
                parts.Add(string.Join($" {Localize.Key("Menu.Career.Theme.Or")} ", who));
            }

            var sources = (theme.Sources ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => YARG.Song.SongSources.TryGetSource(value.Trim(), out var source)
                    ? source.GetDisplayName()
                    : value.Trim())
                .ToList();
            if (sources.Count > 0)
            {
                parts.Add(string.Join($" {Localize.Key("Menu.Career.Theme.Or")} ", sources));
            }

            var text = string.Join(", ", parts);
            var listed = theme.Songs?.Length ?? 0;
            if (listed > 0)
            {
                text = text.Length == 0
                    ? Localize.KeyFormat("Menu.Career.Theme.ListedOnly", listed)
                    : Localize.KeyFormat("Menu.Career.Theme.Listed", text, listed);
            }

            return text;
        }

        /// <summary>"All Strums, 125% speed or faster", or null if the tier has no gig rules.</summary>
        public static string RulesLine(CareerTier tier)
        {
            if (!CareerGigRules.HasRules(tier))
            {
                return null;
            }

            var parts = CareerGigRules.Flags(CareerGigRules.ForcedModifiers(tier))
                .Select(modifier => modifier.ToLocalizedName())
                .ToList();
            if (tier.MinSongSpeed is { } speed && speed > 100)
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Show.RuleSpeed", speed));
            }

            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        public static string Stars(float stars) => stars.ToString("0.#", CultureInfo.CurrentCulture);

        /// <summary>
        /// The song about to be played from the career menu is hidden (a mystery setlist's or an encore, not
        /// yet played by this save): its title and artist to show instead.
        /// </summary>
        public static bool HideCurrentSong(out string title, out string artist)
        {
            title = artist = null;
            var context = GlobalVariables.State.CurrentCareer;
            if (context is null)
            {
                return false;
            }

            var career = CareerBands.AllCareers().FirstOrDefault(c => c.Id == context.CareerId);
            var tierIndex = career?.Tiers.ToList().FindIndex(tier => tier.Id == context.TierId) ?? -1;
            if (tierIndex < 0)
            {
                return false;
            }

            var tier = career.Tiers[tierIndex];
            var songIndex = System.Array.FindIndex(tier.Songs, song => song.Id == context.CareerSongId);
            if (songIndex < 0 || !(tier.Mystery || tier.Songs[songIndex].IsEncore))
            {
                return false;
            }

            var evaluation = CareerMenu.Evaluate(career, CareerMenu.FindSaveId(career));
            var completed = evaluation.Tiers[tierIndex].Songs[songIndex].Completed;
            if (!CareerSetlists.IsHidden(tier, tier.Songs[songIndex], completed))
            {
                return false;
            }

            title = HiddenTitle(tier.Songs[songIndex]);
            artist = HiddenDetail(tier, tier.Songs[songIndex]);
            return true;
        }

        public static string HiddenTitle(CareerSong song)
        {
            return Localize.Key(song.IsEncore ? "Menu.Career.Show.EncoreTitle" : "Menu.Career.Show.MysteryTitle");
        }

        public static string HiddenDetail(CareerTier tier, CareerSong song)
        {
            return song.IsEncore && tier.EncoreMinStars is { } bar
                ? Localize.KeyFormat("Menu.Career.Show.EncoreHidden", Stars(bar))
                : Localize.Key("Menu.Career.Show.MysteryDetail");
        }
    }
}
