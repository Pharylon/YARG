using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Audio;
using YARG.Career;
using YARG.Core.Audio;
using YARG.Core.Song;
using YARG.Core.Game;
using YARG.Core.Input;
using YARG.Localization;
using YARG.Menu.ListMenu;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Scores;
using YARG.Settings;
using YARG.Settings.Customization;

namespace YARG.Menu.Career
{
    /// <summary>
    /// The way into careers, in three steps on one list: pick the band, set who is playing tonight
    /// (the lineup), then pick the career. Careers are only listed once a band is chosen and one of
    /// its members is connected.
    /// </summary>
    public class CareerListMenu : ListMenu<ViewType, CareerView>
    {
        public enum Step
        {
            Bands,
            Lineup,
            Careers,
        }

        [SerializeField]
        private TextMeshProUGUI _careerNameText;

        [SerializeField]
        private TextMeshProUGUI _careerDescriptionText;

        // The band's home screen on the lineup step (see CareerBandHome).
        [SerializeField]
        private Sprite _bandFallbackSprite;
        [SerializeField]
        private Sprite _starSprite;
        [SerializeField]
        private Sprite _goldStarSprite;
        [SerializeField]
        private Sprite[] _fanSprites;
        // How much of the panel's bottom the screen's button bar covers.
        [SerializeField]
        private float _bandHomeBottomInset = 82f;

        protected override int ExtraListViewPadding => 10;

        // Kept for the session: coming back from a career returns to the career list, not the bands.
        private static Step _step = Step.Bands;

        /// <summary>
        /// Set when a career is opened from this list, so that coming back lands on the careers again.
        /// Any other way in starts at the bands.
        /// </summary>
        public static bool ReturningFromCareer { get; set; }

        public Step CurrentStep => _step;

        private bool _schemePushed;
        private bool _createPrompted;

        private bool _backButtonWired;

