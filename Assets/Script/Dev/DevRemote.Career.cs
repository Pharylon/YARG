#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using YARG.Career;
using YARG.Core;
using YARG.Core.Game;
using YARG.Menu.Career;
using YARG.Scores;

namespace YARG.Dev
{
    /// <summary>
    /// Career commands. Options are <c>key=value</c> words; a value with spaces goes in double quotes.
    ///
    /// Which song:
    ///   career=&lt;index|id|name&gt;   Index in the career list, the career's GUID, or (part of) its name.
    ///                             Defaults to the career open in the career menu.
    ///   tier=&lt;n&gt; song=&lt;n|all&gt;     Tier and song index, both from 0. "all" is every song of the tier.
    ///   (no tier)                 The career menu's selection: a song, or every song of a tier header.
    ///
    /// What result (simulate, scorescreen):
    ///   stars=&lt;0..5|gold&gt;  score=&lt;n&gt;  percent=&lt;0..100&gt;  fc=&lt;true|false&gt;
    ///   difficulty=&lt;Beginner|Easy|Medium|Hard|Expert|ExpertPlus&gt;
    ///   path=&lt;score|grant&gt;   score (default): through ScoreContainer.RecordScore like a played song.
    ///                        grant: a completion row only, like the career menu's debug Grant Stars.
    ///   commit=&lt;true|false&gt; scorescreen only: commit the result through the score path first (as after a
    ///                        real song), so the screen shows the real fan outcome. Default false: the
    ///                        screen shows what the result would earn, and nothing is saved.
    ///
    /// Commands:
    ///   simulate ...     Commit the result for each chosen song as the current (non-bot) players, then
    ///                    refresh the career menu if it shows that career. Reports the career's progress.
    ///   career ...       Report the career's progress: per tier, whether it is open, its star total and
    ///                    each song's best stars ("-" = never completed, "G" = gold). Careers that use
    ///                    fans also report the save's total fans and each tier's and song's fans.
    ///   scorescreen ...  Open the score screen for one song with the result (see commit=).
    /// </summary>
    public partial class DevRemote
    {
        private static readonly HashSet<string> ResultOptions = new()
        {
            "stars", "score", "percent", "fc", "difficulty", "path", "commit",
        };

        private static readonly HashSet<string> TargetOptions = new()
        {
            "career", "tier", "song",
        };

        private static string Simulate(string argument)
        {
            if (!TryParseOptions(argument, true, out var options, out var error) ||
                !TryResolveTargets(options, out var career, out var targets, out error) ||
                !TryParseResult(options, out var result, out error))
            {
                return $"error {error}";
            }

            var committed = 0;
            foreach (var (tierIndex, songIndex) in targets)
            {
                if (!CareerSimulator.TryCommit(career, tierIndex, songIndex, result, out error))
                {
                    break;
                }

                committed++;
            }

            var menu = OpenCareerMenu();
            if (committed > 0 && menu != null && menu.Career?.Id == career.Id)
            {
                menu.RefreshProgress();
            }

            var summary = Summarize(career);
            if (committed > 0 && CareerSongOutcome.Last is { } outcome && outcome.CareerId == career.Id)
            {
                summary = $"last: {DescribeOutcome(outcome)} | {summary}";
            }

            return error is null
                ? $"ok committed {committed} | {summary}"
                : $"error committed {committed} of {targets.Count}, then: {error} | {summary}";
        }

        private static string DescribeCareer(string argument)
        {
            if (!TryParseOptions(argument, false, out var options, out var error))
            {
                return $"error {error}";
            }

            var menu = OpenCareerMenu();
            CareerPreset career;
            if (options.TryGetValue("career", out var reference))
            {
                if (!TryFindCareer(reference, out career, out error))
                {
                    return $"error {error}";
                }
            }
            else if (menu != null && menu.Career != null)
            {
                career = menu.Career;
            }
            else
            {
                return "error no career= given and the career menu is not open";
            }

            return $"ok {Summarize(career)}";
        }

