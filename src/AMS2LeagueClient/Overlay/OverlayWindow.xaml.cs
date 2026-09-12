using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Overlay
{
    public partial class OverlayWindow : Window
    {
        private enum DisplayMode
        {
            Gameplay,
            Waiting
        }

        private readonly bool _diagnostic;
        private PedalTelemetryView? _pedalView;
        private LegacyPedalTelemetryView? _legacyPedalView;
        private ContentControl? _telemetryHost;
        private OverlayHudView? TimingHud;
        private PedalTelemetryView? _pedalGaugeView;
        private DrivingDashboardView? _dashboardView;
        private AvanteClusterView? _avanteView;
        private AvanteClusterView? _avanteExpandedView;
        private DrivingNumberView? _speedView;
        private DrivingNumberView? _gearView;
        private readonly AuxiliaryOverlayWindow?[] _drivingWindows = new AuxiliaryOverlayWindow?[7];
        private readonly DrivingTelemetryHistory _drivingHistory = new DrivingTelemetryHistory();
        private double _lastTrackTemperature = double.NaN;
        public void ResetDrivingTelemetry() { _drivingHistory.Clear(); RefreshDrivingViews(); }
        private DrivingTelemetryHistory? _drivingPreview;
        private readonly DispatcherTimer _drivingPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        private RelativeDriversView? _relativeView;
        private LapTimingView? _lapTimingView;
        private SessionInfoView? _sessionView;
        private EventCardView? _eventView;
        private RaceControlView? _raceControlView;
        private MultiplayerWaitingOverlayView? _waitingView;
        private AuxiliaryOverlayWindow? _relativeWindow;
        private AuxiliaryOverlayWindow? _lapTimingWindow;
        private AuxiliaryOverlayWindow? _sessionWindow;
        private AuxiliaryOverlayWindow? _eventWindow;
        private AuxiliaryOverlayWindow? _raceControlWindow;
        private AuxiliaryOverlayWindow? _waitingWindow;
        private readonly OverlayLayoutStore _layoutStore;
        private OverlayLayoutProfile _layoutProfile;
        private IntPtr _handle;
        private OverlayShellViewModel _viewModel = new OverlayShellViewModel();
        private GameWindowSnapshot? _lastGameWindow;
        private MultiplayerWaitingOverlayViewModel? _lastWaitingViewModel;
        private DisplayMode _displayMode = DisplayMode.Gameplay;
        private string _lastBoundsKey = string.Empty;
        private string _lastSessionKey = string.Empty;
        private string _lastEventKey = string.Empty;
        private string _lastRaceControlKey = string.Empty;
        private string _lastWaitingKey = string.Empty;
        private DateTime _eventExitDeadline = DateTime.MinValue;
        private DateTime _raceControlExitDeadline = DateTime.MinValue;
        private bool _layoutEditing;
        private bool _layoutPreview;
        private OverlayShellViewModel? _liveViewModelBeforePreview;
        private bool _closing;

        public OverlayWindow(bool diagnostic, string? layoutPath = null)
        {
            _diagnostic = diagnostic;
            InitializeComponent();
            SizeChanged += (sender, args) => ResizeTimingPreview();
            string resolvedLayoutPath = layoutPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AMS2KRLeague",
                "overlay-layout.json");
            _layoutStore = new OverlayLayoutStore(resolvedLayoutPath);
            _layoutProfile = _layoutStore.Load();
            _layoutProfile.SetDrivingPanelDefaults();

            SynchronizeSurfaces();
            _drivingPreviewTimer.Tick += (sender, args) =>
            {
                _drivingPreview?.Add(DemoSnapshotFactory.CreatePreviewSample(DateTimeOffset.UtcNow));
                RefreshDrivingViews();
            };
        }

        private void SynchronizeSurfaces()
        {
            _lastSessionKey = _lastEventKey = _lastRaceControlKey = _lastWaitingKey = string.Empty;
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.TimingTower))
                TimingHost.Child = TimingHud ??= new OverlayHudView();
            else
            {
                if (IsVisible) Hide();
                TimingHost.Child = null;
                TimingHud = null;
            }
            SynchronizeSurface(ref _relativeWindow, OverlayComponentKeys.RelativeDrivers,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.RelativeDrivers, "전후방 거리",
                    _relativeView = new RelativeDriversView(), OverlayUiMetrics.RelativeWidth, OverlayUiMetrics.RelativeHeight));
            if (_relativeWindow == null) _relativeView = null;
            SynchronizeSurface(ref _lapTimingWindow, OverlayComponentKeys.LapTiming,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.LapTiming, "현재·섹터 타임",
                    _lapTimingView = new LapTimingView(), OverlayUiMetrics.LapTimingWidth, OverlayUiMetrics.LapTimingHeight));
            if (_lapTimingWindow == null) _lapTimingView = null;
            SynchronizeSurface(ref _sessionWindow, OverlayComponentKeys.SessionInfo,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.SessionInfo, "세션 정보",
                    _sessionView = new SessionInfoView(), OverlayUiMetrics.SessionWidth, OverlayUiMetrics.SessionHeight));
            if (_sessionWindow == null) _sessionView = null;
            SynchronizeSurface(ref _eventWindow, OverlayComponentKeys.EventCard,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.EventCard, "이벤트 카드",
                    _eventView = new EventCardView(), OverlayUiMetrics.EventWidth, OverlayUiMetrics.EventHeight));
            if (_eventWindow == null) _eventView = null;
            SynchronizeSurface(ref _raceControlWindow, OverlayComponentKeys.RaceControl,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.RaceControl, "레이스 컨트롤",
                    _raceControlView = new RaceControlView(), OverlayUiMetrics.RaceControlExpandedWidth, OverlayUiMetrics.RaceControlExpandedHeight));
            if (_raceControlWindow == null) _raceControlView = null;
            SynchronizeSurface(ref _waitingWindow, OverlayComponentKeys.Waiting,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.Waiting, "멀티 대기 화면",
                    _waitingView = new MultiplayerWaitingOverlayView { DataContext = _lastWaitingViewModel }, OverlayUiMetrics.WaitingWidth, OverlayUiMetrics.WaitingHeight));
            if (_waitingWindow == null) _waitingView = null;
            SynchronizeSurface(ref _drivingWindows[0], OverlayComponentKeys.PedalTelemetry,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.PedalTelemetry, "텔레메트리",
                    _telemetryHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch }, OverlayUiMetrics.PedalWidth, OverlayUiMetrics.PedalHeight));
            if (_drivingWindows[0] == null) _telemetryHost = null;
            SynchronizeSurface(ref _drivingWindows[1], OverlayComponentKeys.PedalGauge,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.PedalGauge, "페달 게이지",
                    _pedalGaugeView = new PedalTelemetryView(true), OverlayUiMetrics.PedalGaugeWidth, OverlayUiMetrics.PedalHeight));
            if (_drivingWindows[1] == null) _pedalGaugeView = null;
            SynchronizeSurface(ref _drivingWindows[2], OverlayComponentKeys.Speed,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.Speed, "속도계",
                    _speedView = new DrivingNumberView(false), OverlayUiMetrics.SpeedWidth, OverlayUiMetrics.SpeedHeight));
            if (_drivingWindows[2] == null) _speedView = null;
            SynchronizeSurface(ref _drivingWindows[3], OverlayComponentKeys.Gear,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.Gear, "기어",
                    _gearView = new DrivingNumberView(true), OverlayUiMetrics.GearSize, OverlayUiMetrics.GearSize));
            if (_drivingWindows[3] == null) _gearView = null;
            SynchronizeSurface(ref _drivingWindows[4], OverlayComponentKeys.DrivingDashboard,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.DrivingDashboard, "레이싱 계기판",
                    _dashboardView = new DrivingDashboardView(), OverlayUiMetrics.DashboardWidth, OverlayUiMetrics.DashboardHeight));
            if (_drivingWindows[4] == null) _dashboardView = null;
            SynchronizeSurface(ref _drivingWindows[5], OverlayComponentKeys.AvanteCluster,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.AvanteCluster, "아반떼 N 계기판 · 일반형",
                    _avanteView = new AvanteClusterView(), 454, 375));
            if (_drivingWindows[5] == null) _avanteView = null;
            SynchronizeSurface(ref _drivingWindows[6], OverlayComponentKeys.AvanteClusterExpanded,
                () => new AuxiliaryOverlayWindow(OverlayComponentKeys.AvanteClusterExpanded, "아반떼 N 계기판 · 확장형",
                    _avanteExpandedView = new AvanteClusterView(true), 820, 300));
            if (_drivingWindows[6] == null) _avanteExpandedView = null;
            if (_telemetryHost == null) { _pedalView = null; _legacyPedalView = null; }
            if (Array.TrueForAll(_drivingWindows, panel => panel == null)) _drivingHistory.Clear();
            ApplyDrivingAppearance();
            ApplyComponentOpacities();
            ApplyOutputVisibility();
        }

        private void SynchronizeSurface(ref AuxiliaryOverlayWindow? window, string component, Func<AuxiliaryOverlayWindow> create)
        {
            if (!_layoutProfile.IsEnabled(component))
            {
                if (window == null) return;
                window.HideOverlay();
                window.Content = null;
                window.Close();
                window = null;
            }
            else if (window == null)
            {
                window = create();
                window.SetEditMode(_layoutEditing);
                window.IsVisibleChanged += (_, args) => RefreshDrivingViews();
            }
        }

        public string AvanteVehicleName { get; private set; } = "";
        public string AvanteProfileVehicleName { get; private set; } = "";
        public double? AvanteEngineMaximum { get; private set; }
        public DrivingHudSettings GetDrivingHudSettings() => (_layoutProfile.DrivingHud ?? new DrivingHudSettings()).Normalize();

        public void SaveDrivingHudSettings(DrivingHudSettings settings)
        {
            DrivingHudSettings previous = _layoutProfile.DrivingHud;
            _layoutProfile.DrivingHud = settings.Normalize();
            try { _layoutStore.Save(_layoutProfile); }
            catch { _layoutProfile.DrivingHud = previous; throw; }
            ApplyDrivingAppearance();
            ApplyComponentOpacities();
        }

        private void ApplyDrivingAppearance()
        {
            DrivingHudSettings settings = GetDrivingHudSettings();
            if (_telemetryHost != null)
            {
                if (settings.TelemetryDesign == "racing")
                {
                    _pedalView ??= new PedalTelemetryView();
                    _telemetryHost.Content = _pedalView;
                    _legacyPedalView = null;
                }
                else
                {
                    _legacyPedalView ??= new LegacyPedalTelemetryView();
                    _telemetryHost.Content = _legacyPedalView;
                    _pedalView = null;
                }
            }
            TimingHud?.SetRacingDesign(settings.TowerDesign == "racing");
            Title = settings.TowerDesign == "racing" ? "AMS2 순위 타워 (개량)" : "AMS2 순위 타워 (기본)";
            if (_drivingWindows[0] != null) _drivingWindows[0]!.Title = settings.TelemetryDesign == "racing" ? "AMS2 텔레메트리 (개량)" : "AMS2 텔레메트리 (기본)";
            _legacyPedalView?.ApplySettings(settings);
            _pedalView?.ApplySettings(settings);
            _pedalGaugeView?.ApplySettings(settings);
            _dashboardView?.ApplySettings(settings);
            _avanteView?.ApplySettings(settings); _avanteExpandedView?.ApplySettings(settings);
            _speedView?.ApplyFont(settings.SpeedFont);
            _gearView?.ApplyFont(settings.GearFont);
            _speedView?.ApplyShadow(settings.SpeedShadowColor);
            _gearView?.ApplyShadow(settings.GearShadowColor);
            RefreshDrivingViews();
        }

        public event EventHandler? DrivingTelemetryDemandChanged;
        private bool _drivingDemand;
        private bool IsDrivingVisible(int index) => _drivingWindows[index]?.IsVisible == true &&
            _layoutProfile.GetOpacity(_drivingWindows[index]!.ComponentKey) > 0;
        public bool WantsDrivingTelemetry
        { get { if (_layoutPreview) return false; for (int i=0;i<_drivingWindows.Length;i++) if (IsDrivingVisible(i)) return true; return false; } }
        public bool HasPresentationDemand
        { get { foreach (string key in OverlayComponentKeys.All) if (key != OverlayComponentKeys.Waiting && IsComponentActive(key)) return true; return false; } }
        private bool IsComponentActive(string key) => _layoutProfile.IsEnabled(key) && _layoutProfile.GetOpacity(key) > 0;
        private void NotifyDrivingDemand()
        { bool demand = WantsDrivingTelemetry; if (demand == _drivingDemand) return; _drivingDemand = demand; DrivingTelemetryDemandChanged?.Invoke(this, EventArgs.Empty); }

        public void UpdateDrivingSession(TelemetrySnapshot snapshot)
        {
            string profileVehicle = AvanteRpmScale.ProfileVehicleName(snapshot);
            if (snapshot.RootCarName != AvanteVehicleName || profileVehicle != AvanteProfileVehicleName)
            { AvanteVehicleName = snapshot.RootCarName; AvanteEngineMaximum = null; }
            AvanteProfileVehicleName = profileVehicle;
            double? maximum = snapshot.ViewedVehicleTelemetry?.MaxRpm;
            if (AvanteRpmScale.IsValidEngineMaximum(maximum)) AvanteEngineMaximum = maximum;
            _lastTrackTemperature = snapshot.TrackTemperature;
            if (IsDrivingVisible(5)) _avanteView?.SetSession(snapshot);
            if (IsDrivingVisible(6)) _avanteExpandedView?.SetSession(snapshot);
            if (IsDrivingVisible(4)) _dashboardView?.SetSession(_lastTrackTemperature, _viewModel.Timing.RemainingTimeText);
        }

        public void UpdateDrivingTelemetry(TelemetrySnapshot snapshot, int localIndex, int generation)
        {
            if (_layoutPreview) return;
            UpdateDrivingSession(snapshot);
            UpdateDrivingSample(DrivingTelemetrySample.FromSnapshot(snapshot, localIndex, generation));
        }

        public void UpdateDrivingSample(DrivingTelemetrySample? sample)
        {
            if (_layoutPreview) return;
            DrivingTelemetrySample? previous = _drivingHistory.Current;
            _drivingHistory.Add(sample);
            if (!ReferenceEquals(previous, _drivingHistory.Current)) RefreshDrivingViews();
        }

        private void RefreshDrivingViews()
        {
            bool animatePreview = _layoutEditing && _displayMode == DisplayMode.Gameplay && _drivingHistory.Current == null
                && Array.Exists(_drivingWindows, panel => panel?.IsVisible == true);
            if (animatePreview && !_drivingPreviewTimer.IsEnabled)
            {
                _drivingPreview = DemoSnapshotFactory.CreateDrivingPreview();
                _drivingPreviewTimer.Start();
            }
            else if (!animatePreview) { _drivingPreviewTimer.Stop(); _drivingPreview = null; }
            if (IsDrivingVisible(4))
                _dashboardView?.SetSession(_layoutEditing && _drivingHistory.Current == null ? 27 : _lastTrackTemperature,
                    _layoutEditing && _drivingHistory.Current == null ? "14:03" : _viewModel.Timing.RemainingTimeText);
            DrivingTelemetryHistory shown = _layoutEditing && _drivingHistory.Current == null ? _drivingPreview ?? _drivingHistory : _drivingHistory;
            if (IsDrivingVisible(0) && ReferenceEquals(_telemetryHost?.Content, _legacyPedalView)) _legacyPedalView?.SetHistory(shown);
            if (IsDrivingVisible(0) && ReferenceEquals(_telemetryHost?.Content, _pedalView)) _pedalView?.SetHistory(shown);
            if (IsDrivingVisible(1)) _pedalGaugeView?.SetHistory(shown);
            if (IsDrivingVisible(2)) _speedView?.SetSample(shown.Current);
            if (IsDrivingVisible(3)) _gearView?.SetSample(shown.Current);
            if (IsDrivingVisible(5)) _avanteView?.SetSample(shown.Current, animatePreview);
            if (IsDrivingVisible(6)) _avanteExpandedView?.SetSample(shown.Current, animatePreview);
            if (IsDrivingVisible(4)) _dashboardView?.SetSample(shown.Current, _layoutEditing && _drivingHistory.Current == null ? "P12" : _viewModel.Timing.PositionText.Split('/')[0].Trim());
            NotifyDrivingDemand();
        }

        public bool IsLayoutEditing => _layoutEditing;
        public bool IsLayoutPreview => _layoutPreview;

        public bool IsComponentEnabled(string component)
            => _layoutProfile.IsEnabled(component);

        public int GetTimingTowerRowCapacity(GameWindowSnapshot gameWindow)
        {
            if (gameWindow == null) throw new ArgumentNullException(nameof(gameWindow));
            OverlayBounds bounds;
            if (_layoutEditing && IsVisible && _handle != IntPtr.Zero)
            {
                bounds = OverlayWindowInterop.ReadPhysicalBounds(_handle);
            }
            else
            {
                OverlayComponentLayout defaults = OverlayComponentLayoutCalculator.Calculate(
                    gameWindow.Width,
                    gameWindow.Height,
                    gameWindow.Dpi,
                    _diagnostic,
                    _viewModel.RaceControl.IsExpanded);
                bounds = Resolve(OverlayComponentKeys.TimingTower, defaults.Timing, gameWindow);
            }

            return LeftTowerLayoutMetrics.CalculateRankingRows(bounds.Width, bounds.Height, _diagnostic);
        }

        /// <summary>
        /// Turns one overlay surface on or off. The choice is persisted
        /// immediately so it survives restarts without entering layout edit mode.
        /// </summary>
        public double GetComponentOpacity(string component) => _layoutProfile.GetOpacity(component);
        public void SetComponentOpacity(string component, double opacity)
        {
            double previous = _layoutProfile.GetOpacity(component);
            _layoutProfile.SetOpacity(component, opacity);
            try { _layoutStore.Save(_layoutProfile); }
            catch { _layoutProfile.SetOpacity(component, previous); throw; }
            ApplyComponentOpacities();
        }
        private void ApplyComponentOpacities()
        {
            foreach (Window window in HudWindows())
                if (window.Content is Grid root && root.Children.Count > 0)
                {
                    double opacity = _layoutProfile.GetOpacity(window is AuxiliaryOverlayWindow panel ? panel.ComponentKey : OverlayComponentKeys.TimingTower);
                    root.Children[0].Opacity = opacity;
                    root.Children[0].Visibility = opacity > 0 ? Visibility.Visible : Visibility.Hidden;
                }
            RefreshDrivingViews();
        }

        public void SetComponentEnabled(string component, bool enabled)
        {
            bool previous = _layoutProfile.IsEnabled(component);
            if (_layoutEditing && !enabled) CaptureLayout();
            _layoutProfile.SetEnabled(component, enabled);
            try { _layoutStore.Save(_layoutProfile); }
            catch { _layoutProfile.SetEnabled(component, previous); throw; }
            SynchronizeSurfaces();
            _lastSessionKey = _lastEventKey = _lastRaceControlKey = _lastWaitingKey = string.Empty;
            ApplyViewModel(_viewModel, false);
            if (_lastGameWindow == null) return;
            InvalidateBounds();
            if (_displayMode == DisplayMode.Waiting)
            {
                HideGameplayWindows();
                ShowWaitingSurface(_lastGameWindow, _layoutEditing);
            }
            else
            {
                _waitingWindow?.HideOverlay();
                ShowGameplaySurfaces(_lastGameWindow, _layoutEditing);
            }
        }

        protected override void OnSourceInitialized(EventArgs eventArgs)
        {
            base.OnSourceInitialized(eventArgs);
            _handle = new WindowInteropHelper(this).Handle;
            OverlayWindowInterop.Configure(_handle);
            OverlayWindowInterop.SetEditMode(_handle, _layoutEditing);
        }

        protected override void OnClosed(EventArgs eventArgs)
        {
            _closing = true;
            _drivingPreviewTimer.Stop();
            StopVr();
            if (_layoutEditing)
            {
                CaptureLayout();
                _layoutStore.Save(_layoutProfile);
            }
            OverlayWindowInterop.Forget(_handle);
            _relativeWindow?.Close();
            _lapTimingWindow?.Close();
            _sessionWindow?.Close();
            _eventWindow?.Close();
            _raceControlWindow?.Close();
            _waitingWindow?.Close();
            foreach (AuxiliaryOverlayWindow? panel in _drivingWindows) panel?.Close();
            base.OnClosed(eventArgs);
        }

        public void SetViewModel(OverlayShellViewModel viewModel, bool animate = true)
        {
            if (_layoutPreview)
            {
                _liveViewModelBeforePreview = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
                return;
            }
            ApplyViewModel(viewModel, animate);
        }

        private void ApplyViewModel(OverlayShellViewModel viewModel, bool animate)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            if (IsComponentActive(OverlayComponentKeys.TimingTower))
            {
                ResizeTimingPreview();
                TimingHud?.SetViewModel(viewModel.Timing);
            }
            if (IsComponentActive(OverlayComponentKeys.RelativeDrivers)) _relativeView?.SetViewModel(viewModel.Timing);
            if (IsComponentActive(OverlayComponentKeys.LapTiming)) _lapTimingView?.SetViewModel(viewModel.Timing);

            string sessionKey = viewModel.Session.PrimaryLabel + "\u001f" + viewModel.Session.PrimaryValue + "\u001f"
                + viewModel.Session.PositionValue + "\u001f" + viewModel.Session.LapValue;
            if (IsComponentActive(OverlayComponentKeys.SessionInfo) && sessionKey != _lastSessionKey)
            {
                _lastSessionKey = sessionKey;
                _sessionView?.SetViewModel(viewModel.Session);
            }

            // Presentation-only previews never enter the live event model or capture pipeline.
            EventCardViewModel eventCard = _layoutEditing && !viewModel.EventCard.IsVisible
                ? new EventCardViewModel { EventId = "layout-preview-event", IsVisible = true, IsDemo = true,
                    Title = "이벤트 미리보기", PrimaryText = "개인 최고 기록", SecondaryText = "1:42.350" }
                : viewModel.EventCard;
            RaceControlViewModel raceControl = _layoutEditing && !viewModel.RaceControl.IsVisible
                ? new RaceControlViewModel { EventId = "layout-preview-race-control", IsVisible = true,
                    Title = "미리보기", StateLabel = "황색기" }
                : viewModel.RaceControl;

            string eventKey = eventCard.EventId + "\u001f" + eventCard.IsVisible + "\u001f" + eventCard.SecondaryText;
            if (IsComponentActive(OverlayComponentKeys.EventCard) && eventKey != _lastEventKey)
            {
                _lastEventKey = eventKey;
                TimeSpan exit = _eventView?.SetViewModel(eventCard, animate && !_layoutEditing) ?? TimeSpan.Zero;
                _eventExitDeadline = exit > TimeSpan.Zero ? DateTime.UtcNow + exit : DateTime.MinValue;
            }

            string raceControlKey = raceControl.EventId + "\u001f" + raceControl.IsVisible + "\u001f"
                + raceControl.IsExpanded + "\u001f" + raceControl.Message + "\u001f" + raceControl.StateLabel;
            if (IsComponentActive(OverlayComponentKeys.RaceControl) && raceControlKey != _lastRaceControlKey)
            {
                _lastRaceControlKey = raceControlKey;
                TimeSpan exit = _raceControlView?.SetViewModel(raceControl, animate && !_layoutEditing) ?? TimeSpan.Zero;
                _raceControlExitDeadline = exit > TimeSpan.Zero ? DateTime.UtcNow + exit : DateTime.MinValue;
            }

            if (!_layoutEditing)
            {
                if (!viewModel.Timing.IsBottomGapPanelVisible) _relativeWindow?.HideOverlay();
                if (!IsEventCardPresentable) _eventWindow?.HideOverlay();
                if (!IsRaceControlPresentable) _raceControlWindow?.HideOverlay();
            }
        }

        private void ResizeTimingPreview()
        {
            if (TimingHud == null || (!_layoutEditing && _lastGameWindow != null) || ActualWidth <= 0 || ActualHeight <= 0) return;
            int capacity = LeftTowerLayoutMetrics.CalculateRankingRows(
                (int)Math.Round(ActualWidth), (int)Math.Round(ActualHeight), _diagnostic);
            if (_viewModel.Timing.RankingRowCapacity == capacity) return;
            _viewModel.Timing.ResizeRanking(capacity);
            TimingHud?.SetViewModel(_viewModel.Timing);
        }

        /// <summary>The event card surface is currently shown (including its exit animation).</summary>
        public bool IsEventCardSurfaceVisible => _eventWindow?.IsVisible == true;

        // A dismissed card keeps its surface only for the length of the exit
        // animation returned by the view, so the slide-out is not cut short by
        // the next 20 Hz tick.
        private bool IsEventCardPresentable
            => _viewModel.EventCard.IsVisible || DateTime.UtcNow < _eventExitDeadline;

        private bool IsRaceControlPresentable
            => _viewModel.RaceControl.IsVisible || DateTime.UtcNow < _raceControlExitDeadline;

        public OverlayStyleState GetStyleState()
            => _handle == IntPtr.Zero ? new OverlayStyleState() : OverlayWindowInterop.ReadStyleState(_handle);

        /// <summary>Presentation-only editing; synthetic values never reach the recorder.</summary>
        public void BeginLayoutPreview(bool waiting, GameWindowSnapshot? desktop = null)
        {
            desktop ??= OverlayWindowInterop.GetLayoutPreviewArea(_handle);
            if (!desktop.HasValidClientRect) throw new ArgumentException("Invalid preview area.", nameof(desktop));
            if (_layoutEditing) EndLayoutEdit(true);
            _liveViewModelBeforePreview = _viewModel;
            HideOverlay();
            _lastGameWindow = desktop;
            _displayMode = waiting ? DisplayMode.Waiting : DisplayMode.Gameplay;
            _lastWaitingViewModel = waiting ? new MultiplayerWaitingOverlayViewModel
            {
                Title = "대기 화면 미리보기", SessionLabel = "예선",
                ParticipantCountText = "리그 20 / 원본 20",
                RemainingLabel = "남은 시간", RemainingValue = "05:00"
            } : null;
            if (_waitingView != null) _waitingView.DataContext = _lastWaitingViewModel;
            ApplyViewModel(DemoSnapshotFactory.CreateShell(_diagnostic), false);
            _layoutPreview = true;
            BeginLayoutEdit();
        }

        public bool BeginLayoutEdit()
        {
            if (_layoutEditing) return true;
            if (_lastGameWindow == null || !_lastGameWindow.HasValidClientRect)
            {
                BeginLayoutPreview(false);
                return true;
            }
            _layoutEditing = true;
            SetEditMode(true);
            if (_displayMode == DisplayMode.Waiting)
            {
                HideGameplayWindows();
                ShowWaitingSurface(_lastGameWindow, true);
            }
            else
            {
                _waitingWindow?.HideOverlay();
                ShowGameplaySurfaces(_lastGameWindow, true);
            }
            return true;
        }

        public void EndLayoutEdit(bool save)
        {
            if (!_layoutEditing) return;
            if (save)
            {
                CaptureLayout();
                _layoutStore.Save(_layoutProfile);
            }
            _layoutEditing = false;
            bool wasPreview = _layoutPreview;
            _layoutPreview = false;
            if (wasPreview)
            {
                ApplyViewModel(_liveViewModelBeforePreview ?? new OverlayShellViewModel(), false);
                _liveViewModelBeforePreview = null;
                _lastWaitingViewModel = null;
                _lastWaitingKey = string.Empty;
                if (_waitingView != null) _waitingView.DataContext = null;
            }
            SetEditMode(false);
            if (wasPreview)
            {
                HideOverlay();
                return;
            }
            InvalidateBounds();
            if (_closing || _lastGameWindow == null) return;
            if (_displayMode == DisplayMode.Waiting && _lastWaitingViewModel != null)
            {
                ShowWaitingAt(_lastGameWindow, _lastWaitingViewModel);
            }
            else
            {
                ShowAt(_lastGameWindow);
            }
        }

        public void ResetLayout()
        {
            _layoutProfile = new OverlayLayoutProfile { DrivingHud = GetDrivingHudSettings(), VrHud = GetVrHudSettings(), ComponentOpacities = _layoutProfile.ComponentOpacities };
            _layoutProfile.SetDrivingPanelDefaults();
            _layoutStore.Save(_layoutProfile);
            SynchronizeSurfaces();
            ApplyViewModel(_viewModel, false);
            InvalidateBounds();
            if (_lastGameWindow == null) return;
            if (_layoutEditing)
            {
                if (_displayMode == DisplayMode.Waiting) ShowWaitingSurface(_lastGameWindow, true);
                else ShowGameplaySurfaces(_lastGameWindow, true);
                return;
            }
            if (_displayMode == DisplayMode.Waiting && _lastWaitingViewModel != null)
            {
                ShowWaitingAt(_lastGameWindow, _lastWaitingViewModel);
            }
            else
            {
                ShowAt(_lastGameWindow);
            }
        }

        public void ShowAt(GameWindowSnapshot gameWindow)
        {
            if (_layoutEditing) return;
            _lastGameWindow = gameWindow ?? throw new ArgumentNullException(nameof(gameWindow));
            _displayMode = DisplayMode.Gameplay;
            if (_layoutEditing) return;
            _waitingWindow?.HideOverlay();
            ShowGameplaySurfaces(gameWindow, false);
        }

        public void ShowWaitingAt(GameWindowSnapshot gameWindow, MultiplayerWaitingOverlayViewModel viewModel)
        {
            if (gameWindow == null) throw new ArgumentNullException(nameof(gameWindow));
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (_layoutEditing) return;
            _lastGameWindow = gameWindow;
            _lastWaitingViewModel = viewModel;
            _displayMode = DisplayMode.Waiting;

            string waitingKey = viewModel.SessionLabel + "\u001f" + viewModel.ParticipantCountText
                + "\u001f" + viewModel.RemainingLabel + "\u001f" + viewModel.RemainingValue;
            if (waitingKey != _lastWaitingKey)
            {
                _lastWaitingKey = waitingKey;
                if (_waitingView != null) _waitingView.DataContext = viewModel;
            }
            if (_layoutEditing) return;
            HideGameplayWindows();
            ShowWaitingSurface(gameWindow, false);
        }

        public void ShowDemoAt(int left, int top, uint dpi)
        {
            var demoWindow = new GameWindowSnapshot(IntPtr.Zero, left, top, Scale(1920, dpi / 96.0), Scale(1080, dpi / 96.0), dpi, true, false, 0);
            ShowAt(demoWindow);
        }

        public void HideOverlay()
        {
            if (_layoutEditing) return;
            HideGameplayWindows();
            _waitingWindow?.HideOverlay();
            _lastGameWindow = null;
            InvalidateBounds();
        }

        private void ShowGameplaySurfaces(GameWindowSnapshot gameWindow, bool includeInactive)
        {
            OverlayComponentLayout defaults = OverlayComponentLayoutCalculator.Calculate(
                gameWindow.Width,
                gameWindow.Height,
                gameWindow.Dpi,
                _diagnostic,
                _viewModel.RaceControl.IsExpanded);

            OverlayBounds[] drivingBounds = { defaults.Pedals, defaults.PedalGauge, defaults.Speed, defaults.Gear, defaults.Dashboard,
                new OverlayBounds(Math.Max(0, (gameWindow.Width - 454) / 2), Math.Max(0, gameWindow.Height - 395), 454, 375),
                new OverlayBounds(Math.Max(0, (gameWindow.Width - 820) / 2), Math.Max(0, gameWindow.Height - 320), 820, 300) };
            for (int i = 0; i < _drivingWindows.Length; i++)
            {
                AuxiliaryOverlayWindow? panel = _drivingWindows[i];
                if (panel == null) continue;
                if (_layoutProfile.IsEnabled(panel.ComponentKey))
                    panel.ShowAt(gameWindow, Resolve(panel.ComponentKey, drivingBounds[i], gameWindow));
                else panel?.HideOverlay();
            }

            if (_layoutProfile.IsEnabled(OverlayComponentKeys.TimingTower))
            {
                OverlayBounds tower = Resolve(OverlayComponentKeys.TimingTower, defaults.Timing, gameWindow);
                _viewModel.Timing.ResizeRanking(LeftTowerLayoutMetrics.CalculateRankingRows(tower.Width, tower.Height, _diagnostic));
                TimingHud?.SetViewModel(_viewModel.Timing);
                if (!_layoutEditing)
                {
                    int contentHeight = (int)Math.Ceiling(TimingHud!.Height * tower.Width / LeftTowerLayoutMetrics.Width);
                    tower = new OverlayBounds(tower.X, tower.Y, tower.Width, Math.Min(tower.Height, contentHeight));
                }
                ShowMainAt(gameWindow, tower);
            }
            else if (IsVisible)
            {
                Hide();
            }
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RelativeDrivers)
                && (includeInactive || _viewModel.Timing.IsBottomGapPanelVisible))
            {
                _relativeWindow?.ShowAt(gameWindow, Resolve(OverlayComponentKeys.RelativeDrivers, defaults.Relative, gameWindow));
            }
            else
            {
                _relativeWindow?.HideOverlay();
            }
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.LapTiming))
                _lapTimingWindow?.ShowAt(gameWindow, Resolve(OverlayComponentKeys.LapTiming, defaults.LapTiming, gameWindow));
            else
                _lapTimingWindow?.HideOverlay();
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.SessionInfo))
                _sessionWindow?.ShowAt(gameWindow, Resolve(OverlayComponentKeys.SessionInfo, defaults.Session, gameWindow));
            else
                _sessionWindow?.HideOverlay();
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.EventCard)
                && (includeInactive || IsEventCardPresentable))
            {
                _eventWindow?.ShowAt(gameWindow, Resolve(OverlayComponentKeys.EventCard, defaults.EventCard, gameWindow));
            }
            else
            {
                _eventWindow?.HideOverlay();
            }
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RaceControl)
                && (includeInactive || IsRaceControlPresentable))
            {
                _raceControlWindow?.ShowAt(gameWindow, Resolve(OverlayComponentKeys.RaceControl, defaults.RaceControl, gameWindow));
            }
            else
            {
                _raceControlWindow?.HideOverlay();
            }
        }

        private void ShowWaitingSurface(GameWindowSnapshot gameWindow, bool editing)
        {
            if (editing && _lastWaitingViewModel == null)
            {
                _lastWaitingViewModel = new MultiplayerWaitingOverlayViewModel();
                if (_waitingView != null) _waitingView.DataContext = _lastWaitingViewModel;
            }
            OverlayComponentLayout defaults = OverlayComponentLayoutCalculator.Calculate(
                gameWindow.Width,
                gameWindow.Height,
                gameWindow.Dpi,
                _diagnostic,
                false);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.Waiting))
                _waitingWindow?.ShowAt(gameWindow, Resolve(OverlayComponentKeys.Waiting, defaults.Waiting, gameWindow));
            else
                _waitingWindow?.HideOverlay();
        }

        private OverlayBounds Resolve(string component, OverlayBounds fallback, GameWindowSnapshot gameWindow)
            => _layoutProfile.Resolve(component, fallback, gameWindow.Width, gameWindow.Height);

        private void ShowMainAt(GameWindowSnapshot gameWindow, OverlayBounds bounds)
        {
            bool wasVisible = IsVisible;
            if (!IsVisible)
            {
                Show();
                _handle = new WindowInteropHelper(this).Handle;
                OverlayWindowInterop.SetEditMode(_handle, _layoutEditing);
            }
            string boundsKey = bounds.X + "," + bounds.Y + "," + bounds.Width + "x" + bounds.Height;
            if (boundsKey != _lastBoundsKey)
            {
                _lastBoundsKey = boundsKey;
                OverlayWindowInterop.SetPhysicalBounds(
                    _handle,
                    gameWindow.Left + bounds.X,
                    gameWindow.Top + bounds.Y,
                    bounds.Width,
                    bounds.Height);
            }
            if (!wasVisible && !_layoutEditing) OverlayWindowInterop.ShowWithoutActivation(_handle);
        }

        private void CaptureLayout()
        {
            if (_lastGameWindow == null) return;
            if (_displayMode == DisplayMode.Waiting)
            {
                if (_layoutProfile.IsEnabled(OverlayComponentKeys.Waiting) && _waitingWindow?.IsVisible == true)
                    Capture(OverlayComponentKeys.Waiting, _waitingWindow!.ReadPhysicalBounds(), _lastGameWindow);
                return;
            }

            foreach (AuxiliaryOverlayWindow? panel in _drivingWindows)
                if (panel != null && _layoutProfile.IsEnabled(panel.ComponentKey) && panel.IsVisible)
                    Capture(panel.ComponentKey, panel.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.TimingTower) && IsVisible)
                Capture(OverlayComponentKeys.TimingTower, OverlayWindowInterop.ReadPhysicalBounds(_handle), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RelativeDrivers) && _relativeWindow?.IsVisible == true)
                Capture(OverlayComponentKeys.RelativeDrivers, _relativeWindow!.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.LapTiming) && _lapTimingWindow?.IsVisible == true)
                Capture(OverlayComponentKeys.LapTiming, _lapTimingWindow!.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.SessionInfo) && _sessionWindow?.IsVisible == true)
                Capture(OverlayComponentKeys.SessionInfo, _sessionWindow!.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.EventCard) && _eventWindow?.IsVisible == true)
                Capture(OverlayComponentKeys.EventCard, _eventWindow!.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RaceControl) && _raceControlWindow?.IsVisible == true)
                Capture(OverlayComponentKeys.RaceControl, _raceControlWindow!.ReadPhysicalBounds(), _lastGameWindow);
        }

        private void Capture(string component, OverlayBounds screenBounds, GameWindowSnapshot gameWindow)
        {
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0) return;
            _layoutProfile.Capture(
                component,
                new OverlayBounds(
                    screenBounds.X - gameWindow.Left,
                    screenBounds.Y - gameWindow.Top,
                    screenBounds.Width,
                    screenBounds.Height),
                gameWindow.Width,
                gameWindow.Height);
        }

        private void SetEditMode(bool enabled)
        {
            EditChrome.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeMode = enabled ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
            Focusable = enabled;
            ShowActivated = enabled;
            ApplyOutputVisibility();
            OverlayWindowInterop.SetEditMode(_handle, enabled);
            _relativeWindow?.SetEditMode(enabled);
            _lapTimingWindow?.SetEditMode(enabled);
            _sessionWindow?.SetEditMode(enabled);
            _eventWindow?.SetEditMode(enabled);
            _raceControlWindow?.SetEditMode(enabled);
            _waitingWindow?.SetEditMode(enabled);
            foreach (AuxiliaryOverlayWindow? panel in _drivingWindows) panel?.SetEditMode(enabled);
            RefreshDrivingViews();
            _lastEventKey = _lastRaceControlKey = string.Empty;
            ApplyViewModel(_viewModel, false);
        }

        private void EditDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
        {
            if (_layoutEditing && eventArgs.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void HideGameplayWindows()
        {
            foreach (AuxiliaryOverlayWindow? panel in _drivingWindows) panel?.HideOverlay();
            if (_drivingHistory.Current != null) { _drivingHistory.MarkStale(); RefreshDrivingViews(); }
            if (IsVisible) Hide();
            _relativeWindow?.HideOverlay();
            _lapTimingWindow?.HideOverlay();
            _sessionWindow?.HideOverlay();
            _eventWindow?.HideOverlay();
            _raceControlWindow?.HideOverlay();
        }

        private void InvalidateBounds()
        {
            _lastBoundsKey = string.Empty;
            _relativeWindow?.InvalidateBounds();
            _lapTimingWindow?.InvalidateBounds();
            _sessionWindow?.InvalidateBounds();
            _eventWindow?.InvalidateBounds();
            _raceControlWindow?.InvalidateBounds();
            _waitingWindow?.InvalidateBounds();
            foreach (AuxiliaryOverlayWindow? panel in _drivingWindows) panel?.InvalidateBounds();
        }

        private static int Scale(int logicalPixels, double scale)
            => Math.Max(1, (int)Math.Round(logicalPixels * scale));
    }

    internal sealed class AuxiliaryOverlayWindow : Window
    {
        private readonly Grid _editChrome;
        private IntPtr _handle;
        private string _lastBoundsKey = string.Empty;
        private bool _editing;

        public AuxiliaryOverlayWindow(string componentKey, string label, FrameworkElement content, double designWidth, double designHeight)
        {
            ComponentKey = componentKey;
            Title = "AMS2 " + label;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            MinWidth = 72;
            MinHeight = 48;
            SizeToContent = SizeToContent.Manual;

            content.Width = designWidth;
            content.Height = designHeight;
            var root = new Grid();
            if (content is RaceControlView)
            {
                // This text-heavy card reflows against the actual window size.
                content.Width = content.Height = double.NaN;
                root.Children.Add(content);
            }
            else
            {
                root.Children.Add(new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    Child = content,
                    IsHitTestVisible = false
                });
                // Reflow the panel to the user's free aspect ratio, then apply
                // one uniform scale. Text must never get separate X/Y scaling.
                root.SizeChanged += (sender, args) =>
                {
                    double scale = Math.Min(args.NewSize.Width / designWidth, args.NewSize.Height / designHeight);
                    if (scale <= 0 || !double.IsFinite(scale)) return;
                    content.Width = args.NewSize.Width / scale;
                    content.Height = args.NewSize.Height / scale;
                };
            }
            _editChrome = CreateEditChrome(label);
            root.Children.Add(_editChrome);
            Content = root;

            SourceInitialized += (sender, args) =>
            {
                _handle = new WindowInteropHelper(this).Handle;
                OverlayWindowInterop.Configure(_handle);
                OverlayWindowInterop.SetEditMode(_handle, _editing);
            };
            Closed += (sender, args) => OverlayWindowInterop.Forget(_handle);
        }

        public string ComponentKey { get; }

        public void ShowAt(GameWindowSnapshot gameWindow, OverlayBounds bounds)
        {
            bool wasVisible = IsVisible;
            if (!IsVisible)
            {
                Show();
                _handle = new WindowInteropHelper(this).Handle;
                OverlayWindowInterop.SetEditMode(_handle, _editing);
            }
            string boundsKey = bounds.X + "," + bounds.Y + "," + bounds.Width + "x" + bounds.Height;
            if (boundsKey != _lastBoundsKey)
            {
                _lastBoundsKey = boundsKey;
                OverlayWindowInterop.SetPhysicalBounds(
                    _handle,
                    gameWindow.Left + bounds.X,
                    gameWindow.Top + bounds.Y,
                    bounds.Width,
                    bounds.Height);
            }
            if (!wasVisible && !_editing) OverlayWindowInterop.ShowWithoutActivation(_handle);
        }

        public void SetEditMode(bool enabled)
        {
            _editing = enabled;
            _editChrome.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            ResizeMode = enabled ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
            Focusable = enabled;
            ShowActivated = enabled;
            OverlayWindowInterop.SetEditMode(_handle, enabled);
        }

        public OverlayBounds ReadPhysicalBounds()
            => OverlayWindowInterop.ReadPhysicalBounds(_handle);

        public void HideOverlay()
        {
            if (IsVisible) Hide();
        }

        public void InvalidateBounds()
            => _lastBoundsKey = string.Empty;

        private Grid CreateEditChrome(string label)
        {
            var chrome = new Grid { Visibility = Visibility.Collapsed };
            var dragBar = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(77, 227, 177)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(24, 11, 21, 32)),
                Cursor = Cursors.SizeAll,
                ToolTip = label + " · 드래그로 이동 / 오른쪽 아래 모서리로 크기 조절"
            };
            dragBar.MouseLeftButtonDown += (sender, args) =>
            {
                if (_editing && args.LeftButton == MouseButtonState.Pressed) DragMove();
            };
            chrome.Children.Add(dragBar);
            chrome.Children.Add(new ResizeGrip
            {
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeNWSE
            });
            return chrome;
        }
    }
}
