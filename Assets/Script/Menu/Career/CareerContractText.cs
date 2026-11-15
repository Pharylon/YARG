using System.Globalization;
using System.Linq;
using YARG.Career;
using YARG.Core;
using YARG.Localization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// One-line descriptions of gig contracts for the career list, the song's difficulty select and the
    /// score screen ("Clean show: no-fail off, +25% fans").
    /// </summary>
    public static class CareerContractText
    {
        /// <summary>
        /// The condition alone ("Clean show: no-fail off"), or the content's own text if it has one.
        /// </summary>
        public static string Condition(CareerContract contract)
        {
            if (contract is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(contract.Text))
            {
                return contract.Text;
            }

            var count = (int) System.Math.Ceiling(contract.Parameter);
            return contract.Type switch
            {
                CareerContractType.MinStars when contract.Parameter >= CareerFans.GOLD_STARS =>
                    Localize.Key("Menu.Career.Contract.MinStarsGold"),
                CareerContractType.MinStars =>
                    Localize.KeyFormat("Menu.Career.Contract.MinStars", count),
                CareerContractType.MinAccuracy =>
                    Localize.KeyFormat("Menu.Career.Contract.MinAccuracy",
                        contract.Parameter.ToString("0.#", CultureInfo.CurrentCulture)),
                CareerContractType.FullCombo when count <= 0 =>
                    Localize.Key("Menu.Career.Contract.FullComboAll"),
                CareerContractType.FullCombo when count == 1 =>
                    Localize.Key("Menu.Career.Contract.FullComboAny"),
                CareerContractType.FullCombo =>
                    Localize.KeyFormat("Menu.Career.Contract.FullComboCount", count),
                CareerContractType.MinDifficulty =>
                    Localize.KeyFormat("Menu.Career.Contract.MinDifficulty",
                        ((Difficulty) System.Math.Clamp(count, 0, (int) Difficulty.ExpertPlus)).ToLocalizedName()),
                CareerContractType.NoFailOff =>
                    Localize.Key("Menu.Career.Contract.NoFailOff"),
                CareerContractType.FullBand =>
                    Localize.KeyFormat("Menu.Career.Contract.FullBand", count),
                _ => Localize.Key("Menu.Career.Contract.Unknown"),
            };
        }

        /// <summary>
        /// The condition and its bonus: "Clean show: no-fail off, +25% fans". Null for no contract. The
        /// bonus is left out for careers that do not use fans.
        /// </summary>
        public static string Line(CareerContract contract, bool usesFans)
        {
            if (contract is not { IsActive: true })
            {
                return null;
            }

            return contract.BonusPercent > 0 && usesFans
                ? Localize.KeyFormat("Menu.Career.Contract.Line", Condition(contract), contract.BonusPercent)
                : Condition(contract);
        }

        /// <summary>
        /// "Contract: ..." for the song about to be played from the career menu, or null if it is not a
        /// career song or has no contract.
        /// </summary>
        public static string ForCurrentSong()
        {
            var context = GlobalVariables.State.CurrentCareer;
            if (context is null)
            {
                return null;
            }

            var career = CareerBands.AllCareers().FirstOrDefault(c => c.Id == context.CareerId);
            var line = Line(CareerContracts.For(career, context.TierId, context.TierIndex, context.CareerSongId),
                CareerFans.UsesFans(career));
            var contract = line is null ? null : Localize.KeyFormat("Menu.Career.Contract.Upcoming", line);

            // The gig's rules (forced modifiers, minimum speed) go on the same line.
            var rules = CareerSetlistText.RulesLine(career?.Tiers.FirstOrDefault(tier => tier.Id == context.TierId));
            if (rules is null)
            {
                return contract;
            }

            rules = Localize.KeyFormat("Menu.Career.Show.Rules", rules);
            return contract is null ? rules : Localize.KeyFormat("Menu.Career.HeaderJoin", contract, rules);
        }

        /// <summary>
        /// The score screen's contract part: "Contract met: ... (+25% fans)" or "Contract missed: ...".
        /// </summary>
        public static string Outcome(CareerSongOutcome outcome)
        {
            if (outcome?.Contract is not { IsActive: true } contract)
            {
                return null;
            }

            if (!outcome.ContractMet)
            {
                return Localize.KeyFormat("Menu.Career.Contract.Missed", Condition(contract));
            }

            return contract.BonusPercent > 0 && outcome.UsesFans
                ? Localize.KeyFormat("Menu.Career.Contract.MetBonus", Condition(contract), contract.BonusPercent)
                : Localize.KeyFormat("Menu.Career.Contract.Met", Condition(contract));
        }
    }
}