        private static string ShowScoreScreen(string argument)
        {
            if (!TryParseOptions(argument, true, out var options, out var error) ||
                !TryResolveTargets(options, out var career, out var targets, out error) ||
                !TryParseResult(options, out var result, out error))
            {
                return $"error {error}";
            }

            var (tierIndex, songIndex) = targets[0];
            var song = career.Tiers[tierIndex].Songs[songIndex].SongEntry;
            if (song is null)
            {
                return "error the song is not in the library";
            }

            var commit = false;
            if (options.TryGetValue("commit", out var commitText) && !bool.TryParse(commitText, out commit))
            {
                return "error commit must be true or false";
            }

            if (commit)
            {
                result.Path = SimulationPath.Score;
                if (!CareerSimulator.TryCommit(career, tierIndex, songIndex, result, out error))
                {
                    return $"error {error}";
                }
            }
            else
            {
                // What this result would earn on top of the saved progress (the score screen reads the
                // outcome the score path leaves behind).
                var saveId = CareerSimulator.FindSaveId(career);
                var snapshot = saveId > 0 && ScoreContainer.Careers is not null
                    ? ScoreContainer.Careers.LoadProgressSnapshot(saveId)
                    : new CareerProgressSnapshot { CareerSaveId = saveId };
                CareerSongOutcome.Last = CareerSongOutcome.Preview(career, snapshot, new CareerSongCompletions
                {
                    CareerSongId = career.Tiers[tierIndex].Songs[songIndex].Id,
                    BandStars = result.BandStars.GetStarCount(),
                    IsGold = result.BandStars == StarAmount.StarGold,
                });
            }

            // What the end of a career song leaves behind (see GameManager.EndSong).
            GlobalVariables.State = PersistentState.Default;
            GlobalVariables.State.CurrentSong = song;
            GlobalVariables.State.CurrentCareer = CareerSimulator.ContextFor(career, tierIndex, songIndex);
            GlobalVariables.State.ScoreScreenStats = CareerSimulator.ScoreScreenStatsFor(result);

            GlobalVariables.Instance.LoadScene(SceneIndex.Score);
            return $"ok showing '{song.Name}'{(commit ? " (committed)" : "")}: " +
                DescribeOutcome(CareerSongOutcome.Last);
        }

        private static CareerMenu OpenCareerMenu()
        {
            var menu = FindAnyObjectByType<CareerMenu>();
            return menu != null && menu.isActiveAndEnabled ? menu : null;
        }

        private static string Summarize(CareerPreset career)
        {
            var saveId = CareerSimulator.FindSaveId(career);
            var evaluation = CareerMenu.Evaluate(career, saveId);

            var text = new StringBuilder($"career='{career.Name}' save={saveId}");
            if (evaluation.UsesFans)
            {
                text.Append($" fans={evaluation.TotalFans}");
            }

            foreach (var tier in evaluation.Tiers)
            {
                var content = career.Tiers[tier.TierIndex];
                text.Append($" | t{tier.TierIndex} '{content.Name}'");
                if (tier.IsBonus)
                {
                    text.Append(" bonus");
                }

                text.Append(tier.Unlocked ? " open" : $" locked(needs {tier.RemainingToUnlock})");
                text.Append($" stars={tier.StarSum}/{content.Songs.Length * ViewType.STARS_PER_SONG}");
                text.Append(" best=[");
                text.Append(string.Join(",", tier.Songs.Select(song =>
                    !song.Completed ? "-" : song.BestIsGold ? "G" : song.BestStars.ToString())));
                text.Append(']');
                if (evaluation.UsesFans)
                {
                    text.Append($" fans={tier.Fans}=[{string.Join(",", tier.Songs.Select(song => song.Fans))}]");
                }
                if (tier.Completed)
                {
                    text.Append(" completed");
                }
            }

            return text.ToString();
        }

        private static string DescribeOutcome(CareerSongOutcome outcome)
        {
            if (outcome is null)
            {
                return "no career outcome";
            }

            var text = outcome.UsesFans
                ? $"fans {outcome.FansBefore} -> {outcome.FansAfter} (+{outcome.FansGained})"
                : "career does not use fans";
            return outcome.UnlockedTierNames.Count == 0
                ? text
                : $"{text}, unlocked {string.Join(", ", outcome.UnlockedTierNames.Select(name => $"'{name}'"))}";
        }

