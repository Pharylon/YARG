using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using YARG.Core.Game;
using YARG.Core.Logging;
using YARG.Settings.Customization;

namespace YARG.Career
{
    public enum CareerCompletionMode
    {
        SingleSong = 0,
        FullTierPlaylist = 1,
        FullCareerPlaylist = 2,
    }

    public enum UnlockType
    {
        StarCount,
        CompletionCount,
        // Unlocks when the save's total fans (see CareerFans) reach UnlockCriteria, regardless of
        // which tiers they came from, so several tiers can be open at once.
        FanCount,
    }

    public enum CompletionBonusType
    {
        None,
        Image,
        Sound,
        Video,
    }

    // TODO: Move this to an appropriate location in YARG.Venue
    public enum VenueSize
    {
        NotSpecified,
        Starter,
        Small,
        Medium,
        Large,
        Epic
    }

    public class CareerTier
    {
        public string       Name;
        public Guid         Id;
        public string       Description;
        public CareerSong[] Songs;
        public string       CustomUnlockText;
        public string       BackgroundImage;
        public string       CompletionBonusText;
        [JsonConverter(typeof(StringEnumConverter))]
        public CompletionBonusType CompletionBonus;
        [JsonConverter(typeof(JsonFileInfoConverter))]
        public FileInfo            MediaFilename;
        [JsonConverter(typeof(StringEnumConverter))]
        public VenueSize           VenueSize;
        public string              VenueHint;

        [JsonConverter(typeof(StringEnumConverter))]
        public UnlockType          UnlockType;
        public int                 UnlockCriteria;

        // How this tier's songs are played. FullTierPlaylist: the tier is played as one show, its songs
        // queued through the setlist (CareerShow); each song still counts on its own when it ends.
        // FullCareerPlaylist is not supported yet: such a tier is played song by song.
        [JsonConverter(typeof(StringEnumConverter))]
        public CareerCompletionMode CompletionMode;

        // Bonus is not counted towards other tier unlocks
        public bool IsBonus;

        // Fans per song relative to the Starter venue (see CareerFans). Null: from VenueSize.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? FanMultiplier;

        // Flavor text only.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string City;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string VenueName;

        // Optional gig contract for every song of the tier (a song's own contract overrides it).
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public CareerContract Contract;

        // Gig rules (see CareerGigRules): modifiers every player gets for the tier's songs, on top of their
        // own (only those their instrument supports), and the slowest song speed allowed, in percent (100
        // or more; a faster chosen speed is kept). Applied for the play only, never saved to the profiles.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public Modifier? ForcedModifiers;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? MinSongSpeed;

        // Mystery setlist: the titles and artists of the tier's songs stay hidden until they have been
        // played (career list, instrument select).
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Mystery;

        // Make your own setlist: the players pick this many of the tier's songs (encore songs not counted)
        // for a show, and the tier is complete after this many of its songs are completed. Null: all.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? SetlistSize;

        // Encore: when the band's average stars over the songs of a show reach this, the tier's encore
        // song(s) (CareerSong.IsEncore) are revealed and played after the set. Null: no encore.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public float? EncoreMinStars;

        // A themed gig (see CareerGigTheme): songs played in place of its own (retail mode) and its user
        // picks must fit the theme. Null: no theme.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public CareerGigTheme Theme;

        // User picks: this many more songs in the gig, each one picked by the players from their library
        // (fitting the theme, if any) when they play it. They count like the gig's own songs. Each is a
        // CareerSong slot (IsUserPick) after the gig's own songs, made at load with an Id derived from the
        // gig's Id, and never saved. Null or 0: none.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? UserPicks;

        public const int MAX_USER_PICKS = 20;

        /// <summary>
        /// Makes the gig's user-pick slots match <see cref="UserPicks"/>: the gig's own songs, then one
        /// slot per pick.
        /// </summary>
        public void RefreshUserPicks()
        {
            var own = (Songs ?? Array.Empty<CareerSong>()).Where(song => song is not null && !song.IsUserPick).ToList();
            var count = Math.Clamp(UserPicks ?? 0, 0, MAX_USER_PICKS);
            for (var i = 0; i < count; i++)
            {
                own.Add(new CareerSong
                {
                    Id = UserPickId(Id, i),
                    Identifier = CareerSongIdentifier.UserPick,
                    Parent = this,
                });
            }

            Songs = own.ToArray();
        }

