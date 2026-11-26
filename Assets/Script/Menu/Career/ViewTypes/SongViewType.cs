using System;
using System.Collections.Generic;
using Cysharp.Text;
using YARG.Career;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Localization;

namespace YARG.Menu.Career
{
    public class SongViewType : ViewType
    {
        public override BackgroundType Background => BackgroundType.Normal;
        public bool HasSongEntry => _songEntry != null;
        public Guid TierId => _tierId;
        public CareerSong Song => _song;
        public int TierIndex => _tierIndex;
        public int SongIndex => _songIndex;

        /// <summary>
        /// Driven by <see cref="CareerMenu"/> while the tier expands or collapses.
        /// </summary>
        public float Reveal { get; set; } = 1f;

        /// <summary>
        /// The career uses fans: a completed song also shows the fans its best result earned.
        /// </summary>
        public bool ShowFans { get; set; }

        /// <summary>
        /// The contract of the song's tier, which applies unless the song has its own.
        /// </summary>
        public CareerContract TierContract { get; set; }

        /// <summary>
        /// The song's gig is played as a show (see <see cref="CareerShow"/>): clicking the song starts the
        /// whole set instead of the song alone.
        /// </summary>
        public Action StartShow { get; set; }

        /// <summary>
        /// The song's title and artist are hidden (a mystery setlist's song or an encore not yet played,
        /// see <see cref="CareerSetlists.IsHidden"/>); the tier tells what to show instead.
        /// </summary>
        public CareerTier HiddenBy { get; set; }

        private bool Hidden => HiddenBy is not null;

        /// <summary>
        /// The icon of the song's source (YARG, Rock Band, Guitar Hero, ...), as in the music library.
        /// None for a song that is hidden or not in the library.
        /// </summary>
#nullable enable
        public override UnityEngine.Sprite? GetIcon()
#nullable disable
        {
            return Hidden || _songEntry is null ? null : YARG.Song.SongSources.SourceToIcon(_songEntry.Source);
        }

        /// <summary>
        /// The players of the save's best result of this song (instrument, difficulty, percent), shown
        /// next to the stars like the music library's score display.
        /// </summary>
        public IReadOnlyList<MusicLibrary.ViewType.ScoreInfo> BandScores { get; set; }

        public SongEntry SongEntry => _songEntry;

        /// <summary>
        /// Whether highlighting the row may play the song's preview (never for a hidden song, which
        /// the audio would give away).
        /// </summary>
        public bool CanPreview => !Hidden && _songEntry is not null;

        // An encore is only ever played after a show's set that earned it, never on its own.
        public override bool IsClickable => !_song.IsEncore;

        private readonly CareerSong _song;
        private readonly SongEntry  _songEntry;
        private readonly Guid  _careerId;
        private readonly Guid  _tierId;
        private readonly int   _tierIndex;
        private readonly int   _songIndex;
        private readonly string _tierVenueHint;
        private readonly bool  _lastInTier;
        private readonly CareerEvaluation.SongResult _result;
        private readonly Action _onPlay;

        /// <param name="result">
        /// This song's row from the cached <see cref="CareerEvaluation"/>, or null when progress is
        /// not available (no completion is shown then).
        /// </param>
        /// <param name="onPlay">
        /// Invoked when the song is actually launched, so the menu knows its cached progress is stale
        /// and only reloads it when something could have changed.
        /// </param>
        public SongViewType(CareerSong song, Guid careerId, Guid tierId, int tierIndex, int songIndex,
            string tierVenueHint, bool lastInTier, CareerEvaluation.SongResult result = null,
            Action onPlay = null)
        {
            _song = song;
            _songEntry = song.SongEntry;
            _careerId = careerId;
            _tierId = tierId;
            _tierIndex = tierIndex;
            _songIndex = songIndex;
            _tierVenueHint = tierVenueHint;
            _lastInTier = lastInTier;
            _result = result;
            _onPlay = onPlay;
        }

