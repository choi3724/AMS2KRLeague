using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Presentation
{
    // Opt-in (--monitor-threaded-n). One N HUD per thread. The unchanged WPF view lives in a hidden
    // HwndSource on its own Dispatcher and is rasterized there by RenderTargetBitmap, which runs
    // same-thread composition synchronously: no shared WPF render thread and no GPU work. Pixels
    // are shown in a native layered click-through window with UpdateLayeredWindow, so a slow HUD
    // never holds back another HUD.
    internal sealed class ThreadedAvanteHud : IDisposable
    {
        internal const double CompactDesignWidth = 569, CompactDesignHeight = 545;
        internal const double ExpandedDesignWidth = 820, ExpandedDesignHeight = 436;

        internal readonly struct Placement : IEquatable<Placement>
        {
            internal readonly int X, Y, Width, Height;
            internal readonly byte Alpha;
            internal readonly bool Visible;
            internal readonly Rect? Viewport; // design area shown; null = full ignition canvas
            internal Placement(int x, int y, int width, int height, byte alpha, bool visible, Rect? viewport = null)
            { X = x; Y = y; Width = width; Height = height; Alpha = alpha; Visible = visible; Viewport = viewport; }
            internal static readonly Placement Hidden = new Placement(0, 0, 0, 0, 0, false);
            public bool Equals(Placement other) => X == other.X && Y == other.Y && Width == other.Width
                && Height == other.Height && Alpha == other.Alpha && Visible == other.Visible && Viewport == other.Viewport;
            public override bool Equals(object? obj) => obj is Placement other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height, Alpha, Visible);
        }

        private readonly bool _expanded;
        private readonly Action<Exception> _failed;
        private readonly Action _firstFrame;
        private readonly Thread _thread;
        private Dispatcher? _dispatcher;
        private readonly object _startGate = new object();
        private readonly System.Collections.Generic.List<(Action Action, DispatcherPriority Priority)> _early =
            new System.Collections.Generic.List<(Action, DispatcherPriority)>();
        private int _disposed, _failedOnce;

        // Producer -> worker: the newest sample replaces an unapplied one.
        private DrivingTelemetrySample? _pendingSample;
        private int _sampleQueued;

        // Worker-only state.
        private HwndSource? _host;
        private Border? _root;
        private AvanteClusterView? _view;
        // Mutable WPF transforms belong to the HUD Dispatcher, not the constructing UI thread.
        private ScaleTransform _scale = null!;
        private LayeredSurface? _surface;
        private RenderTargetBitmap? _bitmap;
        private Placement _placement = Placement.Hidden;
        private bool _clockSubscribed, _dirty = true, _wasAnimating, _presented;
        private DispatcherTimer? _fallbackTimer;
        private long _lastCaptureTicks;

        internal long Frames => Interlocked.Read(ref _frames);
        internal long SkippedFrames => Interlocked.Read(ref _skipped);
        private long _frames, _skipped;

        internal ThreadedAvanteHud(bool expanded, DrivingHudSettings settings, TelemetrySnapshot? session,
            Action<Exception> failed, Action firstFrame)
        {
            _expanded = expanded; _failed = failed; _firstFrame = firstFrame;
            _thread = new Thread(() => Run(settings, session))
            {
                IsBackground = true,
                Name = expanded ? "N HUD (expanded)" : "N HUD (compact)",
                Priority = ThreadPriority.AboveNormal
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        internal void SetPlacement(Placement placement) => Post(() => ApplyPlacement(placement));
        internal void ApplySettings(DrivingHudSettings settings) => Post(() => { _view!.ApplySettings(settings); _dirty = true; });
        internal void SetSession(TelemetrySnapshot? session) => Post(() => { _view!.SetSession(session); _dirty = true; });
        internal void RearmIgnition() => Post(() => { _view!.RearmGameplayIgnition(); _dirty = true; });
        internal void BeginIgnition() => Post(() => { _view!.BeginGameplayIgnition(); _dirty = true; });
        internal void MarkIgnitionShown() => Post(() => _view!.MarkGameplayIgnitionShown());

        internal void PublishSample(DrivingTelemetrySample? sample)
        {
            Volatile.Write(ref _pendingSample, sample);
            if (Interlocked.Exchange(ref _sampleQueued, 1) == 0)
                Post(() =>
                {
                    Interlocked.Exchange(ref _sampleQueued, 0);
                    _view!.SetSample(Volatile.Read(ref _pendingSample));
                    _dirty = true;
                }, DispatcherPriority.Render);
        }

        private void Post(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            Dispatcher dispatcher;
            lock (_startGate)
            {
                // Inputs sent while the thread is still starting keep their order.
                if (_dispatcher == null) { _early.Add((action, priority)); return; }
                dispatcher = _dispatcher;
            }
            if (Volatile.Read(ref _disposed) != 0 || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(priority, action);
        }

        private void Run(DrivingHudSettings settings, TelemetrySnapshot? session)
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (sender, args) => { args.Handled = true; Fail(args.Exception); };
            try
            {
                _scale = new ScaleTransform(1, 1);
                _view = new AvanteClusterView(_expanded) { LayoutTransform = _scale };
                _view.ApplySettings(settings);
                if (session != null) _view.SetSession(session);
                _root = new Border { Child = _view };
                var parameters = new HwndSourceParameters(_thread.Name ?? "N HUD")
                {
                    WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
                    ExtendedWindowStyle = 0x00000080 | 0x08000000, // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                    Width = 1, Height = 1
                };
                _host = new HwndSource(parameters) { SizeToContent = SizeToContent.WidthAndHeight };
                _host.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
                _host.RootVisual = _root;
                _surface = new LayeredSurface();
            }
            catch (Exception exception)
            {
                Fail(exception);
                Cleanup();
                return;
            }
            lock (_startGate)
            {
                _dispatcher = dispatcher;
                foreach (var (action, priority) in _early) dispatcher.BeginInvoke(priority, action);
                _early.Clear();
            }
            if (Volatile.Read(ref _disposed) == 0) Dispatcher.Run();
            Cleanup();
        }

        private void ApplyPlacement(Placement placement)
        {
            _placement = placement;
            if (!placement.Visible || placement.Width <= 0 || placement.Height <= 0)
            {
                _surface?.Hide();
                SetCapturing(false);
                return;
            }
            // Same reflow as AuxiliaryOverlayWindow: one uniform design scale fills the window.
            double designWidth = _expanded ? ExpandedDesignWidth : CompactDesignWidth;
            double designHeight = _expanded ? ExpandedDesignHeight : CompactDesignHeight;
            double scale = Math.Min(placement.Width / designWidth, placement.Height / designHeight);
            _scale.ScaleX = _scale.ScaleY = scale;
            _view!.DesignViewport = placement.Viewport;
            _view!.Width = placement.Width / scale;
            _view.Height = placement.Height / scale;
            _root!.Width = placement.Width;
            _root.Height = placement.Height;
            _dirty = true;
            SetCapturing(true);
        }

        private void SetCapturing(bool active)
        {
            if (active)
            {
                if (_clockSubscribed || _fallbackTimer != null) return;
                _clockSubscribed = MonitorPresentationClock.Subscribe(Frame);
                if (!_clockSubscribed)
                {
                    _fallbackTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
                    _fallbackTimer.Tick += Frame;
                    _fallbackTimer.Start();
                }
                return;
            }
            if (_clockSubscribed) { MonitorPresentationClock.Unsubscribe(Frame); _clockSubscribed = false; }
            _fallbackTimer?.Stop(); _fallbackTimer = null;
        }

        private void Frame(object? sender, EventArgs args)
        {
            if (Volatile.Read(ref _disposed) != 0 || _view == null || _surface == null || !_placement.Visible) return;
            long now = Stopwatch.GetTimestamp();
            if (now - _lastCaptureTicks < HudFrameRate.FrameTicks * 9 / 10) return; // user-selected HUD frame limit
            bool animating = _view.IsAnimating;
            // Capture only when inputs or motion changed; one extra frame settles the last motion step.
            if (!_dirty && !animating && !_wasAnimating) { Interlocked.Increment(ref _skipped); return; }
            _wasAnimating = animating;
            _dirty = false;
            _lastCaptureTicks = now;
            int width = _placement.Width, height = _placement.Height;
            _root!.UpdateLayout();
            if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                _bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            else _bitmap.Clear();
            _bitmap.Render(_root);
            _surface.Present(_bitmap, _placement);
            Interlocked.Increment(ref _frames);
            if (!_presented) { _presented = true; _firstFrame(); }
        }

        private void Fail(Exception exception)
        {
            if (Interlocked.Exchange(ref _failedOnce, 1) == 0) _failed(exception);
            Dispatcher? dispatcher;
            lock (_startGate) dispatcher = _dispatcher;
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        }

        private void Cleanup()
        {
            SetCapturing(false);
            _surface?.Dispose(); _surface = null;
            if (_host != null)
            {
                // Dispatcher shutdown can dispose its HwndSource before this worker cleans up.
                if (!_host.IsDisposed) { _host.RootVisual = null; _host.Dispose(); }
                _host = null;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Dispatcher? dispatcher;
            lock (_startGate) dispatcher = _dispatcher;
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
            // Never block the UI thread for long; the worker is a background thread.
            if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
        }

        // Native layered click-through window fed from a premultiplied BGRA DIB section.
        internal sealed class LayeredSurface : IDisposable
        {
            private const int WsExLayered = 0x00080000, WsExTransparent = 0x00000020, WsExToolWindow = 0x00000080;
            private const int WsExNoActivate = 0x08000000, WsExTopmost = 0x00000008;
            private const uint WsPopup = 0x80000000;
            private const string ClassName = "AMS2KRLeague.ThreadedHud";
            private static readonly WndProc Procedure = WindowProcedure;
            private static readonly object ClassGate = new object();
            private static bool _registered;
            private readonly IntPtr _hwnd, _memoryDc;
            private IntPtr _dib, _previousBitmap, _bits;
            private int _width, _height;
            private bool _shown;
            internal IntPtr Handle => _hwnd;

            internal LayeredSurface()
            {
                IntPtr instance = GetModuleHandle(null);
                lock (ClassGate)
                {
                    if (!_registered)
                    {
                        var windowClass = new WNDCLASSEX
                        {
                            cbSize = Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
                            hInstance = instance, lpszClassName = ClassName
                        };
                        if (RegisterClassEx(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                        _registered = true;
                    }
                }
                _hwnd = CreateWindowEx(WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExTopmost,
                    ClassName, "AMS2 N HUD", WsPopup, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                _memoryDc = CreateCompatibleDC(IntPtr.Zero);
                if (_memoryDc == IntPtr.Zero) { DestroyWindow(_hwnd); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            }

            internal void Present(BitmapSource bitmap, Placement placement)
            {
                int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
                EnsureDib(width, height);
                GdiFlush();
                bitmap.CopyPixels(new Int32Rect(0, 0, width, height), _bits, width * height * 4, width * 4);
                var destination = new POINT { X = placement.X, Y = placement.Y };
                var size = new SIZE { Width = width, Height = height };
                var source = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = placement.Alpha, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref destination, ref size, _memoryDc, ref source, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!_shown) { ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */); _shown = true; }
            }

            internal void Hide()
            {
                if (!_shown) return;
                ShowWindow(_hwnd, 0 /* SW_HIDE */);
                _shown = false;
            }

            private void EnsureDib(int width, int height)
            {
                if (_dib != IntPtr.Zero && width == _width && height == _height) return;
                ReleaseDib();
                var info = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, // top-down
                    biPlanes = 1, biBitCount = 32, biCompression = 0
                };
                _dib = CreateDIBSection(_memoryDc, ref info, 0, out _bits, IntPtr.Zero, 0);
                if (_dib == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                _previousBitmap = SelectObject(_memoryDc, _dib);
                _width = width; _height = height;
            }

            private void ReleaseDib()
            {
                if (_dib == IntPtr.Zero) return;
                SelectObject(_memoryDc, _previousBitmap);
                DeleteObject(_dib);
                _dib = _bits = IntPtr.Zero;
            }

            public void Dispose()
            {
                ReleaseDib();
                DeleteDC(_memoryDc);
                DestroyWindow(_hwnd);
            }

            private static IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
            {
                if (message == 0x0084) return new IntPtr(-1); // WM_NCHITTEST -> HTTRANSPARENT
                if (message == 0x0021) return new IntPtr(3);  // WM_MOUSEACTIVATE -> MA_NOACTIVATE
                return DefWindowProc(hwnd, message, wParam, lParam);
            }

            private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WNDCLASSEX
            {
                public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
                public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm;
            }
            [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int Width, Height; }
            [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
            [StructLayout(LayoutKind.Sequential)]
            private struct BITMAPINFOHEADER
            {
                public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage,
                    biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
            }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style, int x, int y,
                int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
            [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
            [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
            [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDestination, ref POINT destination, ref SIZE size,
                IntPtr hdcSource, ref POINT source, int colorKey, ref BLENDFUNCTION blend, int flags);
            [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
            [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
            [DllImport("gdi32.dll", SetLastError = true)]
            private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
            [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);
            [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject);
            [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        }
    }
}
