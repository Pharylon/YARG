using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using YARG.Core;
using YARG.Core.Logging;

namespace YARG.Career
{
    public enum CareerIssueType
    {
        // A tier can never open: its fan, star or completion requirement is more than the tiers that can
        // open before it can ever give (gold stars and every contract bonus counted).
        UnreachableTier,

        // A fan-count tier that needs fans while no tier that can open before it has a song that earns any.
        NoFanSongs,

        // A contract's parameter is outside what its type accepts (see CareerContractType).
        ContractParameterOutOfRange,

        // A contract with a negative fan bonus (counted as 0).
        NegativeContractBonus,

        // A tier's FanMultiplier is negative (counted as no fans).
        NegativeFanMultiplier,

        // Two songs of the career share a career song Id, so they share progress.
        DuplicateSongId,

        // Two tiers of the career share a tier Id.
        DuplicateTierId,

        // CompletionMode FullCareerPlaylist, which gameplay does not support: the tier is played song by song.
        CareerPlaylistUnsupported,

        // MinSongSpeed below 100 (scores below 100% speed are never saved, so no song could count) or
        // above 5000. Value: the speed.
        SongSpeedOutOfRange,

        // SetlistSize below 1 or above the number of set songs (encores not counted). Value: the size;
        // Available: the set songs.
        SetlistSizeOutOfRange,

        // Encore songs that can never be played: the tier is not played as a show, or has no EncoreMinStars
        // (or one above 5). They stay hidden for good.
        EncoreNeverPlayed,

        // EncoreMinStars set on a tier without encore songs, or outside 0..5. Value: the bar.
        EncoreBarInvalid,
    }

    /// <summary>
    /// One content mistake found by <see cref="CareerValidation.Validate"/>. The numbers are raw so the log
    /// and the preset editor can word it their own way.
    /// </summary>
    public sealed class CareerIssue
    {
        public CareerIssueType Type;
        public int TierIndex;
        // -1 for issues of the tier itself.
        public int SongIndex = -1;

        // UnreachableTier: what the tier needs and the most it can get (fans, stars or songs, by its
        // unlock type). NoFanSongs: Required only.
        public UnlockType UnlockType;
        public int Required;
        public int Available;

        // Contract issues: the contract's type and parameter (or bonus).
        public CareerContractType ContractType;
        public float Value;

        /// <summary>English, for the log.</summary>
        public string Describe(CareerPreset career)
        {
            var tier = career?.Tiers != null && TierIndex >= 0 && TierIndex < career.Tiers.Count
                ? career.Tiers[TierIndex]
                : null;
            var where = $"tier {TierIndex + 1} '{tier?.Name}'" + (SongIndex >= 0 ? $", song {SongIndex + 1}" : "");

            return Type switch
            {
                CareerIssueType.UnreachableTier => UnlockType switch
                {
                    UnlockType.FanCount => $"{where} needs {Required:N0} fans, but the tiers that can open before it " +
                        $"give at most {Available:N0} (gold stars and every contract bonus counted). It can never open.",
                    UnlockType.StarCount => $"{where} needs {Required:N0} stars, but the regular tiers before it " +
                        $"that can open hold at most {Available:N0}. It can never open.",
                    _ => $"{where} needs {Required:N0} completed songs, but the regular tiers before it that can " +
                        $"open hold only {Available:N0}. It can never open.",
                },
                CareerIssueType.NoFanSongs => $"{where} needs {Required:N0} fans, but no tier that can open before " +
                    "it has a song that earns fans. It can never open.",
                CareerIssueType.ContractParameterOutOfRange => $"{where}: contract {ContractType} has parameter " +
                    $"{Value.ToString(CultureInfo.InvariantCulture)}, expected {CareerValidation.ParameterRange(ContractType)}.",
                CareerIssueType.NegativeContractBonus => $"{where}: contract {ContractType} has a negative fan " +
                    $"bonus ({Value.ToString(CultureInfo.InvariantCulture)}%), which counts as 0.",
                CareerIssueType.NegativeFanMultiplier => $"{where}: FanMultiplier " +
                    $"{Value.ToString(CultureInfo.InvariantCulture)} is negative, so its songs earn no fans.",
                CareerIssueType.DuplicateSongId => $"{where} uses a career song Id that an earlier song already " +
                    "uses, so the two share their progress.",
                CareerIssueType.DuplicateTierId => $"{where} uses a tier Id that an earlier tier already uses.",
                CareerIssueType.CareerPlaylistUnsupported => $"{where}: CompletionMode FullCareerPlaylist is not " +
                    "supported yet; its songs are played one by one.",
                CareerIssueType.SongSpeedOutOfRange => $"{where}: MinSongSpeed {Value.ToString(CultureInfo.InvariantCulture)} " +
                    "is outside 100..5000 (below 100% no score is saved, so no song could count).",
                CareerIssueType.SetlistSizeOutOfRange => $"{where}: SetlistSize {Value.ToString(CultureInfo.InvariantCulture)} " +
                    $"is outside 1..{Available} (the songs of its set, encores not counted).",
                CareerIssueType.EncoreNeverPlayed => $"{where} is an encore that can never be played: its tier needs " +
                    "CompletionMode FullTierPlaylist and an EncoreMinStars from 0 to 5.",
                CareerIssueType.EncoreBarInvalid => $"{where}: EncoreMinStars {Value.ToString(CultureInfo.InvariantCulture)} " +
                    "needs encore songs (IsEncore) and a value from 0 to 5.",
                _ => $"{where}: {Type}",
            };
        }
    }