        /// <summary>
        /// The header's back arrow closes the menu outright (MenuObject.CloseMenu); here it steps back
        /// through careers → lineup → bands like the Back button does.
        /// </summary>
        private void WireHeaderBackButton()
        {
            if (_backButtonWired)
            {
                return;
            }

            _backButtonWired = true;
            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var onClick = button.onClick;
                for (var i = 0; i < onClick.GetPersistentEventCount(); i++)
                {
                    if (onClick.GetPersistentMethodName(i) != nameof(MenuObject.CloseMenu))
                    {
                        continue;
                    }

                    onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                    onClick.AddListener(Back);
                }
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            WireHeaderBackButton();

            var keepStep = ReturningFromCareer && ActiveBand() is not null;
            ReturningFromCareer = false;
            _step = keepStep ? Step.Careers : Step.Bands;

            SetNavigation();
            Refresh();
            SelectDefaultRow();

            // Nothing to pick from: go straight to making a band.
            if (_step == Step.Bands && !_createPrompted && ScoreContainer.Careers is { } careers &&
                careers.GetBands().Count == 0)
            {
                _createPrompted = true;
                CreateBand();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            if (_schemePushed && Navigator.Instance != null)
            {
                Navigator.Instance.PopScheme();
            }

            _schemePushed = false;
            StopAnthem();
            StemSettings.ApplySettings = true;
        }

        private void SetNavigation()
        {
            if (_schemePushed)
            {
                Navigator.Instance.PopScheme();
            }

            var entries = new List<NavigationScheme.Entry>
            {
                new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up",
                    ctx => {
                        SetWrapAroundState(!ctx.IsRepeat);
                        SelectedIndex--;
                    }),
                new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down",
                    ctx => {
                        SetWrapAroundState(!ctx.IsRepeat);
                        SelectedIndex++;
                    }),
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm",
                    () => CurrentSelection?.ViewClick()),
                new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Back, hide: true),
            };

            if (_step == Step.Bands)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Blue, "Menu.Career.Band.Manage", ManageSelectedBand));
            }
            else if (_step == Step.Lineup)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Career.Lineup.AddAll", AddAllMembers));
                entries.Add(new NavigationScheme.Entry(MenuAction.Orange, "Menu.Career.Lineup.RemoveOthers", RemoveNonMembers));
                entries.Add(new NavigationScheme.Entry(MenuAction.Blue, "Menu.Career.Band.Manage", ManageActiveBand));
            }
            else
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Blue, "Menu.Career.Band.Manage", ManageActiveBand));
            }

            _ = Navigator.Instance.PushScheme(new NavigationScheme(entries, false));
            _schemePushed = true;
        }

        private static CareerBand ActiveBand() => CareerBands.ActiveBand(ScoreContainer.Careers);

        private void GoTo(Step step)
        {
            _step = step;
            SetNavigation();
            Refresh();
            SelectDefaultRow();
        }

        /// <summary>
        /// The row a step opens on: the active band, "Continue", or the first career.
        /// </summary>
        private void SelectDefaultRow()
        {
            var active = CareerBands.ActiveBandId;
            var found = _step switch
            {
                Step.Bands => SetIndexTo(view => view is BandChoiceViewType band && band.Band.Id == active),
                Step.Careers => SetIndexTo(view => view is CareerViewType),
                _ => false,
            };

            if (!found)
            {
                SelectedIndex = 0;
            }
        }

        protected override List<ViewType> CreateViewList()
        {
            var viewList = new List<ViewType>();
            var careers = ScoreContainer.Careers;

            if (CustomContentManager.Careers == null || careers is null)
            {
                return viewList;
            }

            // A band that is gone (deleted) sends the later steps back to the start.
            if (_step != Step.Bands && ActiveBand() is null)
            {
                _step = Step.Bands;
            }

            switch (_step)
            {
                case Step.Bands:
                    AddBandRows(viewList, careers);
                    break;
                case Step.Lineup:
                    AddLineupRows(viewList, careers);
                    break;
                case Step.Careers:
                    AddCareerRows(viewList);
                    break;
            }

            return viewList;
        }

        private void AddBandRows(List<ViewType> viewList, CareerDatabase careers)
        {
            var bands = careers.GetBands();
            foreach (var band in bands)
            {
                viewList.Add(new BandChoiceViewType(band, SelectBand));
            }

            viewList.Add(new CareerActionViewType(Localize.Key("Menu.Career.Band.New"),
                Localize.Key(bands.Count == 0 ? "Menu.Career.Band.NewHintNone" : "Menu.Career.Band.NewHint"),
                CreateBand));
        }

        private void AddLineupRows(List<ViewType> viewList, CareerDatabase careers)
        {
            var band = ActiveBand();
            var members = careers.GetBandMembers(band.Id);
            var memberConnected = MemberConnected(members);

            // Set apart at the top like the music library's "Playlist Manager": on to the setlists.
            viewList.Add(new CareerActionViewType(
                Localize.Key("Menu.Career.Lineup.Continue"),
                memberConnected ? string.Empty : Localize.Key("Menu.Career.Lineup.NeedMemberShort"),
                Continue, BaseViewType.BackgroundType.Button));

            // The band's members, then everyone else who can fill in, each in profile order.
            var bandMates = PlayerContainer.Profiles.Where(profile => members.Contains(profile.Id)).ToList();
            var fillIns = PlayerContainer.Profiles.Where(profile => !members.Contains(profile.Id)).ToList();

            viewList.Add(new CareerDividerViewType(Localize.Key("Menu.Career.Lineup.BandMates"), ProfileCount(bandMates.Count)));
            foreach (var profile in bandMates)
            {
                viewList.Add(new LineupProfileViewType(profile, true, ToggleProfile));
            }

            if (fillIns.Count > 0)
            {
                viewList.Add(new CareerDividerViewType(Localize.Key("Menu.Career.Lineup.FillIns"), ProfileCount(fillIns.Count)));
                foreach (var profile in fillIns)
                {
                    viewList.Add(new LineupProfileViewType(profile, false, ToggleProfile));
                }
            }
        }

        private void AddCareerRows(List<ViewType> viewList)
        {
            foreach (var career in CustomContentManager.Careers.DefaultPresets)
            {
                viewList.Add(new CareerViewType(career));
            }

            foreach (var career in CustomContentManager.Careers.CustomPresets)
            {
                viewList.Add(new CareerViewType(career));
            }
        }

        private static string ProfileCount(int count)
        {
            return Localize.KeyFormat(count == 1 ? "Menu.Career.Lineup.ProfileCountOne" : "Menu.Career.Lineup.ProfileCount", count);
        }

        private static bool MemberConnected(List<Guid> members)
        {
            return PlayerContainer.Players.Any(player => members.Contains(player.Profile.Id));
        }

        private static string ConnectedNames()
        {
            var names = PlayerContainer.Players.Select(player => player.Profile.Name).ToList();
            return names.Count == 0 ? Localize.Key("Menu.Career.Lineup.Nobody") : Localize.List(names);
        }

        private void SelectBand(CareerBand band)
        {
            CareerBands.ActiveBandId = band.Id;
            GoTo(Step.Lineup);
        }

        private void CreateBand()
        {
            if (DialogManager.Instance.IsDialogShowing)
            {
                return;
            }

            // The dialogs make the new band the active one; then it is on to its lineup.
            var before = CareerBands.ActiveBandId;
            CareerBandDialogs.CreateBand(() =>
            {
                if (CareerBands.ActiveBandId != before && ActiveBand() is not null && _step == Step.Bands)
                {
                    GoTo(Step.Lineup);
                }
                else
                {
                    Refresh();
                }
            });
        }

        private void ManageSelectedBand()
        {
            if (CurrentSelection is BandChoiceViewType row)
            {
                CareerBandDialogs.ShowManage(row.Band, Refresh);
            }
        }

        /// <summary>
        /// Manage Band on the lineup: the chosen band. Deleting it there sends the list back to the bands.
        /// </summary>
        private void ManageActiveBand()
        {
            if (ActiveBand() is { } band)
            {
                CareerBandDialogs.ShowManage(band, () =>
                {
                    if (ActiveBand() is null)
                    {
                        GoTo(Step.Bands);
                    }
                    else
                    {
                        Refresh();
                    }
                });
            }
        }

        private void ToggleProfile(YargProfile profile)
        {
            var player = PlayerContainer.GetPlayerFromProfile(profile);
            if (player is not null)
            {
                PlayerContainer.DisposePlayer(player);
            }
            else if (PlayerContainer.CreatePlayerFromProfile(profile, true) is null)
            {
                ToastManager.ToastWarning(Localize.KeyFormat("Menu.Career.Lineup.ConnectFailed", profile.Name));
            }

            Refresh();
        }

        private static List<Guid> ActiveMembers()
        {
            var band = ActiveBand();
            return band is null ? new List<Guid>() : ScoreContainer.Careers.GetBandMembers(band.Id);
        }

        private void AddAllMembers()
        {
            var members = ActiveMembers();
            foreach (var profile in PlayerContainer.Profiles.Where(profile => members.Contains(profile.Id)).ToList())
            {
                if (PlayerContainer.GetPlayerFromProfile(profile) is null &&
                    PlayerContainer.CreatePlayerFromProfile(profile, true) is null)
                {
                    ToastManager.ToastWarning(Localize.KeyFormat("Menu.Career.Lineup.ConnectFailed", profile.Name));
                }
            }

            Refresh();
        }

        private void RemoveNonMembers()
        {
            var members = ActiveMembers();
            foreach (var player in PlayerContainer.Players.Where(player => !members.Contains(player.Profile.Id)).ToList())
            {
                PlayerContainer.DisposePlayer(player);
            }

            Refresh();
        }

        /// <summary>
        /// On to the careers, which needs a member of the band among the players: without one nothing
        /// played would count for the band.
        /// </summary>
        private void Continue()
        {
            var band = ActiveBand();
            if (band is null || !MemberConnected(ScoreContainer.Careers.GetBandMembers(band.Id)))
            {
                ToastManager.ToastWarning(Localize.Key("Menu.Career.Lineup.NeedMemberToast"));
                return;
            }

            GoTo(Step.Careers);
        }

        protected override void OnSelectedIndexChanged()
        {
            base.OnSelectedIndexChanged();
            UpdateHeader();
        }

        private void UpdateHeader()
        {
            // The band's home screen stays up from the lineup on through the setlists.
            var band = _step != Step.Bands ? ActiveBand() : null;

            _title ??= new CareerHeaderTitle(this);
            if (band is null)
            {
                _title.ShowBandSelection();
            }
            else
            {
                _title.ShowName(band.Name);
            }

            UpdateAnthem(band);
            if (band is null)
            {
                ShowBandHome(null);
                _careerNameText.text = CurrentSelection?.GetPrimaryText(true);
                _careerDescriptionText.text = CurrentSelection?.GetSecondaryText(true);
                return;
            }

            ShowBandHome(band);
        }

        private CareerHeaderTitle _title;
        private CareerBandHome _bandHome;
        private GameObject _textColumn;
        private Guid? _sheetBand;
        private CareerBandSheet _sheet;

        /// <summary>
        /// The lineup step turns the left panel into the band's home screen (see
        /// <see cref="CareerBandHome"/>); the other steps show the selected row's name and description.
        /// </summary>
        private void ShowBandHome(CareerBand band)
        {
            if (_textColumn == null)
            {
                var layout = _careerNameText.GetComponentInParent<VerticalLayoutGroup>(true);
                _textColumn = layout != null ? layout.gameObject : _careerNameText.gameObject;
            }

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
                // Beside the text column, over the whole panel.
                _bandHome = CareerBandHome.Create((RectTransform) _textColumn.transform.parent, new CareerBandHome.Art
                {
                    Star = _starSprite,
                    GoldStar = _goldStarSprite,
                    Fans = _fanSprites,
                    TitleTemplate = _careerNameText,
                    BodyTemplate = _careerDescriptionText,
                }, _bandHomeBottomInset);
            }

            // The band's totals across all its careers. Loaded once per band (nothing on the lineup
            // changes them); Refresh drops it when the band may have changed.
            if (_sheet is null || _sheetBand != band.Id)
            {
                _sheet = CareerBandSheet.Load(ScoreContainer.Careers, band.Id, CareerBands.AllCareers());
                _sheetBand = band.Id;
            }

            // What is in the way, if anything: no band mate connected, or a connected player without
            // a controller.
            string notice = null;
            if (!MemberConnected(ScoreContainer.Careers.GetBandMembers(band.Id)))
            {
                notice = Localize.Key("Menu.Career.Lineup.NeedMemberShort");
            }
            else if (CurrentSelection is LineupProfileViewType profile &&
                PlayerContainer.GetPlayerFromProfile(profile.Profile) is { IsMissingController: true })
            {
                notice = Localize.KeyFormat("Menu.Career.Lineup.ProfileNoController", profile.Profile.Name);
            }

            var custom = CareerIcons.BandIcon(band.Id);
            _textColumn.SetActive(false);
            _bandHome.Show(band, _sheet, custom != null ? custom : _bandFallbackSprite, custom != null, notice);
        }

        /// <summary>
        /// Rebuilds the list and the header, after the bands or the players changed.
        /// </summary>
        public void Refresh()
        {
            _sheet = null;
            RequestViewListUpdate();
            UpdateHeader();
        }

        private const double ANTHEM_FADE_DURATION = 1.25;

        private readonly PreviewPlayer _anthemPlayer = new();
        private Guid? _anthemBand;

        /// <summary>
        /// One of the band's own songs plays behind its home screen, from the moment the band is
        /// picked until the list is left or goes back to the bands (see <see cref="CareerBandAnthem"/>).
        /// </summary>
        private void UpdateAnthem(CareerBand band)
        {
            if (band?.Id == _anthemBand)
            {
                return;
            }

            StopAnthem();
            if (band is null)
            {
                return;
            }

            _anthemBand = band.Id;

            var volume = SettingsManager.Settings.PreviewVolume.Value;
            var song = volume > 0 ? PickAnthem(band) : null;
            if (song is null)
            {
                return;
            }

            StemSettings.ApplySettings = SettingsManager.Settings.ApplyVolumesInMusicLibrary.Value;
            _ = _anthemPlayer.StartPreview(song, volume, 1f, 0, ANTHEM_FADE_DURATION,
                SettingsManager.Settings.CensorMatureContent.Value);
        }

        /// <summary>
        /// A random song the band has stars on, each as likely as its best stars there.
        /// </summary>
        private static SongEntry PickAnthem(CareerBand band)
        {
            try
            {
                var sheet = CareerBandSheet.Load(ScoreContainer.Careers, band.Id, CareerBands.AllCareers());
                var songs = sheet.History
                    .Where(gig => gig.Song?.SongEntry is not null && gig.Stars > 0)
                    .GroupBy(gig => gig.Song.SongEntry)
                    .Select(group => (song: group.Key, stars: group.Max(gig => gig.Stars)))
                    .ToList();

                var weights = songs.Select(entry => entry.stars).ToList();
                var total = CareerBandAnthem.Total(weights);
                var index = total > 0 ? CareerBandAnthem.Pick(weights, UnityEngine.Random.Range(0, total)) : -1;
                return index >= 0 ? songs[index].song : null;
            }
            catch (Exception e)
            {
                YARG.Core.Logging.YargLogger.LogException(e, "Career: failed to pick the band's song.");
                return null;
            }
        }

        private void StopAnthem()
        {
            _anthemBand = null;
            _ = _anthemPlayer.StopPreview();
        }

        private void OnDestroy()
        {
            _anthemPlayer.Dispose();
        }

        private void Back()
        {
            switch (_step)
            {
                case Step.Careers:
                    GoTo(Step.Lineup);
                    break;
                case Step.Lineup:
                    GoTo(Step.Bands);
                    break;
                default:
                    MenuManager.Instance.PopMenu();
                    break;
            }
        }
    }
}
