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
        private readonly AuxiliaryOverlayWindow _sessionWindow;
        private readonly AuxiliaryOverlayWindow _eventWindow;
        private readonly AuxiliaryOverlayWindow _raceControlWindow;
        private IntPtr _handle;
        private OverlayShellViewModel _viewModel = new OverlayShellViewModel();
        private string _lastBoundsKey = string.Empty;
        private string _lastSessionKey = string.Empty;
        private string _lastEventKey = string.Empty;
        private string _lastRaceControlKey = string.Empty;

        public OverlayWindow(bool diagnostic)
        {
            _diagnostic = diagnostic;
            InitializeComponent();
            _sessionWindow = new AuxiliaryOverlayWindow("AMS2 Session Info", _sessionView);
            _eventWindow = new AuxiliaryOverlayWindow("AMS2 Event Card", _eventView);
            _raceControlWindow = new AuxiliaryOverlayWindow("AMS2 Race Control", _raceControlView);
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
            double scale = gameWindow.Dpi / 96.0;
            int sideInset = (int)Math.Round(gameWindow.Width * 0.07);
            int topInset = (int)Math.Round(gameWindow.Height * 0.07);
            int bottomInset = (int)Math.Round(gameWindow.Height * 0.09);
            int towerLeftInset = Math.Max(8, (int)Math.Round(gameWindow.Width * 0.004));
            int towerTopInset = Math.Max(8, (int)Math.Round(gameWindow.Height * 0.008));
            int timingWidth = Scale(LeftTowerLayoutMetrics.Width, scale);
            int timingHeight = Math.Min(
                Scale(_diagnostic ? LeftTowerLayoutMetrics.DiagnosticHeight : LeftTowerLayoutMetrics.DesiredHeight, scale),
                Math.Max(1, gameWindow.Height - (towerTopInset * 2)));
            int sessionWidth = Scale(280, scale);
            int sessionHeight = Scale(150, scale);
            int eventWidth = Scale(650, scale);
            int eventHeight = Scale(105, scale);
            int raceWidth = Scale(_viewModel.RaceControl.IsExpanded ? 520 : 330, scale);
            int raceHeight = Scale(_viewModel.RaceControl.IsExpanded ? 165 : 55, scale);

            EnsureShown(gameWindow.Left + towerLeftInset, gameWindow.Top + towerTopInset, timingWidth, timingHeight);
            _sessionWindow.ShowAt(
                gameWindow.Left + towerLeftInset + timingWidth + Scale(LeftTowerLayoutMetrics.SessionGap, scale),
                gameWindow.Top + towerTopInset,
                sessionWidth,
                sessionHeight);
            if (_viewModel.EventCard.IsVisible)
            {
                _eventWindow.ShowAt(gameWindow.Left + (gameWindow.Width - eventWidth) / 2, gameWindow.Top + gameWindow.Height - bottomInset - eventHeight, eventWidth, eventHeight);
            }
            if (_viewModel.RaceControl.IsVisible)
            {
                _raceControlWindow.ShowAt(gameWindow.Left + (gameWindow.Width - raceWidth) / 2, gameWindow.Top + topInset, raceWidth, raceHeight);
            }
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