        private static bool TryResolveTargets(Dictionary<string, string> options, out CareerPreset career,
            out List<(int tierIndex, int songIndex)> targets, out string error)
        {
            targets = new List<(int, int)>();
            var menu = OpenCareerMenu();

            if (options.TryGetValue("career", out var reference))
            {
                if (!TryFindCareer(reference, out career, out error))
                {
                    return false;
                }
            }
            else if (menu != null && menu.Career != null)
            {
                career = menu.Career;
            }
            else
            {
                career = null;
                error = "no career= given and the career menu is not open";
                return false;
            }

            if (options.TryGetValue("tier", out var tierText))
            {
                if (!int.TryParse(tierText, out var tierIndex) || tierIndex < 0 || tierIndex >= career.Tiers.Count)
                {
                    error = $"tier must be 0..{career.Tiers.Count - 1}";
                    return false;
                }

                var songCount = career.Tiers[tierIndex].Songs.Length;
                if (!options.TryGetValue("song", out var songText))
                {
                    error = "tier= needs song=<index|all>";
                    return false;
                }

                if (songText is "all" or "*")
                {
                    for (var songIndex = 0; songIndex < songCount; songIndex++)
                    {
                        targets.Add((tierIndex, songIndex));
                    }
                }
                else if (int.TryParse(songText, out var songIndex) && songIndex >= 0 && songIndex < songCount)
                {
                    targets.Add((tierIndex, songIndex));
                }
                else
                {
                    error = $"song must be 0..{songCount - 1} or all";
                    return false;
                }
            }
            else
            {
                if (menu == null || menu.Career?.Id != career.Id)
                {
                    error = "no tier= given and the career menu does not show that career";
                    return false;
                }

                switch (menu.CurrentSelection)
                {
                    case SongViewType song:
                        targets.Add((song.TierIndex, song.SongIndex));
                        break;
                    case TierViewType header when header.IsClickable:
                        var tierIndex = career.Tiers.ToList().IndexOf(header.Tier);
                        for (var songIndex = 0; songIndex < header.Tier.Songs.Length; songIndex++)
                        {
                            targets.Add((tierIndex, songIndex));
                        }
                        break;
                }
            }

            if (targets.Count == 0)
            {
                error = "nothing selected: select a song or an unlocked tier, or give tier= and song=";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryFindCareer(string reference, out CareerPreset career, out string error)
        {
            var careers = CareerSimulator.AllCareers().ToList();
            career = null;

            if (int.TryParse(reference, out var index))
            {
                career = index >= 0 && index < careers.Count ? careers[index] : null;
            }
            else if (Guid.TryParse(reference, out var id))
            {
                career = careers.FirstOrDefault(c => c.Id == id);
            }
            else
            {
                career = careers.FirstOrDefault(c => string.Equals(c.Name, reference, StringComparison.OrdinalIgnoreCase));
                if (career is null)
                {
                    var matches = careers
                        .Where(c => c.Name?.IndexOf(reference, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                    career = matches.Count == 1 ? matches[0] : null;
                }
            }

            error = career is null
                ? $"no single career matches '{reference}'; careers: " +
                    string.Join(", ", careers.Select((c, i) => $"{i}='{c.Name}'"))
                : null;
            return career is not null;
        }

        private static bool TryParseResult(Dictionary<string, string> options, out SimulatedResult result,
            out string error)
        {
            result = new SimulatedResult();
            error = null;

            foreach (var option in options)
            {
                var value = option.Value;
                switch (option.Key)
                {
                    case "stars":
                        if (value.Equals("gold", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Stars = 6;
                        }
                        else if (int.TryParse(value, out var stars) && stars is >= 0 and <= 6)
                        {
                            result.Stars = stars;
                        }
                        else
                        {
                            error = "stars must be 0..5 or gold";
                        }
                        break;
                    case "score":
                        if (int.TryParse(value, out var score) && score >= 0)
                        {
                            result.Score = score;
                        }
                        else
                        {
                            error = "score must be a whole number";
                        }
                        break;
                    case "percent":
                        if (float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var percent) && percent is >= 0f and <= 100f)
                        {
                            result.Percent = percent / 100f;
                        }
                        else
                        {
                            error = "percent must be 0..100";
                        }
                        break;
                    case "fc":
                        if (bool.TryParse(value, out var fullCombo))
                        {
                            result.FullCombo = fullCombo;
                        }
                        else
                        {
                            error = "fc must be true or false";
                        }
                        break;
                    case "difficulty":
                        if (Enum.TryParse<Difficulty>(value, true, out var difficulty))
                        {
                            result.Difficulty = difficulty;
                        }
                        else
                        {
                            error = $"difficulty must be one of {string.Join(", ", Enum.GetNames(typeof(Difficulty)))}";
                        }
                        break;
                    case "path":
                        if (Enum.TryParse<SimulationPath>(value, true, out var path))
                        {
                            result.Path = path;
                        }
                        else
                        {
                            error = "path must be score or grant";
                        }
                        break;
                }

                if (error is not null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Splits <c>key=value</c> words, honoring double quotes. Unknown keys are an error, so a typo
        /// does not silently fall back to a default.
        /// </summary>
        private static bool TryParseOptions(string argument, bool allowResult, out Dictionary<string, string> options,
            out string error)
        {
            options = new Dictionary<string, string>();
            var words = new List<string>();
            var current = new StringBuilder();
            var quoted = false;

            foreach (var c in argument)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0)
                    {
                        words.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                words.Add(current.ToString());
            }

            foreach (var word in words)
            {
                // "selected" is the default when no tier is given; accepted for readability.
                if (word.Equals("selected", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var equals = word.IndexOf('=');
                var key = equals > 0 ? word.Substring(0, equals).ToLowerInvariant() : string.Empty;
                if (!TargetOptions.Contains(key) && !(allowResult && ResultOptions.Contains(key)))
                {
                    error = $"unknown option '{word}'";
                    return false;
                }

                options[key] = word.Substring(equals + 1);
            }

            error = null;
            return true;
        }
    }
}
#endif
