using System.Collections.Generic;
using YARG.Menu.ListMenu;

namespace YARG.Menu.Career
{
    public abstract class ViewType : BaseViewType
    {
        public const int STARS_PER_SONG = 5;

        public enum CareerRowKind
        {
            CareerHeader,
            TierHeader,
            LockedTierHeader,
            Song,
        }

        public struct CareerInfo
        {
            public CareerRowKind Kind;

            public bool Unlocked;
            public bool Completed;
            public int BestStars;
            // The best result was gold (BestStars is 5 then).
            public bool BestIsGold;
            // Song rows: who played the save's best result, on what and how well (one entry per
            // player; empty when never played or the result has no score record).
            public IReadOnlyList<MusicLibrary.ViewType.ScoreInfo> Scores;
            public int EarnedStars;
            public int AvailableStars;

            // Career header only
            public int TotalEarnedStars;
            public int TotalAvailableStars;
            public string PlayerNames;
            public string TierName;

            public string Artist;
            public string Album;

            public string LockHint;

            // Whether this row closes its tier's box: the last song of a tier, or a locked tier
            // header (which lists no songs).
            public bool LastInGroup;

            // How far an animating song row is expanded, from 0 (hidden) to 1 (full height).
            public float Reveal;

            // A row drawn like a song without a star display (e.g. "Play the full set").
            public bool NoStars;

            public readonly bool IsHeader => Kind != CareerRowKind.Song;
        }

        /// <summary>
        /// The row is the first of its gig's box (there is no header row above it): it draws the
        /// box's top edge.
        /// </summary>
        public bool FirstInGroup { get; set; }

        public virtual CareerInfo? GetCareerInfo()
        {
            return null;
        }

        public virtual bool IsClickable => true;

        public enum RowIcon
        {
            None,
            Person,
            Bot,
        }

        // Career list rows (see CareerView): an icon of its own, the text on the right edge of a
        // button row, and the state of the row's connect button (null: no button).
        public virtual RowIcon CareerIcon => RowIcon.None;

        // A picture of its own (a band icon, a profile picture), drawn in place of CareerIcon.
        // Decoded image textures are upside down, so the row flips it.
        public virtual UnityEngine.Sprite CustomIcon => null;

        public virtual string GetSideText(bool selected) => null;

        public virtual bool? ConnectedState => null;

        public virtual void ViewClick()
        {

        }
    }
}