        /// <summary>The stable Id of a gig's user-pick slot, so its progress stays linked.</summary>
        public static Guid UserPickId(Guid tierId, int index)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{tierId}/user-pick/{index}")));
        }

        // User-pick slots are not content: they are left out of the saved file and made again on load.
        private CareerSong[] _savedSongs;

        [OnSerializing]
        private void OnSerializing(StreamingContext context)
        {
            _savedSongs = Songs;
            Songs = Songs?.Where(song => song is not null && !song.IsUserPick).ToArray();
        }

        [OnSerialized]
        private void OnSerialized(StreamingContext context)
        {
            Songs = _savedSongs;
            _savedSongs = null;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            RefreshUserPicks();

            if (Id == Guid.Empty)
            {
                YargLogger.LogFormatError(
                    "CareerTier: missing stable Id in content for tier '{0}'. Career progress will not be able to track this tier.",
                    Name);
            }
        }

        public CareerTier()
        {
        }

        public CareerTier(CareerTier other)
        {
            Name = other.Name;
            Id = Guid.NewGuid();
            Description = other.Description;

            if (other.Songs != null)
            {
                Songs = other.Songs.Where(song => !song.IsUserPick)
                    .Select(song => new CareerSong(song) { Parent = this }).ToArray();
            }
            else
            {
                Songs = Array.Empty<CareerSong>();
            }

            BackgroundImage = other.BackgroundImage;
            CustomUnlockText = other.CustomUnlockText;
            CompletionBonusText = other.CompletionBonusText;
            CompletionBonus = other.CompletionBonus;
            MediaFilename = other.MediaFilename == null ? null : new FileInfo(other.MediaFilename.Name);
            VenueSize = other.VenueSize;
            VenueHint = other.VenueHint;
            UnlockType = other.UnlockType;
            UnlockCriteria = other.UnlockCriteria;
            CompletionMode = other.CompletionMode;
            IsBonus = other.IsBonus;
            FanMultiplier = other.FanMultiplier;
            City = other.City;
            VenueName = other.VenueName;
            Contract = other.Contract is null ? null : new CareerContract(other.Contract);
            ForcedModifiers = other.ForcedModifiers;
            MinSongSpeed = other.MinSongSpeed;
            Mystery = other.Mystery;
            SetlistSize = other.SetlistSize;
            EncoreMinStars = other.EncoreMinStars;
            Theme = other.Theme is null ? null : new CareerGigTheme(other.Theme);
            UserPicks = other.UserPicks;

            // The copy's slots belong to its own Id.
            RefreshUserPicks();
        }

        public void AddSong(CareerSong song)
        {
            if (song == null) return;
            song.Parent = this;
            var list = Songs != null ? new List<CareerSong>(Songs) : new List<CareerSong>();

            // The gig's own songs come before its user picks.
            var firstPick = list.FindIndex(existing => existing.IsUserPick);
            list.Insert(firstPick < 0 ? list.Count : firstPick, song);
            Songs = list.ToArray();
        }

        public void InsertSong(int index, CareerSong song)
        {
            if (song == null) return;
            song.Parent = this;
            var list = Songs != null ? new List<CareerSong>(Songs) : new List<CareerSong>();
            if (index < 0) index = 0;
            if (index > list.Count) index = list.Count;
            list.Insert(index, song);
            Songs = list.ToArray();
        }

        public bool RemoveSong(CareerSong song)
        {
            if (Songs == null) return false;
            var list = new List<CareerSong>(Songs);
            bool removed = list.Remove(song);
            if (removed)
            {
                Songs = list.ToArray();
            }
            return removed;
        }

        public void RemoveSongAt(int index)
        {
            if (Songs == null || index < 0 || index >= Songs.Length) return;
            var list = new List<CareerSong>(Songs);
            list.RemoveAt(index);
            Songs = list.ToArray();
        }

        public void MoveSong(int fromIndex, int toIndex)
        {
            if (Songs == null || fromIndex < 0 || fromIndex >= Songs.Length || toIndex < 0 || toIndex >= Songs.Length || fromIndex == toIndex)
                return;

            var list = new List<CareerSong>(Songs);
            var song = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, song);
            Songs = list.ToArray();
        }

        public override string ToString()
        {
            return $"CareerTier: {Name}";
        }

        public void RefreshSongEntries()
        {
            foreach (var song in Songs)
            {
                song.RefreshSongEntry();
            }

            foreach (var song in Theme?.Songs ?? Array.Empty<CareerSong>())
            {
                song?.RefreshSongEntry();
            }
        }
    }
}