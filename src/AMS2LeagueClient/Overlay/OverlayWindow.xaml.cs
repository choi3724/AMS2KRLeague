using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Presentation;

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
        private readonly RelativeDriversView _relativeView = new RelativeDriversView();
        private readonly LapTimingView _lapTimingView = new LapTimingView();
        private readonly SessionInfoView _sessionView = new SessionInfoView();
        private readonly EventCardView _eventView = new EventCardView();
        private readonly RaceControlView _raceControlView = new RaceControlView();
        private readonly MultiplayerWaitingOverlayView _waitingView = new MultiplayerWaitingOverlayView();
        private readonly AuxiliaryOverlayWindow _relativeWindow;
        private readonly AuxiliaryOverlayWindow _lapTimingWindow;
        private readonly AuxiliaryOverlayWindow _sessionWindow;
        private readonly AuxiliaryOverlayWindow _eventWindow;
        private readonly AuxiliaryOverlayWindow _raceControlWindow;
        private readonly AuxiliaryOverlayWindow _waitingWindow;
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

            _relativeWindow = new AuxiliaryOverlayWindow(
                OverlayComponentKeys.RelativeDrivers,
                "전후방 거리",
                _relativeView,
                OverlayUiMetrics.RelativeWidth,
                OverlayUiMetrics.RelativeHeight);
            _lapTimingWindow = new AuxiliaryOverlayWindow(
                OverlayComponentKeys.LapTiming,
                "현재·섹터 타임",
                _lapTimingView,
                OverlayUiMetrics.LapTimingWidth,
                OverlayUiMetrics.LapTimingHeight);
            _sessionWindow = new AuxiliaryOverlayWindow(
                OverlayComponentKeys.SessionInfo,
                "세션 정보",
                _sessionView,
                OverlayUiMetrics.SessionWidth,
                OverlayUiMetrics.SessionHeight);
            _eventWindow = new AuxiliaryOverlayWindow(
                OverlayComponentKeys.EventCard,
                "이벤트 카드",
                _eventView,
                OverlayUiMetrics.EventWidth,
                OverlayUiMetrics.EventHeight);
            _raceControlWindow = new AuxiliaryOverlayWindow(
                OverlayComponentKeys.RaceControl,
                "레이스 컨트롤",
                _raceControlView,
                OverlayUiMetrics.RaceControlExpandedWidth,
                OverlayUiMetrics.RaceControlExpandedHeight);
            _waitingWindow = new AuxiliaryOverlayWindow(
                OverlayComponentKeys.Waiting,
                "멀티 대기 화면",
                _waitingView,
                OverlayUiMetrics.WaitingWidth,
                OverlayUiMetrics.WaitingHeight);
        }

        public bool IsLayoutEditing => _layoutEditing;

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
        public void SetComponentEnabled(string component, bool enabled)
        {
            _layoutProfile.SetEnabled(component, enabled);
            _layoutStore.Save(_layoutProfile);
            if (_lastGameWindow == null) return;
            InvalidateBounds();
            if (_displayMode == DisplayMode.Waiting)
            {
                HideGameplayWindows();
                ShowWaitingSurface(_lastGameWindow, _layoutEditing);
            }
            else
            {
                _waitingWindow.HideOverlay();
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
            if (_layoutEditing)
            {
                CaptureLayout();
                _layoutStore.Save(_layoutProfile);
            }
            OverlayWindowInterop.Forget(_handle);
            _relativeWindow.Close();
            _lapTimingWindow.Close();
            _sessionWindow.Close();
            _eventWindow.Close();
            _raceControlWindow.Close();
            _waitingWindow.Close();
            base.OnClosed(eventArgs);
        }

        public void SetViewModel(OverlayShellViewModel viewModel, bool animate = true)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            ResizeTimingPreview();
            TimingHud.SetViewModel(viewModel.Timing);
            _relativeView.SetViewModel(viewModel.Timing);
            _lapTimingView.SetViewModel(viewModel.Timing);

            string sessionKey = viewModel.Session.PrimaryLabel + "\u001f" + viewModel.Session.PrimaryValue + "\u001f"
                + viewModel.Session.PositionValue + "\u001f" + viewModel.Session.LapValue;
            if (sessionKey != _lastSessionKey)
            {
                _lastSessionKey = sessionKey;
                _sessionView.SetViewModel(viewModel.Session);
            }

            string eventKey = viewModel.EventCard.EventId + "\u001f" + viewModel.EventCard.IsVisible + "\u001f" + viewModel.EventCard.SecondaryText;
            if (eventKey != _lastEventKey)
            {
                _lastEventKey = eventKey;
                TimeSpan exit = _eventView.SetViewModel(viewModel.EventCard, animate);
                _eventExitDeadline = exit > TimeSpan.Zero ? DateTime.UtcNow + exit : DateTime.MinValue;
            }

            string raceControlKey = viewModel.RaceControl.EventId + "\u001f" + viewModel.RaceControl.IsVisible + "\u001f"
                + viewModel.RaceControl.IsExpanded + "\u001f" + viewModel.RaceControl.Message + "\u001f" + viewModel.RaceControl.StateLabel;
            if (raceControlKey != _lastRaceControlKey)
            {
                _lastRaceControlKey = raceControlKey;
                TimeSpan exit = _raceControlView.SetViewModel(viewModel.RaceControl, animate);
                _raceControlExitDeadline = exit > TimeSpan.Zero ? DateTime.UtcNow + exit : DateTime.MinValue;
            }

            if (!_layoutEditing)
            {
                if (!viewModel.Timing.IsBottomGapPanelVisible) _relativeWindow.HideOverlay();
                if (!IsEventCardPresentable) _eventWindow.HideOverlay();
                if (!IsRaceControlPresentable) _raceControlWindow.HideOverlay();
            }
        }

        private void ResizeTimingPreview()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            int capacity = LeftTowerLayoutMetrics.CalculateRankingRows(
                (int)Math.Round(ActualWidth), (int)Math.Round(ActualHeight), _diagnostic);
            if (_viewModel.Timing.RankingRowCapacity == capacity) return;
            _viewModel.Timing.ResizeRanking(capacity);
            TimingHud.SetViewModel(_viewModel.Timing);
        }

        /// <summary>The event card surface is currently shown (including its exit animation).</summary>
        public bool IsEventCardSurfaceVisible => _eventWindow.IsVisible;

        // A dismissed card keeps its surface only for the length of the exit
        // animation returned by the view, so the slide-out is not cut short by
        // the next 20 Hz tick.
        private bool IsEventCardPresentable
            => _viewModel.EventCard.IsVisible || DateTime.UtcNow < _eventExitDeadline;

        private bool IsRaceControlPresentable
            => _viewModel.RaceControl.IsVisible || DateTime.UtcNow < _raceControlExitDeadline;

        public OverlayStyleState GetStyleState()
            => _handle == IntPtr.Zero ? new OverlayStyleState() : OverlayWindowInterop.ReadStyleState(_handle);

        public bool BeginLayoutEdit()
        {
            if (_layoutEditing) return true;
            if (_lastGameWindow == null || !_lastGameWindow.HasValidClientRect) return false;
            _layoutEditing = true;
            SetEditMode(true);
            if (_displayMode == DisplayMode.Waiting)
            {
                HideGameplayWindows();
                ShowWaitingSurface(_lastGameWindow, true);
            }
            else
            {
                _waitingWindow.HideOverlay();
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
            SetEditMode(false);
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
            _layoutProfile = new OverlayLayoutProfile();
            _layoutStore.Reset();
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
            _lastGameWindow = gameWindow ?? throw new ArgumentNullException(nameof(gameWindow));
            _displayMode = DisplayMode.Gameplay;
            if (_layoutEditing) return;
            _waitingWindow.HideOverlay();
            ShowGameplaySurfaces(gameWindow, false);
        }

        public void ShowWaitingAt(GameWindowSnapshot gameWindow, MultiplayerWaitingOverlayViewModel viewModel)
        {
            if (gameWindow == null) throw new ArgumentNullException(nameof(gameWindow));
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            _lastGameWindow = gameWindow;
            _lastWaitingViewModel = viewModel;
            _displayMode = DisplayMode.Waiting;

            string waitingKey = viewModel.SessionLabel + "\u001f" + viewModel.ParticipantCountText
                + "\u001f" + viewModel.RemainingLabel + "\u001f" + viewModel.RemainingValue;
            if (waitingKey != _lastWaitingKey)
            {
                _lastWaitingKey = waitingKey;
                _waitingView.DataContext = viewModel;
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
            _waitingWindow.HideOverlay();
        }

        private void ShowGameplaySurfaces(GameWindowSnapshot gameWindow, bool includeInactive)
        {
            OverlayComponentLayout defaults = OverlayComponentLayoutCalculator.Calculate(
                gameWindow.Width,
                gameWindow.Height,
                gameWindow.Dpi,
                _diagnostic,
                _viewModel.RaceControl.IsExpanded);

            if (_layoutProfile.IsEnabled(OverlayComponentKeys.TimingTower))
            {
                ShowMainAt(gameWindow, Resolve(OverlayComponentKeys.TimingTower, defaults.Timing, gameWindow));
            }
            else if (IsVisible)
            {
                Hide();
            }
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RelativeDrivers)
                && (includeInactive || _viewModel.Timing.IsBottomGapPanelVisible))
            {
                _relativeWindow.ShowAt(gameWindow, Resolve(OverlayComponentKeys.RelativeDrivers, defaults.Relative, gameWindow));
            }
            else
            {
                _relativeWindow.HideOverlay();
            }
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.LapTiming))
                _lapTimingWindow.ShowAt(gameWindow, Resolve(OverlayComponentKeys.LapTiming, defaults.LapTiming, gameWindow));
            else
                _lapTimingWindow.HideOverlay();
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.SessionInfo))
                _sessionWindow.ShowAt(gameWindow, Resolve(OverlayComponentKeys.SessionInfo, defaults.Session, gameWindow));
            else
                _sessionWindow.HideOverlay();
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.EventCard)
                && (includeInactive || IsEventCardPresentable))
            {
                _eventWindow.ShowAt(gameWindow, Resolve(OverlayComponentKeys.EventCard, defaults.EventCard, gameWindow));
            }
            else
            {
                _eventWindow.HideOverlay();
            }
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RaceControl)
                && (includeInactive || IsRaceControlPresentable))
            {
                _raceControlWindow.ShowAt(gameWindow, Resolve(OverlayComponentKeys.RaceControl, defaults.RaceControl, gameWindow));
            }
            else
            {
                _raceControlWindow.HideOverlay();
            }
        }

        private void ShowWaitingSurface(GameWindowSnapshot gameWindow, bool editing)
        {
            if (editing && _lastWaitingViewModel == null)
            {
                _lastWaitingViewModel = new MultiplayerWaitingOverlayViewModel();
                _waitingView.DataContext = _lastWaitingViewModel;
            }
            OverlayComponentLayout defaults = OverlayComponentLayoutCalculator.Calculate(
                gameWindow.Width,
                gameWindow.Height,
                gameWindow.Dpi,
                _diagnostic,
                false);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.Waiting))
                _waitingWindow.ShowAt(gameWindow, Resolve(OverlayComponentKeys.Waiting, defaults.Waiting, gameWindow));
            else
                _waitingWindow.HideOverlay();
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
                if (_layoutProfile.IsEnabled(OverlayComponentKeys.Waiting) && _waitingWindow.IsVisible)
                    Capture(OverlayComponentKeys.Waiting, _waitingWindow.ReadPhysicalBounds(), _lastGameWindow);
                return;
            }

            if (_layoutProfile.IsEnabled(OverlayComponentKeys.TimingTower) && IsVisible)
                Capture(OverlayComponentKeys.TimingTower, OverlayWindowInterop.ReadPhysicalBounds(_handle), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RelativeDrivers) && _relativeWindow.IsVisible)
                Capture(OverlayComponentKeys.RelativeDrivers, _relativeWindow.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.LapTiming) && _lapTimingWindow.IsVisible)
                Capture(OverlayComponentKeys.LapTiming, _lapTimingWindow.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.SessionInfo) && _sessionWindow.IsVisible)
                Capture(OverlayComponentKeys.SessionInfo, _sessionWindow.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.EventCard) && _eventWindow.IsVisible)
                Capture(OverlayComponentKeys.EventCard, _eventWindow.ReadPhysicalBounds(), _lastGameWindow);
            if (_layoutProfile.IsEnabled(OverlayComponentKeys.RaceControl) && _raceControlWindow.IsVisible)
                Capture(OverlayComponentKeys.RaceControl, _raceControlWindow.ReadPhysicalBounds(), _lastGameWindow);
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
            OverlayWindowInterop.SetEditMode(_handle, enabled);
            _relativeWindow.SetEditMode(enabled);
            _lapTimingWindow.SetEditMode(enabled);
            _sessionWindow.SetEditMode(enabled);
            _eventWindow.SetEditMode(enabled);
            _raceControlWindow.SetEditMode(enabled);
            _waitingWindow.SetEditMode(enabled);
        }

        private void EditDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
        {
            if (_layoutEditing && eventArgs.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void HideGameplayWindows()
        {
            if (IsVisible) Hide();
            _relativeWindow.HideOverlay();
            _lapTimingWindow.HideOverlay();
            _sessionWindow.HideOverlay();
            _eventWindow.HideOverlay();
            _raceControlWindow.HideOverlay();
        }

        private void InvalidateBounds()
        {
            _lastBoundsKey = string.Empty;
            _relativeWindow.InvalidateBounds();
            _lapTimingWindow.InvalidateBounds();
            _sessionWindow.InvalidateBounds();
            _eventWindow.InvalidateBounds();
            _raceControlWindow.InvalidateBounds();
            _waitingWindow.InvalidateBounds();
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
                    Stretch = Stretch.Fill,
                    Child = content,
                    IsHitTestVisible = false
                });
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
                Background = Brushes.Transparent,
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
