using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YARG.Audio;
using YARG.Career;
using YARG.Core.Audio;
using YARG.Core.Song;
using YARG.Localization;
using YARG.Menu.Persistent;
using YARG.Scores;
using YARG.Settings;

namespace YARG.Menu.Career
{
    // Song rows that behave like the music library's: the band's best result (who played what, how
    // well), a preview of the highlighted song, and Confirm on a gig playing its next song.
    public partial class CareerMenu
    {
        private const double PREVIEW_SCROLL_DELAY = 0.6;
        private const double PREVIEW_FADE_DURATION = 1.25;

        private readonly PreviewPlayer _previewPlayer = new();
        private SongEntry _previewSong;
        private double _previewDelay;

        // Per completion row: the players of that play (see CareerEvaluation.SongResult.CountedCompletionId).
        private Dictionary<int, List<MusicLibrary.ViewType.ScoreInfo>> _scoresByCompletion = new();

        // Per completion row: the song that was played (for a user pick, which has no song of its own).
        private Dictionary<int, byte[]> _songByCompletion = new();

        /// <summary>
        /// Loads who played each completion of the save. Results belong to the save, so they are the
        /// band's own (or, without a band, the current players'), never another group's.
        /// </summary>
        private void LoadBandScores()
        {
            _scoresByCompletion = new Dictionary<int, List<MusicLibrary.ViewType.ScoreInfo>>();
            _songByCompletion = new Dictionary<int, byte[]>();

            var careers = ScoreContainer.Careers;
            if (careers is null || _careerSaveId <= 0)
            {
                return;
            }

            try
            {
                var scoresByGame = careers.GetSavePlayerScores(_careerSaveId)
                    .GroupBy(score => score.GameRecordId)
                    .ToDictionary(group => group.Key, group => group.Select(score => new MusicLibrary.ViewType.ScoreInfo
                    {
                        Score = score.Score,
                        Difficulty = score.Difficulty,
                        Instrument = score.Instrument,
                        Percent = score.GetPercent(),
                        IsFc = score.IsFc,
                    }).ToList());

                foreach (var completion in careers.GetSongCompletions(_careerSaveId))
                {
                    _songByCompletion[completion.Id] = completion.SongChecksum;

                    if (completion.GameRecordId > 0 && scoresByGame.TryGetValue(completion.GameRecordId, out var scores))
                    {
                        _scoresByCompletion[completion.Id] = scores;
                    }
                }
            }
            catch (Exception e)
            {
                YARG.Core.Logging.YargLogger.LogException(e, "Career: failed to load the save's player scores.");
            }
        }

        private IReadOnlyList<MusicLibrary.ViewType.ScoreInfo> ScoresFor(CareerEvaluation.SongResult result)
        {
            return result is not null && _scoresByCompletion.TryGetValue(result.CountedCompletionId, out var scores)
                ? scores
                : null;
        }

        /// <summary>
        /// Moves the selection to the row (a mouse click on it). False if the row is not in this list.
        /// </summary>
        public bool Select(ViewType row)
        {
            return SetIndexTo(view => ReferenceEquals(view, row));
        }

        /// <summary>
        /// Confirm: on a gig's header, play its next song; on any other row, what clicking it does.
        /// </summary>
        private void Confirm()
        {
            if (CurrentSelection is TierViewType { IsClickable: true } header)
            {
                PlayNextSong(header.Tier);
                return;
            }

            CurrentSelection?.ViewClick();
        }

        /// <summary>
        /// Plays the gig's first song the save has no stars on. A gig played as a show starts the show.
        /// When every song has stars there is nothing "next", so the gig is opened to pick from.
        /// </summary>
        private void PlayNextSong(CareerTier tier)
        {
            if (_career is null)
            {
                return;
            }

            var tierIndex = _career.Tiers.ToList().IndexOf(tier);
            if (tierIndex < 0)
            {
                return;
            }

            if (CareerSetlists.IsShow(tier))
            {
                StartShow(tierIndex);
                return;
            }

            var results = _evaluation?.Tiers[tierIndex].Songs;
            for (var songIndex = 0; songIndex < (tier.Songs?.Length ?? 0); songIndex++)
            {
                var song = tier.Songs[songIndex];
                var result = results is not null && songIndex < results.Count ? results[songIndex] : null;
                if (song.IsEncore || song.SongEntry is null || result is { Completed: true, BestStars: > 0 })
                {
                    continue;
                }

                new SongViewType(song, _career.Id, tier.Id, tierIndex, songIndex, tier.VenueHint,
                    songIndex == tier.Songs.Length - 1, result, OnSongPlayed).ViewClick();
                return;
            }

            ToastManager.ToastInformation(Localize.Key("Menu.Career.NoNextSong"));
        }

        /// <summary>
        /// Orange: expand or collapse the gig the selection is on (its header or any of its rows).
        /// </summary>
        private void ToggleSelectedTier()
        {
            var tierId = CurrentSelection switch
            {
                TierViewType { IsClickable: true } header => header.Tier.Id,
                SongViewType song => song.TierId,
                ShowViewType show => show.TierId,
                _ => (Guid?) null,
            };

            var tier = _career?.Tiers.FirstOrDefault(t => t.Id == tierId);
            if (tier is not null)
            {
                ToggleTier(tier);
            }
        }

        /// <summary>
        /// Keeps the preview on the highlighted song (as the music library does), and silent when the
        /// selection is not a song, a dialog or a video is up, or the song is a hidden one. Polled
        /// every frame: the selection also changes when the list is rebuilt under it.
        /// </summary>
        private void UpdatePreview()
        {
            var song = !_videoPlaying && !DialogManager.Instance.IsDialogShowing &&
                CurrentSelection is SongViewType { CanPreview: true } row
                    ? row.SongEntry
                    : null;

            if (song == _previewSong)
            {
                return;
            }

            _previewSong = song;
            _ = _previewPlayer.StopPreview();

            var volume = SettingsManager.Settings.PreviewVolume.Value;
            if (song is not null && volume > 0)
            {
                _ = _previewPlayer.StartPreview(song, volume, GlobalVariables.State.SongSpeed, _previewDelay,
                    PREVIEW_FADE_DURATION, SettingsManager.Settings.CensorMatureContent.Value);
            }

            _previewDelay = PREVIEW_SCROLL_DELAY;
        }

        private void StopPreview()
        {
            _previewSong = null;
            _previewDelay = 0;
            _ = _previewPlayer.StopPreview();
        }

        private void LateUpdate()
        {
            UpdatePreview();
        }

        private void OnDestroy()
        {
            ClearGigPictures();
            _previewPlayer.Dispose();
        }
    }
}
