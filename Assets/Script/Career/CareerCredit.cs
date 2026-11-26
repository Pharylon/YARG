using System;
using System.Collections.Generic;
using System.Linq;

namespace YARG.Career
{
    public enum CareerCreditBlock
    {
        None,

        /// <summary>No human player took part.</summary>
        NoPlayers,

        /// <summary>A bot took part. Careers do not credit bot play.</summary>
        BotPlaying,

        /// <summary>The band was deleted or cannot be found.</summary>
        BandMissing,

        /// <summary>A band is active but none of its members took part.</summary>
        NoMemberPlaying,
    }

    /// <summary>
    /// Who a career result is credited to: the active band's save, or (no band) the save of the exact
    /// set of human players, as before bands existed. Pure, so the rule is tested without a database.
    /// </summary>
    public class CareerCredit
    {
        public CareerCreditBlock Block { get; private set; }
        public bool Counts => Block == CareerCreditBlock.None;

        /// <summary>The band that owns the save, or null for a profile-set save.</summary>
        public Guid? BandId { get; private set; }

        /// <summary>The profile set that owns the save when there is no band (distinct, in play order).</summary>
        public IReadOnlyList<Guid> ProfileIds { get; private set; } = Array.Empty<Guid>();

        /// <summary>The band members among the players (band saves only).</summary>
        public IReadOnlyList<Guid> MembersPlaying { get; private set; } = Array.Empty<Guid>();

        /// <summary>
        /// The rule. With a band: the result counts when at least one member is among the human players;
        /// other humans may sit in and do not change which save is used. Without a band: the save of
        /// exactly these players. Either way, any bot taking part blocks credit.
        /// </summary>
        /// <param name="bandId">The active band, or null.</param>
        /// <param name="bandMembers">The band's members, or null if the band does not exist (deleted).</param>
        /// <param name="humanPlayers">The non-bot players taking part.</param>
        /// <param name="hasBots">Whether any bot took part.</param>
        public static CareerCredit Decide(Guid? bandId, IReadOnlyCollection<Guid> bandMembers,
            IEnumerable<Guid> humanPlayers, bool hasBots)
        {
            var humans = humanPlayers.Distinct().ToList();

            if (hasBots)
            {
                return Blocked(CareerCreditBlock.BotPlaying, bandId);
            }

            if (humans.Count == 0)
            {
                return Blocked(CareerCreditBlock.NoPlayers, bandId);
            }

            if (bandId is null)
            {
                return new CareerCredit { ProfileIds = humans };
            }

            if (bandMembers is null)
            {
                return Blocked(CareerCreditBlock.BandMissing, bandId);
            }

            var membersPlaying = humans.Where(bandMembers.Contains).ToList();
            if (membersPlaying.Count == 0)
            {
                return Blocked(CareerCreditBlock.NoMemberPlaying, bandId);
            }

            return new CareerCredit { BandId = bandId, MembersPlaying = membersPlaying };
        }

        private static CareerCredit Blocked(CareerCreditBlock block, Guid? bandId)
        {
            return new CareerCredit { Block = block, BandId = bandId };
        }
    }
}
