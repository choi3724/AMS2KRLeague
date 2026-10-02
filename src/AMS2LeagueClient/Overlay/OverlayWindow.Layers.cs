using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Overlay
{
    public partial class OverlayWindow
    {
        private string _lastLayerSignature = string.Empty;

        public IReadOnlyList<string> GetLayerOrder() => _layoutProfile.LayerOrder.ToArray();
        public IReadOnlyList<string> GetVisibleLayerComponents()
            => _layoutProfile.LayerOrder.Where(component => LayerWindow(component)?.IsVisible == true).ToArray();

        /// <summary>Changes visible stacking without writing the layout file.</summary>
        public void PreviewLayerOrder(IReadOnlyList<string> order)
        {
            _layoutProfile.LayerOrder = OverlayLayerOrder.Normalize(order);
            ApplyLayerOrder(true);
        }

        /// <summary>Commits appearance and stacking together after a component dialog is saved.</summary>
        public void SaveComponentSettings(DrivingHudSettings settings, IReadOnlyList<string> order)
        {
            DrivingHudSettings previousSettings = _layoutProfile.DrivingHud;
            List<string> previousOrder = _layoutProfile.LayerOrder;
            _layoutProfile.DrivingHud = settings.Normalize();
            _layoutProfile.LayerOrder = OverlayLayerOrder.Normalize(order);
            try { _layoutStore.Save(_layoutProfile); }
            catch
            {
                _layoutProfile.DrivingHud = previousSettings;
                _layoutProfile.LayerOrder = previousOrder;
                throw;
            }
            ApplyDrivingAppearance();
            ApplyComponentOpacities();
            ApplyLayerOrder(true);
        }

        private Window? LayerWindow(string component) => component switch
        {
            OverlayComponentKeys.TimingTower => this,
            OverlayComponentKeys.RelativeDrivers => _relativeWindow,
            OverlayComponentKeys.LapTiming => _lapTimingWindow,
            OverlayComponentKeys.SessionInfo => _sessionWindow,
            OverlayComponentKeys.EventCard => _eventWindow,
            OverlayComponentKeys.RaceControl => _raceControlWindow,
            OverlayComponentKeys.Waiting => _waitingWindow,
            OverlayComponentKeys.PedalTelemetry => _drivingWindows[0],
            OverlayComponentKeys.PedalGauge => _drivingWindows[1],
            OverlayComponentKeys.Speed => _drivingWindows[2],
            OverlayComponentKeys.Gear => _drivingWindows[3],
            OverlayComponentKeys.DrivingDashboard => _drivingWindows[4],
            OverlayComponentKeys.AvanteCluster => _drivingWindows[5],
            OverlayComponentKeys.AvanteClusterExpanded => _drivingWindows[6],
            _ => null
        };

        private IEnumerable<Window> HudWindowsInLayerOrder()
        {
            foreach (string component in _layoutProfile.LayerOrder)
                if (LayerWindow(component) is Window window) yield return window;
        }

        private IntPtr LayerHandle(string component, Window window)
        {
            int slot = component == OverlayComponentKeys.AvanteCluster ? 0
                : component == OverlayComponentKeys.AvanteClusterExpanded ? 1 : -1;
            if (slot >= 0 && _threadedNPresenting[slot] && _threadedN[slot]?.SurfaceHandle is IntPtr native && native != IntPtr.Zero)
                return native;
            return new WindowInteropHelper(window).Handle;
        }

        private void ApplyLayerOrder(bool force = false)
        {
            if (_closing) return;
            var visible = new List<(string Component, IntPtr Handle)>();
            foreach (string component in _layoutProfile.LayerOrder)
            {
                Window? window = LayerWindow(component);
                if (window?.IsVisible != true) continue;
                IntPtr handle = LayerHandle(component, window);
                if (handle != IntPtr.Zero) visible.Add((component, handle));
            }
            string signature = string.Join("|", visible.Select(item => item.Component + ":" + item.Handle));
            if (!force && signature == _lastLayerSignature) return;
            bool complete = true;
            // Windows orders topmost HWNDs in the order these calls succeed: last is in front.
            foreach (var item in visible)
                complete &= OverlayWindowInterop.PlaceTopmost(item.Handle);
            if (complete) _lastLayerSignature = signature;
        }
    }
}
