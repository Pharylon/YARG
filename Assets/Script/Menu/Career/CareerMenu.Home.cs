using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using YARG.Career;
using YARG.Localization;
using YARG.Scores;

namespace YARG.Menu.Career
{
    // The band's home screen stays on the left from the lineup through to the gigs (see
    // CareerBandHome and CareerListMenu). Without an active band the career's own name and
    // description show there instead.
    public partial class CareerMenu
    {
        private CareerBandHome _bandHome;
        private GameObject _textColumn;
        private CareerBandSheet _sheet;
        private Guid? _sheetBand;
        private CareerEvaluation _sheetEvaluation;

        // Blue: the left panel shows the highlighted song's details instead of the band.
        private bool _showSongDetails;
        private CareerSongDetails _songDetails;

        private void ToggleSongDetails()
        {
            if (_videoPlaying)
            {
                return;
            }

            _showSongDetails = !_showSongDetails;
            SetMenuNavigation(true);
            UpdateHeader();
        }

        private void ShowBandHome(string notice)
        {
            if (_careerNameText == null)
            {
                return;
            }

            if (_showSongDetails)
            {
                ShowSongDetails();
                return;
            }

            if (_songDetails != null)
            {
                _songDetails.Hide();
            }

            if (_textColumn == null)
            {
                var layout = _careerNameText.GetComponentInParent<VerticalLayoutGroup>(true);
                _textColumn = layout != null ? layout.gameObject : _careerNameText.gameObject;
            }

            var careers = ScoreContainer.Careers;
            var band = CareerBands.ActiveBand(careers);
            if (band is null)
            {
                if (_bandHome != null)
                {
                    _bandHome.Hide();
                }

                _textColumn.SetActive(true);
                return;
            }

            if (_bandHome == null)
            {
                _bandHome = CareerBandHome.Create((RectTransform) _textColumn.transform.parent, new CareerBandHome.Art
                {
                    Star = _starSprite,
                    GoldStar = _goldStarSprite,
                    Fans = _fanSprites,
                    TitleTemplate = _careerNameText,
                    BodyTemplate = _careerDescriptionText,
                }, _bandHomeBottomInset);
            }

            // The band's totals across all its careers, loaded again whenever this career's progress is.
            if (_sheet is null || _sheetBand != band.Id || _sheetEvaluation != _evaluation)
            {
                _sheet = CareerBandSheet.Load(careers, band.Id, CareerBands.AllCareers());
                _sheetBand = band.Id;
                _sheetEvaluation = _evaluation;
            }

            var custom = CareerIcons.BandIcon(band.Id);
            _bandHome.gameObject.SetActive(true);
            _textColumn.SetActive(false);
            _bandHome.Show(band, _sheet, custom != null ? custom : _bandFallbackSprite, custom != null, notice);
        }

        private void ShowSongDetails()
        {
            if (_textColumn == null)
            {
                var layout = _careerNameText.GetComponentInParent<VerticalLayoutGroup>(true);
                _textColumn = layout != null ? layout.gameObject : _careerNameText.gameObject;
            }

            if (_songDetails == null)
            {
                _songDetails = CareerSongDetails.Create((RectTransform) _textColumn.transform.parent, _careerNameText,
                    _difficultyRingPrefab, _starSprite, _goldStarSprite, _bandHomeBottomInset);
            }

            _textColumn.SetActive(false);
            if (_bandHome != null)
            {
                _bandHome.gameObject.SetActive(false);
            }

            _songDetails.Show(DetailsFor(CurrentSelection));
        }

        /// <summary>The highlighted song's name, for the header while its details are open.</summary>
        private string DetailsTitle()
        {
            return CurrentSelection is SongViewType song
                ? song.SongEntry?.Name.ToString() ?? (song.Song.IsUserPick ? Localize.Key("Menu.Career.Theme.Pick") : null)
                : null;
        }

        private CareerSongDetails.Info DetailsFor(ViewType row)
        {
            if (row is not SongViewType song)
            {
                return null;
            }

            var result = song.Result;
            var info = new CareerSongDetails.Info
            {
                Song = song.CanPreview || song.Song.IsUserPick ? song.SongEntry : null,
                ReplacedName = song.ReplacedSong?.Name,
                IsUserPick = song.Song.IsUserPick,
                ShowFans = _evaluation?.UsesFans ?? false,
            };

            if (result is { Completed: true })
            {
                info.Played = true;
                info.Stars = result.BestStars;
                info.Gold = result.BestIsGold;
                info.Fans = result.Fans;
                info.Contract = song.TierIndex < (_career?.Tiers.Count ?? 0)
                    ? CareerContracts.For(_career.Tiers[song.TierIndex])
                    : null;
                info.ContractMet = result.ContractMet;
                if (_completionById.TryGetValue(result.CountedCompletionId, out var completion))
                {
                    info.Points = completion.BandScore;
                }

                foreach (var score in ScoresFor(result) ?? new List<MusicLibrary.ViewType.ScoreInfo>())
                {
                    info.Players.Add(YARG.Localization.Localize.KeyFormat("Menu.Career.Details.Player",
                        score.Instrument.ToLocalizedName(), score.Difficulty.ToLocalizedName(),
                        score.Percent.ToString("0%")));
                }
            }

            return info;
        }
    }
}
