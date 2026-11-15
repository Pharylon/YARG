using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Career;
using YARG.Core.Game;
using YARG.Settings.Types;

namespace YARG.Settings.Metadata
{
    /// <summary>
    /// Career editor fields for the Phase B gig conditions (tier: forced modifiers, minimum song speed,
    /// mystery setlist, setlist size, encore star bar; song: encore). Same stock setting rows as the other
    /// fields (see PresetSubTab.Career.Fans.cs); the session length (playlist) field already exists.
    /// </summary>
    public partial class CareerPresetSubTab
    {
        private IEnumerable<CareerPresetPreviewBuilder.Field> TierSetlistFields(CareerTier tier, bool editable)
        {
            // Modifier names, comma separated (e.g. "AllStrums, NoKicks"); empty for none. Anything that is
            // not a modifier name keeps the saved value.
            yield return new CareerPresetPreviewBuilder.Field(FieldText("ForcedModifiers"),
                new StringSetting(FormatModifiers(tier.ForcedModifiers), value =>
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        tier.ForcedModifiers = null;
                    }
                    else if (Enum.TryParse<Modifier>(string.Join(",", value.Split(new[] { ',', ' ' },
                        StringSplitOptions.RemoveEmptyEntries)), true, out var modifiers))
                    {
                        tier.ForcedModifiers = modifiers == Modifier.None ? null : modifiers;
                    }
                    else
                    {
                        return;
                    }

                    Save();
                }), editable);

            // 0 = no minimum.
            yield return new CareerPresetPreviewBuilder.Field(FieldText("MinSongSpeed"),
                new IntSetting(tier.MinSongSpeed ?? 0, min: 0, max: 5000, onChange: value =>
                {
                    tier.MinSongSpeed = value <= 0 ? null : value;
                    Save();
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("Mystery"), new ToggleSetting(tier.Mystery, value =>
            {
                tier.Mystery = value;
                Save();
            }), editable);

            // 0 = the whole set.
            yield return new CareerPresetPreviewBuilder.Field(FieldText("SetlistSize"),
                new IntSetting(tier.SetlistSize ?? 0, min: 0, max: 100, onChange: value =>
                {
                    tier.SetlistSize = value <= 0 ? null : value;
                    Save();
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("EncoreMinStars"),
                new StringSetting(FormatNumber(tier.EncoreMinStars), value =>
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        tier.EncoreMinStars = null;
                    }
                    else if (TryParseNumber(value, out var stars))
                    {
                        tier.EncoreMinStars = stars;
                    }
                    else
                    {
                        return;
                    }

                    Save();
                }), editable);
        }

        private IEnumerable<CareerPresetPreviewBuilder.Field> SongSetlistFields(SongFilterState state, bool editable)
        {
            var song = state.CareerSong;
            yield return new CareerPresetPreviewBuilder.Field(FieldText("IsEncore"), new ToggleSetting(song.IsEncore, value =>
            {
                song.IsEncore = value;
                Save();
                state.SummaryChanged?.Invoke();
            }), editable);
        }

        private static string FormatModifiers(Modifier? modifiers)
        {
            return modifiers is { } set && set != Modifier.None
                ? string.Join(", ", CareerGigRules.Flags(set).Select(modifier => modifier.ToString()))
                : string.Empty;
        }
    }
}
