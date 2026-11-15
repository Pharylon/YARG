using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Player;
using YARG.Scores;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG.Career
{
    /// <summary>
    /// The active band and save resolution on top of <see cref="CareerCredit"/>: which save the career
    /// menu shows and a finished song is committed to. With no active band everything resolves by
    /// player set, exactly as before bands existed.
    /// </summary>
    public static class CareerBands
    {
        private static Guid? _activeBandId;
        private static bool _activeBandRestored;

        /// <summary>
        /// The band careers are played as, or null to play as the current players (profile-set saves).
        /// Remembered in the settings file across game starts; on the first read it is restored from
        /// there if that band still exists (and is not deleted), otherwise it starts as null. Each
        /// career song carries it in <see cref="CareerContext.BandId"/>.
        /// </summary>
        public static Guid? ActiveBandId
        {
            get
            {
                RestoreActiveBand();
                return _activeBandId;
            }
            set
            {
                _activeBandRestored = true;
                if (_activeBandId == value)
                {
                    return;
                }

                _activeBandId = value;
                RememberActiveBand(value);
            }
        }

        private static void RestoreActiveBand()
        {
            // Settings and the score database load at startup; until both have, there is nothing to
            // restore from or check against, so try again on the next read.
            if (_activeBandRestored || SettingsManager.Settings is null || ScoreContainer.Careers is null)
            {
                return;
            }

            _activeBandRestored = true;

            var remembered = SettingsManager.Settings.CareerActiveBandId;
            if (remembered is { } id && ScoreContainer.Careers.GetBand(id) is not null)
            {
                _activeBandId = id;
            }
            else if (remembered is not null)
            {
                // Deleted or gone since: forget it.
                RememberActiveBand(null);
            }
        }

        private static void RememberActiveBand(Guid? bandId)
        {
            if (SettingsManager.Settings is null || SettingsManager.Settings.CareerActiveBandId == bandId)
            {
                return;
            }

            SettingsManager.Settings.CareerActiveBandId = bandId;
            SettingsManager.SaveSettings();
        }

        /// <summary>
        /// The active band, or null if none is active or it no longer exists (which also clears it).
        /// </summary>
        public static CareerBand ActiveBand(CareerDatabase careers)
        {
            if (ActiveBandId is not { } id || careers is null)
            {
                return null;
            }

            var band = careers.GetBand(id);
            if (band is null)
            {
                ActiveBandId = null;
            }

            return band;
        }

        /// <summary>
        /// Every installed career, default ones first.
        /// </summary>
        public static IEnumerable<CareerPreset> AllCareers()
        {
            return CustomContentManager.Careers is null
                ? Enumerable.Empty<CareerPreset>()
                : CustomContentManager.Careers.DefaultPresets.Concat(CustomContentManager.Careers.CustomPresets);
        }

        /// <summary>
        /// The human players taking part: not sitting out and not bots (bots too behind the dev switch,
        /// see <see cref="CareerBotCredit"/>).
        /// </summary>
        public static List<Guid> CurrentHumanPlayers()
        {
            return PlayerContainer.Players
                .Where(player => !player.SittingOut && (!player.Profile.IsBot || CareerBotCredit.Enabled))
                .Select(player => player.Profile.Id)
                .ToList();
        }

        /// <summary>
        /// Who a result by these players is credited to, playing as <paramref name="bandId"/> (or as
        /// themselves if null).
        /// </summary>
        public static CareerCredit Credit(CareerDatabase careers, Guid? bandId, IEnumerable<Guid> humanPlayers,
            bool hasBots)
        {
            IReadOnlyCollection<Guid> members = null;
            if (bandId is { } id && careers?.GetBand(id) is not null)
            {
                members = careers.GetBandMembers(id);
            }

            return CareerCredit.Decide(bandId, members, humanPlayers, hasBots);
        }

        /// <summary>
        /// Credit for the current players and the active band: what the career menu shows.
        /// </summary>
        public static CareerCredit CurrentCredit(CareerDatabase careers)
        {
            return Credit(careers, ActiveBand(careers)?.Id, CurrentHumanPlayers(), false);
        }

        /// <summary>
        /// The active save the credit points at, or 0 if there is none yet. A band's save is found even
        /// when the current players would not earn it credit (no member playing), so the menu still
        /// shows the band's progress.
        /// </summary>
        public static int FindSaveId(CareerDatabase careers, CareerPreset career, CareerCredit credit)
        {
            if (careers is null || career is null)
            {
                return 0;
            }

            if (credit.BandId is { } bandId)
            {
                return credit.Block == CareerCreditBlock.BandMissing
                    ? 0
                    : careers.FindActiveSaveForBand(career.Id, bandId)?.Id ?? 0;
            }

            return credit.Counts ? careers.FindActiveSaveForProfiles(career.Id, credit.ProfileIds)?.Id ?? 0 : 0;
        }

        /// <summary>
        /// The active save the credit points at, created if needed. The credit must count.
        /// </summary>
        public static int GetOrCreateSaveId(CareerDatabase careers, CareerPreset career, CareerCredit credit)
        {
            if (!credit.Counts)
            {
                throw new InvalidOperationException($"Career: this result does not count ({credit.Block}).");
            }

            return credit.BandId is { } bandId
                ? careers.GetOrCreateSave(career.Id, career.Version, bandId)
                : careers.GetOrCreateSave(career.Id, career.Version, credit.ProfileIds.ToList());
        }

        /// <summary>
        /// The band's fans over all its active saves (see <see cref="CareerFans.BandFans"/>). Saves of
        /// careers that are no longer installed are skipped.
        /// </summary>
        public static int BandFans(CareerDatabase careers, Guid bandId, IEnumerable<CareerPreset> allCareers)
        {
            if (careers is null)
            {
                return 0;
            }

            var byId = new Dictionary<Guid, CareerPreset>();
            foreach (var career in allCareers)
            {
                byId.TryAdd(career.Id, career);
            }

            var evaluations = new List<CareerEvaluation>();
            foreach (var save in careers.GetActiveSavesForBand(bandId))
            {
                if (byId.TryGetValue(save.CareerId, out var career))
                {
                    evaluations.Add(CareerEvaluation.Evaluate(career, careers.LoadProgressSnapshot(save.Id)));
                }
            }

            return CareerFans.BandFans(evaluations);
        }

        /// <summary>
        /// Member names, in membership order. Profiles that no longer exist are left out.
        /// </summary>
        public static List<string> MemberNames(CareerDatabase careers, Guid bandId)
        {
            return careers.GetBandMembers(bandId)
                .Select(PlayerContainer.GetProfileById)
                .Where(profile => profile is not null)
                .Select(profile => profile.Name)
                .ToList();
        }
    }
}
