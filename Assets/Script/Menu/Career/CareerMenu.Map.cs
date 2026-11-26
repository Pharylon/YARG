using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YARG.Career;
using YARG.Helpers.Extensions;
using YARG.Localization;

namespace YARG.Menu.Career
{
    // The gigs are picked on a map (CareerGigMap) at the top of the right-hand panel; the list under
    // it holds only the selected gig's songs; the gig's name, progress, fans and rules are shown
    // between the two. Left/Right move along the route.
    public partial class CareerMenu
    {
        // Share of the panel's height the map takes; the song list gets the rest.
        private const float MAP_HEIGHT_SHARE = 0.5f;

        private CareerGigMap _gigMap;

        // Gigs that unlocked since the map last showed them, waiting to be celebrated there. Kept
        // until the map exists (progress is loaded before the menu first shows).
        private readonly List<Guid> _freshUnlocks = new();

        /// <summary>
        /// Splits the list's panel: the map on top, and under it a new viewport that the rows are moved
        /// into (the list lays itself out inside whatever its rows' parent sits in).
        /// </summary>
        private void EnsureGigMap()
        {
            if (_gigMap != null || ViewObjectParent is not RectTransform rows ||
                rows.parent is not RectTransform panel || _careerNameText == null)
            {
                return;
            }

            var viewport = new GameObject("Song List Viewport", typeof(RectTransform), typeof(RectMask2D))
                .GetComponent<RectTransform>();
            viewport.SetParent(panel, false);
            viewport.SetSiblingIndex(rows.GetSiblingIndex());
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = new Vector2(1f, 1f - MAP_HEIGHT_SHARE);
            // The panel runs on under the button bar at the bottom of the screen; the list stops above it.
            viewport.offsetMin = new Vector2(0f, _bandHomeBottomInset);
            viewport.offsetMax = Vector2.zero;
            rows.SetParent(viewport, false);

            var mapArea = new GameObject("Gig Map Area", typeof(RectTransform)).GetComponent<RectTransform>();
            mapArea.SetParent(panel, false);
            mapArea.anchorMin = new Vector2(0f, 1f - MAP_HEIGHT_SHARE);
            mapArea.anchorMax = Vector2.one;
            mapArea.offsetMin = Vector2.zero;
            mapArea.offsetMax = Vector2.zero;
            if (panel.TryGetComponent<LayoutGroup>(out _))
            {
                viewport.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                mapArea.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }

            _gigMap = CareerGigMap.Create(mapArea, _careerNameText, SelectGig);
        }

        // The gigs' own pictures (a tier's image media), by tier; loaded once per career.
        private readonly Dictionary<Guid, Texture2D> _gigPictures = new();
        private Guid? _gigPicturesCareer;

