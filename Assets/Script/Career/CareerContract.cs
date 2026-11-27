using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using YARG.Core;
using YARG.Core.Game;
using YARG.Scores;

namespace YARG.Career
{
    /// <summary>
    /// What a gig contract asks for. Phase A types are judged from one song's result alone (see
    /// <see cref="CareerContracts.IsMet"/>). Phase B types (forced modifiers, playlist gigs, ...) go after
    /// these; keep the stored numbers stable, completion rows store them.
    /// </summary>
    public enum CareerContractType
    {
        // No contract.
        None = 0,

        // Band stars of at least Parameter (1..5, 6 = gold).
        MinStars = 1,

        // Average note accuracy of the players, in percent (0..100), of at least Parameter.
        MinAccuracy = 2,

        // Full combos: Parameter 0 = every player, N >= 1 = at least N players (1 = any one player).
        FullCombo = 3,

        // Every player on difficulty Parameter or above (YARG.Core.Difficulty: 0 Beginner .. 3 Hard,
        // 4 Expert, 5 Expert+).
        MinDifficulty = 4,

        // "Clean show": the no-fail setting is Off for the play. Parameter unused.
        NoFailOff = 5,

        // At least Parameter human players.
        FullBand = 6,
    }

    /// <summary>
    /// An optional condition on a tier (the gig), for every song of it, with a fan bonus when met. Failing
    /// a contract never blocks progress; the result just earns no bonus.
    /// </summary>
    public class CareerContract
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public CareerContractType Type;

        // Meaning depends on Type (see CareerContractType).
        public float Parameter;

        // Extra fans for a result that met the contract, in percent of the song's fans.
        public int BonusPercent;

        // Replaces the generated description of the condition, e.g. "Play it like you mean it".
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Text;

        public CareerContract()
        {
        }

        public CareerContract(CareerContract other)
        {
            Type = other.Type;
            Parameter = other.Parameter;
            BonusPercent = other.BonusPercent;
            Text = other.Text;
        }

        [JsonIgnore]
        public bool IsActive => Type != CareerContractType.None;
    }

    /// <summary>
    /// Contract rules, pure (no UnityEngine), covered by Tests/YARG.Career.Tests.
    /// </summary>
    public static class CareerContracts
    {
        /// <summary>
        /// The contract every song of the tier is played under, or null if it has no active one.
        /// </summary>
        public static CareerContract For(CareerTier tier)
        {
            return tier?.Contract is { IsActive: true } contract ? contract : null;
        }

        /// <summary>
        /// The contract of a tier by its id and index, as a <see cref="CareerContext"/> carries them.
        /// </summary>
        public static CareerContract For(CareerPreset career, Guid tierId, int tierIndex)
        {
            if (career?.Tiers is null)
            {
                return null;
            }

            var tier = tierIndex >= 0 && tierIndex < career.Tiers.Count && career.Tiers[tierIndex].Id == tierId
                ? career.Tiers[tierIndex]
                : career.Tiers.FirstOrDefault(t => t.Id == tierId);

            return For(tier);
        }

        /// <summary>
        /// Whether one play met the contract. <paramref name="players"/> are the human players' score rows
        /// of the play (everyone who played, band members and sit-ins alike); <paramref name="noFailOff"/>
        /// is whether the no-fail setting was Off, or null if unknown. Missing facts never meet a contract.
        /// </summary>
        public static bool IsMet(CareerContract contract, GameRecord record, IReadOnlyCollection<PlayerScoreRecord> players,
            bool? noFailOff)
        {
            if (contract is not { IsActive: true })
            {
                return false;
            }

            players ??= Array.Empty<PlayerScoreRecord>();

            switch (contract.Type)
            {
                case CareerContractType.MinStars:
                {
                    if (record is null)
                    {
                        return false;
                    }

                    var stars = record.BandStars == StarAmount.StarGold
                        ? CareerFans.GOLD_STARS
                        : record.BandStars.GetStarCount();
                    return stars >= contract.Parameter;
                }
                case CareerContractType.MinAccuracy:
                {
                    // Players without notes have no accuracy and are left out of the average.
                    var percents = players
                        .Where(HasNotes)
                        .Select(player => player.GetPercent())
                        .Where(percent => !float.IsNaN(percent))
                        .ToList();
                    return percents.Count > 0 && percents.Average() * 100f >= contract.Parameter - 0.0001f;
                }
                case CareerContractType.FullCombo:
                {
                    // A player without notes cannot full combo, and does not hold back "every player".
                    var withNotes = players.Where(HasNotes).ToList();
                    var fullCombos = withNotes.Count(player => player.IsFc);
                    var needed = (int) Math.Ceiling(contract.Parameter);
                    return needed <= 0
                        ? withNotes.Count > 0 && fullCombos == withNotes.Count
                        : fullCombos >= needed;
                }
                case CareerContractType.MinDifficulty:
                {
                    var minimum = (int) Math.Ceiling(contract.Parameter);
                    return players.Count > 0 && players.All(player => (int) player.Difficulty >= minimum);
                }
                case CareerContractType.NoFailOff:
                    return noFailOff == true;
                case CareerContractType.FullBand:
                {
                    var count = players.Select(player => player.PlayerId).Distinct().Count();
                    return count > 0 && count >= contract.Parameter;
                }
                default:
                    // A type this build does not know (newer content) is never met.
                    return false;
            }
        }

        /// <summary>
        /// Write the raw contract outcome of a play onto its completion commit.
        /// </summary>
        public static void Apply(CareerSongCompletionCommit commit, CareerContract contract, GameRecord record,
            IReadOnlyCollection<PlayerScoreRecord> players, bool? noFailOff)
        {
            commit.ContractType = contract?.IsActive == true ? contract.Type : null;
            commit.ContractMet = IsMet(contract, record, players, noFailOff);
        }

        /// <summary>
        /// Whether a stored completion met <paramref name="contract"/> as the content declares it now. A
        /// row stores the contract type it was judged under; if the content has since changed to another
        /// type the old result no longer counts (a changed parameter is not re-judged).
        /// </summary>
        public static bool RowMet(CareerContract contract, CareerSongCompletions row)
        {
            return contract is { IsActive: true } && row is { ContractMet: true } && row.ContractType == contract.Type;
        }

        private static bool HasNotes(PlayerScoreRecord player)
        {
            return player.NotesHit + player.NotesMissed > 0;
        }
    }
}
