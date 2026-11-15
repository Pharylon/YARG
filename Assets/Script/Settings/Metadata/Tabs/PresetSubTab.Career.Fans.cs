using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using YARG.Career;
using YARG.Localization;
using YARG.Settings.Types;

namespace YARG.Settings.Metadata
{
    /// <summary>
    /// Career editor fields for fans, venues and contracts (tier: fan multiplier, city, venue name, contract;
    /// song: contract) and the content warnings of <see cref="CareerValidation"/>. Built from the same stock
    /// setting rows as the other editor fields; no prefab of its own.
    /// </summary>
    public partial class CareerPresetSubTab
    {
        // Song contract choice that keeps the tier's contract (CareerSong.Contract = null).
        private const int TIER_CONTRACT = -1;

        private sealed class CareerChoiceSetting : DropdownSetting<int>
        {
            private readonly Func<int, string> _formatter;

            public CareerChoiceSetting(int value, Action<int> onChange, Func<int, string> formatter)
                : base(value, onChange, localizable: false)
            {
                _formatter = formatter;
            }

            public override string ValueToString(int value) => _formatter(value);
        }

        /// <summary>Fan multiplier, city, venue name and contract fields of the tier editor.</summary>
        private IEnumerable<CareerPresetPreviewBuilder.Field> TierFanFields(CareerTier tier, bool editable,
            Action onInputBlur)
        {
            yield return new CareerPresetPreviewBuilder.Field(FieldText("FanMultiplier"), new StringSetting(FormatNumber(tier.FanMultiplier), value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    tier.FanMultiplier = null;
                }
                else if (TryParseNumber(value, out var multiplier) && multiplier >= 0f)
                {
                    tier.FanMultiplier = multiplier;
                }
                else
                {
                    // Not a number: keep the saved value.
                    return;
                }

                Save();
            }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("City"), new StringSetting(tier.City ?? string.Empty, value =>
            {
                tier.City = string.IsNullOrWhiteSpace(value) ? null : value;
                Save();
            }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("VenueName"), new StringSetting(tier.VenueName ?? string.Empty, value =>
            {
                tier.VenueName = string.IsNullOrWhiteSpace(value) ? null : value;
                Save();
            }), editable);

            var contractType = new CareerEnumSetting<CareerContractType>(
                tier.Contract?.Type ?? CareerContractType.None, value =>
                {
                    if (value == CareerContractType.None)
                    {
                        tier.Contract = null;
                    }
                    else
                    {
                        tier.Contract ??= new CareerContract { BonusPercent = DEFAULT_CONTRACT_BONUS };
                        SetContractType(tier.Contract, value);
                    }

                    Save();
                    // The parameter's label, value and visibility depend on the type.
                    ShowEditTierDialog(tier, onInputBlur, FieldText("ContractType"));
                }, FormatEnumValue);
            foreach (CareerContractType type in Enum.GetValues(typeof(CareerContractType)))
            {
                contractType.Add(type);
            }

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ContractType"), contractType, editable);

            foreach (var field in ContractDetailFields(tier.Contract, editable))
            {
                yield return field;
            }
        }

        /// <summary>The contract field of the song editor, and its details for a song's own contract.</summary>
        private IEnumerable<CareerPresetPreviewBuilder.Field> SongContractFields(SongFilterState state, bool editable)
        {
            var song = state.CareerSong;
            var current = song.Contract is null ? TIER_CONTRACT : (int) song.Contract.Type;

            var choice = new CareerChoiceSetting(current, value =>
            {
                if (value == TIER_CONTRACT)
                {
                    song.Contract = null;
                }
                else
                {
                    var type = (CareerContractType) value;
                    song.Contract ??= new CareerContract
                    {
                        BonusPercent = type == CareerContractType.None ? 0 : DEFAULT_CONTRACT_BONUS,
                    };
                    SetContractType(song.Contract, type);
                }

                Save();
                state.SummaryChanged?.Invoke();
                ShowSongFields(state, SongFieldIndex(FieldText("SongContract")));
            }, value => value switch
            {
                TIER_CONTRACT                   => SongTierContractText(song),
                (int) CareerContractType.None   => EditorText("SongContractNone"),
                _                               => FormatEnumValue((CareerContractType) value),
            });

            choice.Add(TIER_CONTRACT);
            foreach (CareerContractType type in Enum.GetValues(typeof(CareerContractType)))
            {
                choice.Add((int) type);
            }

            yield return new CareerPresetPreviewBuilder.Field(FieldText("SongContract"), choice, editable);

            foreach (var field in ContractDetailFields(song.Contract, editable))
            {
                yield return field;
            }
        }

        private const int DEFAULT_CONTRACT_BONUS = 25;

        private static void SetContractType(CareerContract contract, CareerContractType type)
        {
            contract.Type = type;
            if (!CareerValidation.ParameterInRange(type, contract.Parameter))
            {
                contract.Parameter = type switch
                {
                    CareerContractType.MinStars      => 4,
                    CareerContractType.MinAccuracy   => 90,
                    CareerContractType.FullCombo     => 1,
                    CareerContractType.MinDifficulty => 3,
                    CareerContractType.FullBand      => 2,
                    _                                => 0,
                };
            }
        }