        private static readonly string[] PICTURE_EXTENSIONS = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// Loads the pictures of the career's gigs (the image a tier names as its media, in the
        /// career's content folder) for the map's stops. They appear as they finish decoding.
        /// </summary>
        private void LoadGigPictures(CareerPreset career)
        {
            if (_gigPicturesCareer == career.Id)
            {
                return;
            }

            ClearGigPictures();
            _gigPicturesCareer = career.Id;

            var folder = GetCareerContentFolder(career);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            foreach (var tier in career.Tiers)
            {
                var name = tier.MediaFilename?.Name;
                if (string.IsNullOrEmpty(name) ||
                    !PICTURE_EXTENSIONS.Contains(System.IO.Path.GetExtension(name).ToLowerInvariant()))
                {
                    continue;
                }

                var file = System.IO.Path.Combine(folder, name);
                if (System.IO.File.Exists(file))
                {
                    LoadGigPicture(career.Id, tier.Id, file).Forget();
                }
            }
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid LoadGigPicture(Guid careerId, Guid tierId, string file)
        {
            Texture2D texture;
            try
            {
                using var image = await Cysharp.Threading.Tasks.UniTask.RunOnThreadPool(
                    () => YARG.Core.IO.YARGImage.Load(file));
                if (image is null)
                {
                    return;
                }

                texture = image.LoadTexture(true);
            }
            catch (Exception e)
            {
                YARG.Core.Logging.YargLogger.LogFormatWarning<string, string>(
                    "Career: could not load the gig picture `{0}`: {1}", file, e.Message);
                return;
            }

            // The menu or the career may be gone by now.
            if (this == null || _gigPicturesCareer != careerId)
            {
                Destroy(texture);
                return;
            }

            _gigPictures[tierId] = texture;
            UpdateGigMap();
        }

        private void ClearGigPictures()
        {
            foreach (var texture in _gigPictures.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            _gigPictures.Clear();
            _gigPicturesCareer = null;
        }

        /// <summary>
        /// The gig the map has selected: the remembered one, else the one the band is working on. It
        /// may be a locked one, which lists no songs.
        /// </summary>
        private Guid? SelectedGig()
        {
            if (_evaluation is null)
            {
                return null;
            }

            if (_evaluation.Tiers.Any(tier => tier.TierId == _expandedTier))
            {
                return _expandedTier;
            }

            _expandedTier = CurrentTierId(_evaluation);
            return _expandedTier;
        }

        private void UpdateGigMap()
        {
            if (_gigMap == null || _career is null || _evaluation is null)
            {
                return;
            }

            var selected = SelectedGig();
            string info = null;
            string story = null;

            var stops = new List<CareerGigMap.Stop>();
            var column = 0;
            for (var i = 0; i < _career.Tiers.Count && i < _evaluation.Tiers.Count; i++)
            {
                var tier = _career.Tiers[i];
                var result = _evaluation.Tiers[i];

                // A gig that unlocks on the same terms as the one before it unlocks with it: the two
                // are a fork in the route (the first gig is open from the start, so it never forks).
                if (i > 0 && !(i > 1 && UnlocksWith(tier, _career.Tiers[i - 1])))
                {
                    column++;
                }

                // Stars, fans, and the gig's contract and rules; for a locked gig, what unlocks it.
                if (tier.Id == selected)
                {
                    info = new TierViewType(tier, result)
                    {
                        ShowFans = _evaluation.UsesFans,
                        HometownShow = IsHometown(tier),
                    }.GetSecondaryText(false);

                    // The gig's own text (what the unlock dialog used to say).
                    story = tier.CustomUnlockText;
                }
                stops.Add(new CareerGigMap.Stop
                {
                    Id = tier.Id,
                    Label = StopLabel(tier),
                    Bonus = tier.IsBonus,
                    Unlocked = result.Unlocked,
                    Completed = result.Unlocked && result.Completed,
                    Column = column,
                    Picture = _gigPictures.GetValueOrDefault(tier.Id),
                });
            }

            _gigMap.Show(stops, selected, info, story);
            RefreshNavigationForGig();

            if (_freshUnlocks.Count > 0)
            {
                // Held back while the band panel is still counting in the new stars and fans.
                _gigMap.Celebrate(_freshUnlocks, () => _bandHome != null && _bandHome.IsGaining);
                _freshUnlocks.Clear();
            }
        }

        private static bool UnlocksWith(CareerTier tier, CareerTier previous)
        {
            return tier.UnlockType == previous.UnlockType && tier.UnlockCriteria == previous.UnlockCriteria;
        }

        /// <summary>
        /// A stop is named for its venue ("The Lantern Room, Westbridge"); a gig without one, for itself.
        /// The setlist's first gig is in the band's hometown (see <see cref="IsOpeningGig"/>).
        /// </summary>
        private string StopLabel(CareerTier tier)
        {
            if (IsOpeningGig(tier))
            {
                var place = string.IsNullOrWhiteSpace(tier.VenueName) ? tier.Name : tier.VenueName;
                return Localize.KeyFormat("Menu.Career.Venue", place, _bandHometown.Trim());
            }

            var venue = CareerBandSheetDialogs.VenueText(tier);
            return string.IsNullOrWhiteSpace(venue) ? tier.Name : venue;
        }

        /// <summary>
        /// Selects the gig, from a click on its stop or Left/Right: an unlocked gig lists its songs, a
        /// locked one says what unlocks it.
        /// </summary>
        private void SelectGig(Guid tierId)
        {
            if (_evaluation is null || _videoPlaying || tierId == _expandedTier ||
                !_evaluation.Tiers.Any(tier => tier.TierId == tierId))
            {
                return;
            }

            _expandedTier = tierId;
            RequestViewListUpdate();
            SelectedIndex = 0;
        }

        /// <summary>
        /// Left/Right: the previous or next gig along the route.
        /// </summary>
        private void StepGig(int direction)
        {
            if (_evaluation is null)
            {
                return;
            }

            var gigs = _evaluation.Tiers.Select(tier => tier.TierId).ToList();
            var index = gigs.IndexOf(SelectedGig() ?? Guid.Empty);
            var next = index + direction;
            if (index >= 0 && next >= 0 && next < gigs.Count)
            {
                SelectGig(gigs[next]);
            }
        }
    }
}
