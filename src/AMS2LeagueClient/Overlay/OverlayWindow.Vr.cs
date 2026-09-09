using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Vr;

namespace AMS2LeagueClient.Overlay
{
    public partial class OverlayWindow
    {
        private VrOverlayController? _vr;
        private readonly DispatcherTimer _vrTimer = new DispatcherTimer { Interval = VrOverlayController.FrameInterval };
        private bool _vrSceneActive, _desktopForeground = true;
        private uint _vrExpectedProcessId;
        private RenderTargetBitmap? _vrBitmap;
        private byte[]? _vrPixels;

        public VrHudSettings GetVrHudSettings() => (_layoutProfile.VrHud ?? new VrHudSettings()).Normalize();

        public void StartVr(Action<string> report)
        {
            if (_vr != null) return;
            _vr = new VrOverlayController(new SteamVrOverlayRuntime(), CaptureVrFrame, report);
            _vr.Configure(GetVrHudSettings());
            _vrTimer.Tick += (_, __) => _vr?.Tick(DateTimeOffset.UtcNow,
                _lastGameWindow != null && (_layoutPreview || _vrSceneActive),
                _layoutPreview ? 0 : _vrExpectedProcessId);
            _vrTimer.Start();
            ApplyOutputVisibility();
        }

        public void StopVr()
        {
            _vrTimer.Stop();
            _vr?.Dispose();
            _vr = null;
        }

        public void SaveVrHudSettings(VrHudSettings settings)
        {
            VrHudSettings previous = _layoutProfile.VrHud;
            _layoutProfile.VrHud = settings.Normalize();
            try { _layoutStore.Save(_layoutProfile); }
            catch { _layoutProfile.VrHud = previous; throw; }
            _vr?.Configure(GetVrHudSettings());
            ApplyOutputVisibility();
        }
        public void RecenterVr() => _vr?.Recenter();
        public bool IsVrSceneActive(int processId) => processId > 0 && _vr?.Connected == true
            && _vr.Output != OverlayOutputMode.Monitor && _vr.SceneProcessId == (uint)processId;

        public void SetGameOutputState(bool vrSceneActive, bool desktopForeground)
        {
            _vrSceneActive = vrSceneActive;
            _desktopForeground = desktopForeground;
            ApplyOutputVisibility();
        }

        public GameWindowSnapshot? ResolveOutputWindow(GameWindowSnapshot? window, bool vrSceneActive, int processId = 0)
        {
            _vrExpectedProcessId = processId > 0 ? (uint)processId : 0;
            SetGameOutputState(vrSceneActive, window?.IsForeground == true && !window.IsMinimized);
            if (!vrSceneActive) return window;
            GameWindowSnapshot area = window?.HasValidClientRect == true
                ? window : OverlayWindowInterop.GetLayoutPreviewArea(_handle);
            // Only a confirmed AMS2 VR scene bypasses desktop focus/minimization.
            return new GameWindowSnapshot(area.Handle, area.Left, area.Top, area.Width, area.Height,
                area.Dpi, true, false, area.MonitorHandle);
        }

        private IEnumerable<Window> HudWindows()
        {
            yield return this;
            yield return _relativeWindow; yield return _lapTimingWindow; yield return _sessionWindow;
            yield return _eventWindow; yield return _raceControlWindow; yield return _waitingWindow;
            foreach (var panel in _drivingWindows) yield return panel;
        }

        private void ApplyOutputVisibility()
        {
            bool monitor = _layoutEditing || (GetVrHudSettings().Output != OverlayOutputMode.Vr && _desktopForeground);
            // Keep WPF surfaces arranged for VR rendering; transparent native windows stay click-through.
            foreach (Window window in HudWindows())
                window.Opacity = monitor ? 1 : 0;
        }

        public VrFrame? CaptureVrFrame()
        {
            if (_lastGameWindow == null || !_lastGameWindow.HasValidClientRect) return null;
            int sourceWidth = _lastGameWindow.Width, sourceHeight = _lastGameWindow.Height;
            double scale = Math.Min(1, VrFrame.MaximumDimension / (double)Math.Max(sourceWidth, sourceHeight));
            int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            var drawing = new DrawingVisual();
            bool hasContent = false;
            using (DrawingContext context = drawing.RenderOpen())
            {
                context.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
                foreach (Window window in HudWindows())
                {
                    if (!window.IsVisible || !(window.Content is Grid root) || root.Children.Count == 0) continue;
                    // The first child is the HUD, the second is desktop edit chrome.
                    var content = (FrameworkElement)root.Children[0];
                    if (content.ActualWidth <= 0 || content.ActualHeight <= 0 || window.ActualWidth <= 0) continue;
                    OverlayBounds bounds = OverlayWindowInterop.ReadPhysicalBounds(new WindowInteropHelper(window).Handle);
                    Point offset = content.TranslatePoint(new Point(), root);
                    double xScale = bounds.Width / window.ActualWidth;
                    double yScale = bounds.Height / window.ActualHeight;
                    Rect target = new Rect(
                        (bounds.X - _lastGameWindow.Left + offset.X * xScale) * scale,
                        (bounds.Y - _lastGameWindow.Top + offset.Y * yScale) * scale,
                        content.ActualWidth * xScale * scale, content.ActualHeight * yScale * scale);
                    context.DrawRectangle(new VisualBrush(content)
                    {
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, 0, content.ActualWidth, content.ActualHeight),
                        Stretch = Stretch.Fill
                    }, null, target);
                    hasContent = true;
                }
                context.Pop();
            }
            if (!hasContent) return null;
            if (_vrBitmap == null || _vrBitmap.PixelWidth != width || _vrBitmap.PixelHeight != height)
            {
                _vrBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                _vrPixels = new byte[width * height * 4];
            }
            _vrBitmap.Clear();
            _vrBitmap.Render(drawing);
            _vrBitmap.CopyPixels(_vrPixels!, width * 4, 0);
            ConvertBgraToRgba(_vrPixels!);
            return new VrFrame(width, height, _vrPixels!);
        }

        public static void ConvertBgraToRgba(byte[] pixels)
        {
            if (pixels == null || pixels.Length % 4 != 0) throw new ArgumentException("Invalid pixel buffer.");
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte blue = pixels[i]; pixels[i] = pixels[i + 2]; pixels[i + 2] = blue;
            }
        }
    }
}
