using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Career;
using YARG.Helpers.Extensions;
using YARG.Localization;
using YARG.Menu.Data;
using YARG.Menu.Dialogs;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Scores;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The band sheet and the gig history, shown in the stock list dialog: its scrolling list holds plain
    /// text lines instead of buttons. Every number comes from <see cref="CareerBandSheet"/>, derived when
    /// the dialog opens.
    /// </summary>
    public static class CareerBandSheetDialogs
    {
        private const float LINE_SIZE = 24f;
        private const float SMALL_SIZE = 20f;

        public static void ShowSheet(CareerBand band)
        {
            var careers = ScoreContainer.Careers;
            if (careers is null || band is null || DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            var sheet = CareerBandSheet.Load(careers, band.Id, CareerBands.AllCareers());
            var dialog = DialogManager.Instance.ShowList(band.Name);
            dialog.ClearButtons();

            foreach (var line in SheetLines(careers, band, sheet))
            {
                AddLine(dialog, line, LINE_SIZE);
            }

            dialog.AddDialogButton("Menu.Career.Sheet.HistoryButton", MenuData.Colors.BrightButton,
                () => SwitchTo(() => ShowHistory(band)).Forget());
            dialog.AddDialogButton("Menu.Common.Close", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);

            FollowSelection(dialog);
        }

        public static void ShowHistory(CareerBand band)
        {
            var careers = ScoreContainer.Careers;
            if (careers is null || band is null || DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            var sheet = CareerBandSheet.Load(careers, band.Id, CareerBands.AllCareers());
            var dialog = DialogManager.Instance.ShowList(Localize.KeyFormat("Menu.Career.Sheet.HistoryTitle", band.Name));
            dialog.ClearButtons();

            foreach (var line in HistoryLines(band, sheet))
            {
                AddLine(dialog, line, SMALL_SIZE);
            }

            dialog.AddDialogButton("Menu.Career.Sheet.SheetButton", MenuData.Colors.BrightButton,
                () => SwitchTo(() => ShowSheet(band)).Forget());
            dialog.AddDialogButton("Menu.Common.Close", MenuData.Colors.CancelButton,
                DialogManager.Instance.ClearDialog);

            FollowSelection(dialog);
        }

        /// <summary>
        /// The sheet as text lines, one list entry each.
        /// </summary>
        public static List<string> SheetLines(CareerDatabase careers, CareerBand band, CareerBandSheet sheet)
        {
            var lines = new List<string>();

            var rank = $"<b>{RankName(sheet.Rank)}</b>  ·  " +
                Localize.KeyFormat("Menu.Career.HeaderFans", sheet.Fans.ToString("N0"));
            rank = sheet.NextRank is { } next
                ? Localize.KeyFormat("Menu.Career.HeaderJoin", rank,
                    Localize.KeyFormat("Menu.Career.Sheet.NextRank", next.fansNeeded.ToString("N0"), RankName(next.rank)))
                : Localize.KeyFormat("Menu.Career.HeaderJoin", rank, Localize.Key("Menu.Career.Sheet.TopRank"));
            lines.Add(rank);

            var notSet = Localize.Key("Menu.Career.Sheet.NotSet");
            lines.Add(string.Join("  ·  ",
                Localize.KeyFormat("Menu.Career.Sheet.Hometown", Blank(band.Hometown) ? notSet : band.Hometown.Trim()),
                Localize.KeyFormat("Menu.Career.Sheet.Founded", band.CreatedAt.ToString("d", CultureInfo.CurrentCulture))));

            var members = CareerBands.MemberNames(careers, band.Id);
            lines.Add(Localize.KeyFormat("Menu.Career.Band.Members", members.Count == 0
                ? Localize.Key("Menu.Career.Band.NoMembers")
                : Localize.List(members)));

            lines.Add(Localize.KeyFormat("Menu.Career.Sheet.Totals", sheet.CareersPlayed, sheet.GigsPlayed,
                sheet.TotalStars, sheet.ContractsMet, sheet.ContractGigs));

            if (sheet.Members.Count > 0)
            {
                lines.Add($"<b>{Localize.Key("Menu.Career.Sheet.MemberRecords")}</b>");
                lines.AddRange(sheet.Members.Select(MemberLine));
            }

            if (sheet.GigsPlayed == 0)
            {
                lines.Add(Localize.Key("Menu.Career.Sheet.NoGigs"));
            }

            return lines;
        }

        private static string MemberLine(CareerBandSheet.MemberRecord member)
        {
            var name = PlayerContainer.GetProfileById(member.ProfileId)?.Name ?? Localize.Key("Menu.Career.Sheet.UnknownPlayer");
            if (member.Gigs == 0)
            {
                return Localize.KeyFormat("Menu.Career.Sheet.Member", name, Localize.Key("Menu.Career.Sheet.MemberNoGigs"));
            }

            var parts = new List<string>
            {
                Localize.KeyFormat("Menu.Career.Sheet.MemberGigs", member.Gigs),
                Localize.List(member.Instruments.Select(played =>
                    Localize.KeyFormat("Menu.Career.Sheet.MemberInstrument", played.instrument.ToLocalizedName(), played.gigs))),
            };

            if (member.AverageAccuracy is { } accuracy)
            {
                parts.Add(Localize.KeyFormat("Menu.Career.Sheet.MemberAccuracy",
                    (accuracy * 100f).ToString("0.#", CultureInfo.CurrentCulture)));
            }

            parts.Add(Localize.KeyFormat("Menu.Career.Sheet.MemberFullCombos", member.FullCombos));
            return Localize.KeyFormat("Menu.Career.Sheet.Member", name, string.Join("  ·  ", parts));
        }

        /// <summary>
        /// The history as text, one entry per gig (newest first), after a note on how fans count.
        /// </summary>
        public static List<string> HistoryLines(CareerBand band, CareerBandSheet sheet)
        {
            var lines = new List<string>();
            if (sheet.History.Count == 0)
            {
                lines.Add(Localize.Key("Menu.Career.Sheet.NoGigs"));
                return lines;
            }

            if (sheet.History.Any(gig => gig.UsesFans))
            {
                lines.Add($"<i>{Localize.Key("Menu.Career.Sheet.HistoryNote")}</i>");
            }

            lines.AddRange(sheet.History.Select(gig => GigLine(band, gig)));
            return lines;
        }

        private static string GigLine(CareerBand band, CareerBandSheet.Gig gig)
        {
            var song = gig.Record?.SongName ?? gig.Song?.SongEntry?.Name ?? Localize.Key("Menu.Career.Sheet.UnknownSong");
            var artist = gig.Record?.SongArtist ?? gig.Song?.SongEntry?.Artist;
            var title = string.IsNullOrWhiteSpace(artist)
                ? $"<b>{song}</b>"
                : Localize.KeyFormat("Menu.Career.Sheet.GigSong", song, artist);

            // Where: date, career, tier (venue, city), hometown tag.
            var where = new List<string>
            {
                gig.Date.ToString("g", CultureInfo.CurrentCulture),
                gig.Career.Name,
            };
            if (gig.Tier is not null)
            {
                var venue = VenueText(gig.Tier);
                where.Add(venue is null ? gig.Tier.Name : Localize.KeyFormat("Menu.Career.Sheet.GigTier", gig.Tier.Name, venue));
                if (CareerBandSheet.IsHometown(gig.Tier.City, band.Hometown))
                {
                    where.Add(Localize.Key("Menu.Career.Hometown"));
                }
            }

            // How: stars, fans, contract.
            var how = new List<string>
            {
                gig.Gold
                    ? Localize.Key("Menu.Career.Sheet.GigGold")
                    : Localize.KeyFormat("Menu.Career.Sheet.GigStars", gig.Stars),
            };
            if (gig.UsesFans && gig.Tier is not null)
            {
                how.Add(Localize.KeyFormat(gig.Counts ? "Menu.Career.Sheet.GigFansCounts" : "Menu.Career.Sheet.GigFans",
                    gig.Fans.ToString("N0")));
            }

            if (gig.ContractType is { } type)
            {
                var current = gig.Tier is null ? null : CareerContracts.For(gig.Tier);
                var condition = current?.Type == type ? CareerContractText.Condition(current) : null;
                how.Add(condition is null
                    ? Localize.Key(gig.ContractMet ? "Menu.Career.Sheet.GigContractMet" : "Menu.Career.Sheet.GigContractMissed")
                    : Localize.KeyFormat(gig.ContractMet ? "Menu.Career.Contract.Met" : "Menu.Career.Contract.Missed", condition));
            }

            return $"{title}\n{string.Join("  ·  ", where)}\n{string.Join("  ·  ", how)}";
        }

        public static string RankName(CareerRank rank)
        {
            return Localize.Key("Menu.Career.Rank." + rank);
        }

        /// <summary>
        /// "Venue, City", either one alone, or null if the tier names neither.
        /// </summary>
        public static string VenueText(CareerTier tier)
        {
            var venue = tier?.VenueName;
            var city = tier?.City;

            if (Blank(venue))
            {
                return Blank(city) ? null : city;
            }

            return Blank(city) ? venue : Localize.KeyFormat("Menu.Career.Venue", venue, city);
        }

        private static bool Blank(string text) => string.IsNullOrWhiteSpace(text);

        /// <summary>
        /// A plain text line in the dialog's scrolling list, made from a copy of the dialog's title text
        /// (so it has the menu font) and reachable with a controller so the list can be scrolled.
        /// </summary>
        private static void AddLine(ListDialog dialog, string text, float size)
        {
            var line = dialog.AddListEntry(dialog.Title);
            line.name = "Line";
            line.text = text;
            line.richText = true;
            line.enableAutoSizing = false;
            line.fontSize = size;
            line.fontStyle = FontStyles.Normal;
            line.alignment = TextAlignmentOptions.TopLeft;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.overflowMode = TextOverflowModes.Overflow;
            line.color = MenuData.Colors.BrightText;

            // The list sets the width; the height follows the text.
            var fitter = line.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var group = dialog.GetComponentInChildren<NavigationGroup>();
            if (group != null)
            {
                group.AddNavigatable(RuntimeNavigatable.Attach(line.gameObject, null));
            }
        }

        /// <summary>
        /// The stock list dialog does not scroll to the selected entry, so keep it in view when the
        /// selection moves with the controller.
        /// </summary>
        private static void FollowSelection(ListDialog dialog)
        {
            var scroll = dialog.GetComponentInChildren<ScrollRect>();
            var group = dialog.GetComponentInChildren<NavigationGroup>();
            if (scroll == null || group == null || scroll.content == null)
            {
                return;
            }

            group.SelectionChanged += (selected, origin) =>
            {
                if (selected == null || origin != SelectionOrigin.Navigation ||
                    !selected.transform.IsChildOf(scroll.content))
                {
                    return;
                }

                Canvas.ForceUpdateCanvases();
                var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform) scroll.transform;
                var view = viewport.rect;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected.transform);

                var position = scroll.content.anchoredPosition.y;
                if (bounds.max.y > view.yMax)
                {
                    position -= bounds.max.y - view.yMax;
                }
                else if (bounds.min.y < view.yMin)
                {
                    position += view.yMin - bounds.min.y;
                }
                else
                {
                    return;
                }

                scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x,
                    Mathf.Clamp(position, 0f, scroll.ScrollableHeight()));
            };
        }

        private static async UniTaskVoid SwitchTo(System.Action show)
        {
            DialogManager.Instance.ClearDialog();
            await UniTask.Yield();
            show();
        }
    }
}
