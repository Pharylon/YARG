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

            public readonly bool IsHeader => Kind != CareerRowKind.Song;
        }

        public virtual CareerInfo? GetCareerInfo()
        {
            return null;
        }

        public virtual bool IsClickable => true;

        public virtual void ViewClick()
        {

        }
    }
}