    /// <summary>
    /// Cheap checks for career content mistakes, pure (no UnityEngine), covered by Tests/YARG.Career.Tests.
    /// Warnings only: nothing here stops a career from loading or being played.
    /// </summary>
    public static class CareerValidation
    {
        public static List<CareerIssue> Validate(CareerPreset career)
        {
            var issues = new List<CareerIssue>();
            if (career?.Tiers is null || career.Tiers.Count == 0)
            {
                return issues;
            }

            CheckReachability(career, issues);
            CheckContent(career, issues);

            return issues
                .OrderBy(issue => issue.TierIndex)
                .ThenBy(issue => issue.SongIndex)
                .ToList();
        }

        /// <summary>
        /// Opens tiers the way <see cref="CareerEvaluation"/> does, but with every song of every open tier
        /// at its best possible result (gold, contract met), until nothing more opens. Tiers still closed
        /// then can never open.
        /// </summary>
        private static void CheckReachability(CareerPreset career, List<CareerIssue> issues)
        {
            var tiers = career.Tiers;
            var open = new bool[tiers.Count];
            open[0] = true;

            bool changed;
            do
            {
                changed = false;
                var fans = 0;
                for (var i = 0; i < tiers.Count; i++)
                {
                    if (open[i])
                    {
                        fans += MaxTierFans(tiers[i]);
                    }
                }

                for (var i = 1; i < tiers.Count; i++)
                {
                    if (!open[i] && Requirement(tiers, open, i, fans).met)
                    {
                        open[i] = true;
                        changed = true;
                    }
                }
            } while (changed);

            var finalFans = Enumerable.Range(0, tiers.Count).Where(i => open[i]).Sum(i => MaxTierFans(tiers[i]));
            for (var i = 1; i < tiers.Count; i++)
            {
                if (open[i])
                {
                    continue;
                }

                var (_, available) = Requirement(tiers, open, i, finalFans);
                var type = tiers[i].UnlockType == UnlockType.FanCount && available == 0
                    ? CareerIssueType.NoFanSongs
                    : CareerIssueType.UnreachableTier;
                issues.Add(new CareerIssue
                {
                    Type = type,
                    TierIndex = i,
                    UnlockType = tiers[i].UnlockType,
                    Required = tiers[i].UnlockCriteria,
                    Available = available,
                });
            }
        }

