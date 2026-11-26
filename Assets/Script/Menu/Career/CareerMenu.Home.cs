using System;
using UnityEngine;
using UnityEngine.UI;
using YARG.Career;
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

        private void ShowBandHome(string notice)
        {
            if (_careerNameText == null)
            {
                return;
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
            _textColumn.SetActive(false);
            _bandHome.Show(band, _sheet, custom != null ? custom : _bandFallbackSprite, custom != null, notice);
        }
    }
}
