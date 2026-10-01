using System;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Overlay
{
    // Opt-in (--monitor-threaded-n): each visible N HUD is rasterized on its own thread and shown in
    // a native layered window. The WPF panel keeps its position, size and edit behaviour; only its
    // content is collapsed once the threaded window has presented its first frame. Layout editing,
    // preview and any failure fall back to the WPF view.
    public partial class OverlayWindow
    {
        private readonly bool _threadedNRequested;
        private bool _threadedNFailed;
        private readonly ThreadedAvanteHud?[] _threadedN = new ThreadedAvanteHud?[2];
        private readonly bool[] _threadedNPresenting = new bool[2];
        private readonly bool[] _threadedNIgnitionPending = new bool[2];
        private readonly ThreadedAvanteHud.Placement[] _threadedNPlacement =
            { ThreadedAvanteHud.Placement.Hidden, ThreadedAvanteHud.Placement.Hidden };
        private TelemetrySnapshot? _threadedNSession;
        public bool UsesThreadedN => _threadedNRequested;
        public event Action<string>? ThreadedNStatus;

        private AvanteClusterView? AvanteView(int slot) => slot == 0 ? _avanteView : _avanteExpandedView;
        // True while the threaded window, not the WPF view, shows this N HUD.
        private bool IsThreadedNPresenting(int panelIndex) => (panelIndex == 5 || panelIndex == 6) && _threadedNPresenting[panelIndex - 5];

        private void SyncThreadedN()
        {
            if (!_threadedNRequested || _threadedNFailed) return;
            bool allowed = !_layoutEditing && !_layoutPreview && !_closing && _retainedNWorker == null && !_retainedNActive;
            for (int slot = 0; slot < 2; slot++)
            {
                AuxiliaryOverlayWindow? panel = _drivingWindows[slot + 5];
                if (!allowed || panel == null || AvanteView(slot) == null) { ReleaseThreadedN(slot); continue; }
                bool shown = panel.IsVisible && panel.Opacity > 0 && panel.Handle != IntPtr.Zero;
                double opacity = _layoutProfile.GetOpacity(panel.ComponentKey);
                if (!shown || opacity <= 0)
                {
                    SetThreadedNPlacement(slot, ThreadedAvanteHud.Placement.Hidden);
                    continue;
                }
                if (_threadedN[slot] == null) CreateThreadedN(slot);
                OverlayBounds bounds = panel.ReadPhysicalBounds();
                SetThreadedNPlacement(slot, new ThreadedAvanteHud.Placement(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), bounds.Width > 0 && bounds.Height > 0));
            }
        }

        private void CreateThreadedN(int slot)
        {
            AvanteClusterView view = AvanteView(slot)!;
            ThreadedAvanteHud? hud = null;
            hud = new ThreadedAvanteHud(slot == 1, GetDrivingHudSettings(), _threadedNSession,
                error => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (_closing || !ReferenceEquals(_threadedN[slot], hud)) return;
                    ThreadedNStatus?.Invoke("failed slot=" + slot + " " + error.GetType().Name + " " + error.Message);
                    _threadedNFailed = true;
                    ReleaseThreadedN();
                })),
                () => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (_closing || !ReferenceEquals(_threadedN[slot], hud)) return;
                    _threadedNPresenting[slot] = true;
                    _drivingWindows[slot + 5]?.SetExternalPresentation(true);
                    ThreadedNStatus?.Invoke("active slot=" + slot);
                })));
            _threadedN[slot] = hud;
            _threadedNPlacement[slot] = ThreadedAvanteHud.Placement.Hidden;
            // The intro plays once per game window, whichever view shows it.
            if (view.HasStartedGameplayIgnition) { hud.MarkIgnitionShown(); _threadedNIgnitionPending[slot] = false; }
            else _threadedNIgnitionPending[slot] = true;
            hud.PublishSample(_drivingHistory.Current);
            ThreadedNStatus?.Invoke("created slot=" + slot);
        }

        private void SetThreadedNPlacement(int slot, ThreadedAvanteHud.Placement placement)
        {
            var hud = _threadedN[slot];
            if (hud == null || placement.Equals(_threadedNPlacement[slot])) return;
            _threadedNPlacement[slot] = placement;
            hud.SetPlacement(placement);
        }

        private void ReleaseThreadedN()
        {
            for (int slot = 0; slot < 2; slot++) ReleaseThreadedN(slot);
        }

        private void ReleaseThreadedN(int slot)
        {
            var hud = _threadedN[slot];
            if (hud == null) return;
            _threadedN[slot] = null;
            bool presented = _threadedNPresenting[slot];
            _threadedNPresenting[slot] = false;
            _threadedNPlacement[slot] = ThreadedAvanteHud.Placement.Hidden;
            ThreadedNStatus?.Invoke("released slot=" + slot + " frames=" + hud.Frames + " skipped=" + hud.SkippedFrames);
            hud.Dispose();
            _drivingWindows[slot + 5]?.SetExternalPresentation(false);
            // The WPF view resumes with current values; it must not replay an intro the thread showed.
            if (AvanteView(slot) is { } view)
            {
                if (presented && !_threadedNIgnitionPending[slot]) view.MarkGameplayIgnitionShown();
                view.SetSession(_threadedNSession);
                view.SetSample(_drivingHistory.Current);
            }
        }

        private void PublishThreadedNSample(DrivingTelemetrySample? sample)
        {
            foreach (var hud in _threadedN) hud?.PublishSample(sample);
        }

        private void PublishThreadedNSession(TelemetrySnapshot snapshot)
        {
            _threadedNSession = snapshot;
            foreach (var hud in _threadedN) hud?.SetSession(snapshot);
        }

        private void PublishThreadedNSettings()
        {
            foreach (var hud in _threadedN) hud?.ApplySettings(GetDrivingHudSettings());
        }

        private void RearmThreadedNIgnition()
        {
            for (int slot = 0; slot < 2; slot++)
            {
                var hud = _threadedN[slot];
                if (hud == null) continue;
                hud.RearmIgnition();
                _threadedNIgnitionPending[slot] = true;
            }
        }

        private void BeginThreadedNIgnition()
        {
            for (int slot = 0; slot < 2; slot++)
            {
                var hud = _threadedN[slot];
                if (hud == null || !_threadedNIgnitionPending[slot]) continue;
                hud.BeginIgnition();
                _threadedNIgnitionPending[slot] = false;
            }
        }
    }
}