        public override CareerInfo? GetCareerInfo()
        {
            return new CareerInfo
            {
                Kind        = CareerRowKind.Song,
                Unlocked    = true, // songs are only listed once their tier is unlocked
                Completed   = _result?.Completed ?? false,
                BestStars   = _result?.BestStars ?? 0,
                BestIsGold  = _result?.BestIsGold ?? false,
                Scores      = BandScores,
                Artist      = Hidden ? string.Empty : _songEntry?.Artist.ToString() ?? string.Empty,
                Album       = Hidden ? string.Empty : _songEntry?.Album.ToString() ?? string.Empty,
                LastInGroup = _lastInTier,
                Reveal      = Reveal,
            };
        }

        public override string GetPrimaryText(bool selected)
        {
            var name = Hidden
                ? CareerSetlistText.HiddenTitle(_song)
                : _songEntry?.Name ?? Localize.Key("Menu.Career.SongMissing");
            return AddStrikethrough(FormatAs(name, TextType.Primary, selected));
        }

        public override string GetSecondaryText(bool selected)
        {
            var detail = Hidden ? CareerSetlistText.HiddenDetail(HiddenBy, _song) : DetailLine();
            if (ShowFans && _result is { Completed: true })
            {
                detail = Localize.KeyFormat("Menu.Career.SongFans", detail, _result.Fans.ToString("N0"));
            }

            detail = AddContract(detail);

            return AddStrikethrough(FormatAs(detail, TextType.Secondary, selected));
        }

        /// <summary>
        /// A song with its own contract shows it (the tier header shows the tier's); a song exempt from
        /// its tier's contract says so; a song whose counted result met its contract says that.
        /// </summary>
        private string AddContract(string detail)
        {
            if (_result is { ContractMet: true })
            {
                return Localize.KeyFormat("Menu.Career.Contract.SongMet", detail);
            }

            if (_song.Contract is { IsActive: false } && TierContract is { IsActive: true })
            {
                return Localize.KeyFormat("Menu.Career.Contract.SongNone", detail);
            }

            return CareerContractText.Line(_song.Contract, ShowFans) is { } own
                ? Localize.KeyFormat("Menu.Career.HeaderJoin", detail, own)
                : detail;
        }

        private string AddStrikethrough(string text)
        {
            // From this save's evaluation, so it never carries over from another save (see SongResult.Done).
            if (_result is { Done: true })
            {
                return ZString.Format("<color=white><s>{0}</s></color>", text);
            }

            return text;
        }

        /// <summary>
        /// The artist/album line shown under every song, played or not (D11).
        /// </summary>
        private string DetailLine()
        {
            if (_songEntry is null)
            {
                // Content fallback for songs that are not in the library.
                return _song.Description ?? string.Empty;
            }

            var artist = _songEntry.Artist.ToString();
            var album = _songEntry.Album.ToString();

            return artist;

            //
            // return string.IsNullOrEmpty(album)
            //     ? artist
            //     : Localize.KeyFormat("Menu.Career.SongArtistAlbum", artist, album);
        }

        // Start the song in career mode: carry the career context through to gameplay so the score
        // can be linked to a committed career completion.
        public override void ViewClick()
        {
            if (_song.IsEncore)
            {
                return;
            }

            if (StartShow is not null)
            {
                StartShow();
                return;
            }

            if (_songEntry is null)
            {
                YargLogger.LogError("Career: cannot start song, it was not found in the library.");
                return;
            }

            GlobalVariables.State.SetPlaybackSong(_songEntry, new CareerContext
            {
                CareerId = _careerId,
                TierId = _tierId,
                TierIndex = _tierIndex,
                VenueHint = _tierVenueHint,
                CareerSongId = _song.Id,
                SongIndex = _songIndex,
                BandId = CareerBands.ActiveBandId,
            });

            _onPlay?.Invoke();

            MenuManager.Instance.PushMenu(MenuManager.Menu.DifficultySelect);
        }
    }
}