        /// <summary>
        /// Whether tier <paramref name="index"/> opens given which tiers are open and the fans they can
        /// give, and the most its unlock type can count.
        /// </summary>
        private static (bool met, int available) Requirement(IReadOnlyList<CareerTier> tiers, bool[] open, int index,
            int fans)
        {
            var tier = tiers[index];
            if (tier.UnlockType == UnlockType.FanCount)
            {
                return (fans >= tier.UnlockCriteria, fans);
            }

            // Star and completion tiers count the regular tiers before them (bonus tiers do not count), and
            // stay locked if there is none (see CareerEvaluation).
            var anyRegularBefore = false;
            var available = 0;
            for (var i = 0; i < index; i++)
            {
                if (tiers[i].IsBonus)
                {
                    continue;
                }

                anyRegularBefore = true;
                if (open[i])
                {
                    var songs = tiers[i].Songs?.Count(song => CareerSetlists.CanBePlayed(tiers[i], song)) ?? 0;
                    available += tier.UnlockType == UnlockType.StarCount ? songs * 5 : songs;
                }
            }

            return (anyRegularBefore && available >= tier.UnlockCriteria, available);
        }

        /// <summary>The most fans a tier's songs can earn: gold on every song, every contract met.</summary>
        public static int MaxTierFans(CareerTier tier)
        {
            if (tier.Songs is null)
            {
                return 0;
            }

            var total = 0;
            foreach (var song in tier.Songs)
            {
                // An encore that can never be played earns nothing.
                if (!CareerSetlists.CanBePlayed(tier, song))
                {
                    continue;
                }

                var contract = CareerContracts.For(tier, song);
                total += Math.Max(0, CareerFans.SongFans(tier, CareerFans.GOLD_STARS, contract?.BonusPercent ?? 0));
            }

            return total;
        }

        private static void CheckContent(CareerPreset career, List<CareerIssue> issues)
        {
            var tierIds = new HashSet<Guid>();
            var songIds = new HashSet<Guid>();

            for (var tierIndex = 0; tierIndex < career.Tiers.Count; tierIndex++)
            {
                var tier = career.Tiers[tierIndex];

                if (tier.Id != Guid.Empty && !tierIds.Add(tier.Id))
                {
                    issues.Add(new CareerIssue { Type = CareerIssueType.DuplicateTierId, TierIndex = tierIndex });
                }

                if (tier.FanMultiplier is < 0f)
                {
                    issues.Add(new CareerIssue
                    {
                        Type = CareerIssueType.NegativeFanMultiplier,
                        TierIndex = tierIndex,
                        Value = tier.FanMultiplier.Value,
                    });
                }

                CheckContract(tier.Contract, tierIndex, -1, issues);

                if (tier.CompletionMode == CareerCompletionMode.FullCareerPlaylist)
                {
                    issues.Add(new CareerIssue { Type = CareerIssueType.CareerPlaylistUnsupported, TierIndex = tierIndex });
                }

                CheckSetlist(tier, tierIndex, issues);

                if (tier.Songs is null)
                {
                    continue;
                }

                for (var songIndex = 0; songIndex < tier.Songs.Length; songIndex++)
                {
                    var song = tier.Songs[songIndex];
                    if (song is null)
                    {
                        continue;
                    }

                    if (song.Id != Guid.Empty && !songIds.Add(song.Id))
                    {
                        issues.Add(new CareerIssue
                        {
                            Type = CareerIssueType.DuplicateSongId,
                            TierIndex = tierIndex,
                            SongIndex = songIndex,
                        });
                    }

                    // A song contract on a tier without one is fine, and so is a None that exempts the song.
                    CheckContract(song.Contract, tierIndex, songIndex, issues);
                }
            }
        }

