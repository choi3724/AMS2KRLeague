using System;
using System.Windows;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Overlay
{
    public partial class OverlayWindow : Window
    {
        private readonly bool _diagnostic;
        private readonly SessionInfoView _sessionView = new SessionInfoView();
        private readonly EventCardView _eventView = new EventCardView();
        private readonly RaceControlView _raceControlView = new RaceControlView();
        private readonly MultiplayerWaitingOverlayView _waitingView = new MultiplayerWaitingOverlayView();
        private readonly AuxiliaryOverlayWindow _sessionWindow;
        private readonly AuxiliaryOverlayWindow _eventWindow;
        private readonly AuxiliaryOverlayWindow _raceControlWindow;
        private readonly AuxiliaryOverlayWindow _waitingWindow;
        private IntPtr _handle;
        private OverlayShellViewModel _viewModel = new OverlayShellViewModel();
        private string _lastBoundsKey = string.Empty;
        private string _lastSessionKey = string.Empty;
        private string _lastEventKey = string.Empty;
        private string _lastRaceControlKey = string.Empty;
        private string _lastWaitingKey = string.Empty;

        public OverlayWindow(bool diagnostic)
        {
            _diagnostic = diagnostic;
            InitializeComponent();
            _sessionWindow = new AuxiliaryOverlayWindow("AMS2 Session Info", _sessionView);
            _eventWindow = new AuxiliaryOverlayWindow("AMS2 Event Card", _eventView);
            _raceControlWindow = new AuxiliaryOverlayWindow("AMS2 Race Control", _raceControlView);
            _waitingWindow = new AuxiliaryOverlayWindow("AMS2 Multiplayer Waiting", _waitingView);
        }

        protected override void OnSourceInitialized(EventArgs eventArgs)
        {
            base.OnSourceInitialized(eventArgs);
            _handle = new WindowInteropHelper(this).Handle;
            OverlayWindowInterop.Configure(_handle);
        }

        protected override void OnClosed(EventArgs eventArgs)
        {
            _sessionWindow.Close();
            _eventWindow.Close();
            _raceControlWindow.Close();
            _waitingWindow.Close();
            base.OnClosed(eventArgs);
        }

        public void SetViewModel(OverlayShellViewModel viewModel, bool animate = true)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            TimingHud.SetViewModel(viewModel.Timing);

            string sessionKey = viewModel.Session.PrimaryLabel + "\u001f" + viewModel.Session.PrimaryValue + "\u001f"
                + viewModel.Session.PositionValue + "\u001f" + viewModel.Session.LapValue;
            if (sessionKey != _lastSessionKey)
            {
                _lastSessionKey = sessionKey;
                _sessionView.DataContext = viewModel.Session;
            }

            string eventKey = viewModel.EventCard.EventId + "\u001f" + viewModel.EventCard.IsVisible + "\u001f" + viewModel.EventCard.SecondaryText;
            if (eventKey != _lastEventKey)
            {
                _lastEventKey = eventKey;
                _eventView.SetViewModel(viewModel.EventCard, animate);
            }

            string raceControlKey = viewModel.RaceControl.EventId + "\u001f" + viewModel.RaceControl.IsVisible + "\u001f"
                + viewModel.RaceControl.IsExpanded + "\u001f" + viewModel.RaceControl.Message + "\u001f" + viewModel.RaceControl.StateLabel;
            if (raceControlKey != _lastRaceControlKey)
            {
                _lastRaceControlKey = raceControlKey;
                _raceControlView.SetViewModel(viewModel.RaceControl, animate);
            }
            if (!viewModel.EventCard.IsVisible) _eventWindow.HideOverlay();
            if (!viewModel.RaceControl.IsVisible) _raceControlWindow.HideOverlay();
        }

        public OverlayStyleState GetStyleState()
            => _handle == IntPtr.Zero ? new OverlayStyleState() : OverlayWindowInterop.ReadStyleState(_handle);

        public void ShowAt(GameWindowSnapshot gameWindow)
        {
            _waitingWindow.HideOverlay();
            OverlayComponentLayout layout = OverlayComponentLayoutCalculator.Calculate(
                gameWindow.Width,
                gameWindow.Height,
                gameWindow.Dpi,
                _diagnostic,
                _viewModel.RaceControl.IsExpanded);

            EnsureShown(
                gameWindow.Left + layout.Timing.X,
                gameWindow.Top + layout.Timing.Y,
                layout.Timing.Width,
                layout.Timing.Height);
            _sessionWindow.ShowAt(
                gameWindow.Left + layout.Session.X,
                gameWindow.Top + layout.Session.Y,
                layout.Session.Width,
                layout.Session.Height);
            if (_viewModel.EventCard.IsVisible)
            {
                _eventWindow.ShowAt(
                    gameWindow.Left + layout.EventCard.X,
                    gameWindow.Top + layout.EventCard.Y,
                    layout.EventCard.Width,
                    layout.EventCard.Height);
            }
            if (_viewModel.RaceControl.IsVisible)
            {
                _raceControlWindow.ShowAt(
                    gameWindow.Left + layout.RaceControl.X,
                    gameWindow.Top + layout.RaceControl.Y,
                    layout.RaceControl.Width,
                    layout.RaceControl.Height);
            }
        }

        public void ShowWaitingAt(GameWindowSnapshot gameWindow, MultiplayerWaitingOverlayViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            if (IsVisible) Hide();
            _sessionWindow.HideOverlay();
            _eventWindow.HideOverlay();
            _raceControlWindow.HideOverlay();

            string waitingKey = viewModel.SessionLabel + "\u001f" + viewModel.ParticipantCountText
                + "\u001f" + viewModel.RemainingLabel + "\u001f" + viewModel.RemainingValue;
            if (waitingKey != _lastWaitingKey)
            {
                _lastWaitingKey = waitingKey;
                _waitingView.DataContext = viewModel;
            }

            OverlayComponentLayout layout = OverlayComponentLayoutCalculator.Calculate(
                gameWindow.Width,
                gameWindow.Height,
                gameWindow.Dpi,
                _diagnostic,
                false);
            _waitingWindow.ShowAt(
                gameWindow.Left + layout.Waiting.X,
                gameWindow.Top + layout.Waiting.Y,
                layout.Waiting.Width,
                layout.Waiting.Height);
        }

        public void ShowDemoAt(int left, int top, uint dpi)
        {
            var demoWindow = new GameWindowSnapshot(IntPtr.Zero, left, top, Scale(1920, dpi / 96.0), Scale(1080, dpi / 96.0), dpi, true, false, 0);
            ShowAt(demoWindow);
        }

        public void HideOverlay()
        {
            if (IsVisible) Hide();
            _sessionWindow.HideOverlay();
            _eventWindow.HideOverlay();
            _raceControlWindow.HideOverlay();
            _waitingWindow.HideOverlay();
        }

        private void EnsureShown(int left, int top, int width, int height)
        {
            bool wasVisible = IsVisible;
            if (!IsVisible)
            {
                Show();
                _handle = new WindowInteropHelper(this).Handle;
            }

            string boundsKey = left + "," + top + "," + width + "x" + height;
            if (boundsKey != _lastBoundsKey)
            {
                _lastBoundsKey = boundsKey;
                OverlayWindowInterop.SetPhysicalBounds(_handle, left, top, width, height);
            }
            if (!wasVisible) OverlayWindowInterop.ShowWithoutActivation(_handle);
        }

        private static int Scale(int logicalPixels, double scale)
            => Math.Max(1, (int)Math.Round(logicalPixels * scale));
    }

    internal sealed class AuxiliaryOverlayWindow : Window
    {
        private IntPtr _handle;
        private string _lastBoundsKey = string.Empty;

        public AuxiliaryOverlayWindow(string title, FrameworkElement content)
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            Content = content;
            SourceInitialized += (sender, args) =>
            {
                _handle = new WindowInteropHelper(this).Handle;
                OverlayWindowInterop.Configure(_handle);
            };
        }

        public void ShowAt(int left, int top, int width, int height)
        {
            bool wasVisible = IsVisible;
            if (!IsVisible)
            {
                Show();
                _handle = new WindowInteropHelper(this).Handle;
            }

            string boundsKey = left + "," + top + "," + width + "x" + height;
            if (boundsKey != _lastBoundsKey)
            {
                _lastBoundsKey = boundsKey;
                OverlayWindowInterop.SetPhysicalBounds(_handle, left, top, width, height);
            }
            if (!wasVisible) OverlayWindowInterop.ShowWithoutActivation(_handle);
        }

        public void HideOverlay()
        {
            if (IsVisible) Hide();
        }
    }
}