        /// <summary>Parameter, fan bonus and text of an active contract (none for no contract).</summary>
        private IEnumerable<CareerPresetPreviewBuilder.Field> ContractDetailFields(CareerContract contract,
            bool editable)
        {
            if (contract is not { IsActive: true })
            {
                yield break;
            }

            if (contract.Type != CareerContractType.NoFailOff)
            {
                yield return new CareerPresetPreviewBuilder.Field(FieldText($"ContractParameter.{contract.Type}"),
                    new StringSetting(FormatNumber(contract.Parameter), value =>
                    {
                        if (TryParseNumber(value, out var parameter))
                        {
                            contract.Parameter = parameter;
                            Save();
                        }
                    }), editable);
            }

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ContractBonus"), new IntSetting(contract.BonusPercent, min: 0, max: 1000,
                onChange: value =>
                {
                    contract.BonusPercent = value;
                    Save();
                }), editable);

            yield return new CareerPresetPreviewBuilder.Field(FieldText("ContractText"), new StringSetting(contract.Text ?? string.Empty, value =>
            {
                contract.Text = string.IsNullOrWhiteSpace(value) ? null : value;
                Save();
            }), editable);
        }

        private string SongTierContractText(CareerSong song)
        {
            var tier = _presetRef?.Tiers.FirstOrDefault(t => t.Songs != null && t.Songs.Contains(song));
            return tier?.Contract is { IsActive: true } contract
                ? Localize.KeyFormat(CAREER_EDITOR_KEY + ".SongContractTier", FormatEnumValue(contract.Type))
                : EditorText("SongContractTierNone");
        }

        private int? SongFieldIndex(string label)
        {
            // Song editor order: four facets, Clear (a button, counted only when shown), identifier,
            // fallback description, then the contract fields.
            return label == FieldText("SongContract") ? (ReadOnlyFields ? 6 : 7) : null;
        }

        /// <summary>Read-only warning rows for the tier's content mistakes, shown first in its editor.</summary>
        private IEnumerable<CareerPresetPreviewBuilder.Field> TierWarningFields(CareerTier tier)
        {
            foreach (var issue in TierIssues(tier))
            {
                yield return new CareerPresetPreviewBuilder.Field(FieldText("Warning"), new StringSetting(DescribeIssue(issue)), editable: false);
            }
        }

        /// <summary>" [!] ..." after the tier's summary row when it has a content mistake.</summary>
        private string TierWarningSummary(CareerTier tier)
        {
            var issue = TierIssues(tier).FirstOrDefault();
            return issue is null ? string.Empty : Localize.KeyFormat(CAREER_EDITOR_KEY + ".WarningSummary",
                DescribeIssue(issue));
        }

        private List<CareerIssue> TierIssues(CareerTier tier)
        {
            if (_presetRef is null)
            {
                return new List<CareerIssue>();
            }

            var index = _presetRef.Tiers.ToList().IndexOf(tier);
            return CareerValidation.Validate(_presetRef).Where(issue => issue.TierIndex == index).ToList();
        }

        private static string DescribeIssue(CareerIssue issue)
        {
            const string KEY = CAREER_EDITOR_KEY + ".Issue.";
            var text = issue.Type switch
            {
                CareerIssueType.UnreachableTier => Localize.KeyFormat(
                    KEY + "Unreachable." + issue.UnlockType,
                    issue.Required.ToString("N0"), issue.Available.ToString("N0")),
                CareerIssueType.NoFanSongs => Localize.KeyFormat(KEY + "NoFanSongs", issue.Required.ToString("N0")),
                CareerIssueType.ContractParameterOutOfRange => Localize.KeyFormat(KEY + "ContractParameter",
                    FormatNumber(issue.Value), Localize.Key(KEY + "Range." + issue.ContractType)),
                CareerIssueType.NegativeContractBonus => Localize.KeyFormat(KEY + "NegativeBonus",
                    FormatNumber(issue.Value)),
                CareerIssueType.NegativeFanMultiplier => Localize.KeyFormat(KEY + "NegativeMultiplier",
                    FormatNumber(issue.Value)),
                CareerIssueType.DuplicateSongId => Localize.Key(KEY + "DuplicateSongId"),
                CareerIssueType.DuplicateTierId => Localize.Key(KEY + "DuplicateTierId"),
                CareerIssueType.CareerPlaylistUnsupported => Localize.Key(KEY + "CareerPlaylist"),
                CareerIssueType.SongSpeedOutOfRange => Localize.KeyFormat(KEY + "SongSpeed", FormatNumber(issue.Value)),
                CareerIssueType.SetlistSizeOutOfRange => Localize.KeyFormat(KEY + "SetlistSize",
                    FormatNumber(issue.Value), issue.Available),
                CareerIssueType.EncoreNeverPlayed => Localize.Key(KEY + "EncoreNeverPlayed"),
                CareerIssueType.EncoreBarInvalid => Localize.KeyFormat(KEY + "EncoreBar", FormatNumber(issue.Value)),
                _ => issue.Type.ToString(),
            };

            return issue.SongIndex >= 0
                ? Localize.KeyFormat(KEY + "Song", issue.SongIndex + 1, text)
                : text;
        }

        private static string FormatNumber(float? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        private static bool TryParseNumber(string text, out float value)
        {
            return float.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture,
                out value) && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