        /// <summary>Gig rules, make-your-own-setlist size and encores.</summary>
        private static void CheckSetlist(CareerTier tier, int tierIndex, List<CareerIssue> issues)
        {
            if (tier.MinSongSpeed is { } speed && speed is < 100 or > 5000)
            {
                issues.Add(new CareerIssue { Type = CareerIssueType.SongSpeedOutOfRange, TierIndex = tierIndex, Value = speed });
            }

            var setSongs = CareerSetlists.SetSongIndexes(tier).Count;
            if (tier.SetlistSize is { } size && (size < 1 || size > setSongs))
            {
                issues.Add(new CareerIssue
                {
                    Type = CareerIssueType.SetlistSizeOutOfRange,
                    TierIndex = tierIndex,
                    Value = size,
                    Available = setSongs,
                });
            }

            var encores = CareerSetlists.EncoreSongIndexes(tier);
            if (tier.EncoreMinStars is { } bar && (encores.Count == 0 || bar < 0f || bar > 5f))
            {
                issues.Add(new CareerIssue { Type = CareerIssueType.EncoreBarInvalid, TierIndex = tierIndex, Value = bar });
            }

            if (!CareerSetlists.HasEncore(tier))
            {
                foreach (var songIndex in encores)
                {
                    issues.Add(new CareerIssue { Type = CareerIssueType.EncoreNeverPlayed, TierIndex = tierIndex, SongIndex = songIndex });
                }
            }
        }

        private static void CheckContract(CareerContract contract, int tierIndex, int songIndex,
            List<CareerIssue> issues)
        {
            if (contract is not { IsActive: true })
            {
                return;
            }

            if (!ParameterInRange(contract.Type, contract.Parameter))
            {
                issues.Add(new CareerIssue
                {
                    Type = CareerIssueType.ContractParameterOutOfRange,
                    TierIndex = tierIndex,
                    SongIndex = songIndex,
                    ContractType = contract.Type,
                    Value = contract.Parameter,
                });
            }

            if (contract.BonusPercent < 0)
            {
                issues.Add(new CareerIssue
                {
                    Type = CareerIssueType.NegativeContractBonus,
                    TierIndex = tierIndex,
                    SongIndex = songIndex,
                    ContractType = contract.Type,
                    Value = contract.BonusPercent,
                });
            }
        }

        /// <summary>
        /// Whether <paramref name="parameter"/> means something for <paramref name="type"/> (see
        /// <see cref="CareerContractType"/>). Unknown types are not checked.
        /// </summary>
        public static bool ParameterInRange(CareerContractType type, float parameter)
        {
            var whole = Math.Abs(parameter - Math.Round(parameter)) < 0.0001f;
            return type switch
            {
                CareerContractType.MinStars      => whole && parameter is >= 1 and <= CareerFans.GOLD_STARS,
                CareerContractType.MinAccuracy   => parameter is >= 0 and <= 100,
                CareerContractType.FullCombo     => whole && parameter >= 0,
                CareerContractType.MinDifficulty => whole && parameter >= (int) Difficulty.Beginner &&
                    parameter <= (int) Difficulty.ExpertPlus,
                CareerContractType.FullBand      => whole && parameter >= 1,
                // NoFailOff ignores its parameter.
                _                                => true,
            };
        }

        /// <summary>What <see cref="ParameterInRange"/> accepts, in words, for the log.</summary>
        public static string ParameterRange(CareerContractType type)
        {
            return type switch
            {
                CareerContractType.MinStars      => "a whole number of stars from 1 to 5, or 6 for gold",
                CareerContractType.MinAccuracy   => "a percentage from 0 to 100",
                CareerContractType.FullCombo     => "0 (every player) or a whole number of players",
                CareerContractType.MinDifficulty => "a difficulty from 0 (Beginner) to 5 (Expert+)",
                CareerContractType.FullBand      => "a whole number of players, 1 or more",
                _                                => "any value",
            };
        }
    }

    /// <summary>
    /// Writes <see cref="CareerValidation"/> warnings to the log, where a career author sees them when the
    /// careers load (and when a custom career file changes while the preset editor is open).
    /// </summary>
    public static class CareerValidationLog
    {
        public static void LogAll(IEnumerable<CareerPreset> careers)
        {
            foreach (var career in careers)
            {
                Log(career);
            }
        }

        public static int Log(CareerPreset career)
        {
            List<CareerIssue> issues;
            try
            {
                issues = CareerValidation.Validate(career);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Career validation failed for '{career?.Name}'.");
                return 0;
            }

            foreach (var issue in issues)
            {
                YargLogger.LogFormatWarning<string, string>("Career '{0}': {1}", career.Name, issue.Describe(career));
            }

            return issues.Count;
        }
    }
}
