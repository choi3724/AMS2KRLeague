using System;
using System.Collections.Generic;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Overlay
{
    public partial class OverlayWindow
    {
        private bool _retainedNRequested;
        private bool _retainedNActive;
        private bool _retainedNFailed;
        private int _retainedNGeneration;
        private byte[][]? _retainedNImages;
        private RetainedNPresenter? _retainedNWorker;
        private RetainedNPresenter.Slot[] _retainedNSlots = Array.Empty<RetainedNPresenter.Slot>();
        private CompositionHudFrame? _retainedNFrame;
        private TelemetrySnapshot? _retainedNSession;
        public event Action<string>? RetainedNStatus;

        private void UpdateRetainedNSession(TelemetrySnapshot snapshot)
        {
            _retainedNSession = snapshot;
            if (_retainedNWorker == null) return;
            _retainedNFrame = new CompositionHudFrame(GetDrivingHudSettings(), snapshot);
            PublishRetainedN(_drivingHistory.Current);
        }

        private void RefreshRetainedNSettings()
        {
            if (_retainedNWorker == null) return;
            _retainedNFrame = new CompositionHudFrame(GetDrivingHudSettings(), _retainedNSession);
            PublishRetainedN(_drivingHistory.Current);
        }

        private void PublishRetainedN(DrivingTelemetrySample? sample)
        {
            if (_retainedNWorker is { } worker && _retainedNFrame is { } frame)
                worker.Publish(_retainedNSlots, frame, sample);
        }

        private void SyncRetainedN()
        {
            if (!_retainedNRequested || _retainedNFailed || _layoutEditing || _layoutPreview || _closing)
                return;
            var slots = new List<RetainedNPresenter.Slot>(2);
            for (int i = 5; i <= 6; i++)
            {
                var panel = _drivingWindows[i];
                if (panel == null || !panel.IsVisible || panel.Handle == IntPtr.Zero) continue;
                var bounds = panel.ReadPhysicalBounds();
                if (bounds.Width > 0 && bounds.Height > 0)
                    slots.Add(new RetainedNPresenter.Slot(panel.Handle, bounds.Width, bounds.Height, i == 6,
                        (float)_layoutProfile.GetOpacity(panel.ComponentKey)));
            }
            if (slots.Count == 0) { ReleaseRetainedN(); return; }
            if (_retainedNWorker == null)
            {
                _retainedNImages ??= AvanteClusterView.PrepareNativeImages();
                int generation = ++_retainedNGeneration;
                _retainedNWorker = new RetainedNPresenter(_retainedNImages, GetDrivingHudSettings(), error =>
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        if (_closing || generation != _retainedNGeneration) return;
                        if (error != null)
                        {
                            RetainedNStatus?.Invoke("failed=" + error.GetType().Name + " " + error.Message);
                            _retainedNFailed = true;
                            ReleaseRetainedN();
                            RefreshDrivingViews();
                            return;
                        }
                        _retainedNActive = true;
                        _drivingWindows[5]?.SetRetainedPresentation(true);
                        _drivingWindows[6]?.SetRetainedPresentation(true);
                        // Any WPF motion subscriptions are torn down by IsVisibleChanged.
                        RetainedNStatus?.Invoke("active hwnds=" + _retainedNSlots.Length);
                    })), message => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (!_closing && generation == _retainedNGeneration) RetainedNStatus?.Invoke(message);
                    })));
                RetainedNStatus?.Invoke("created");
            }
            bool changed = slots.Count != _retainedNSlots.Length;
            if (!changed) for (int i = 0; i < slots.Count; i++)
                if (slots[i].Hwnd != _retainedNSlots[i].Hwnd || slots[i].Width != _retainedNSlots[i].Width
                    || slots[i].Height != _retainedNSlots[i].Height || slots[i].Expanded != _retainedNSlots[i].Expanded
                    || slots[i].Opacity != _retainedNSlots[i].Opacity)
                { changed = true; break; }
            if (!changed) return;
            _retainedNSlots = slots.ToArray();
            _retainedNFrame = new CompositionHudFrame(GetDrivingHudSettings(), _retainedNSession);
            PublishRetainedN(_drivingHistory.Current);
        }

        private void ReleaseRetainedN()
        {
            _retainedNGeneration++;
            var worker = _retainedNWorker;
            _retainedNWorker = null;
            _retainedNSlots = Array.Empty<RetainedNPresenter.Slot>();
            _retainedNActive = false;
            _drivingWindows[5]?.SetRetainedPresentation(false);
            _drivingWindows[6]?.SetRetainedPresentation(false);
            if (worker == null) return;
            RetainedNStatus?.Invoke("released commits=" + worker.Commits + " draws=" + worker.Draws);
            worker.Dispose();
        }
    }
}
