using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;

namespace AMS2LeagueClient.Overlay
{
    public sealed class OverlayStyleState
    {
        public bool NoActivate { get; set; }
        public bool ClickThrough { get; set; }
        public bool ToolWindow { get; set; }
        public bool Layered { get; set; }

        public override string ToString()
        {
            return "noActivate=" + NoActivate
                + " clickThrough=" + ClickThrough
                + " toolWindow=" + ToolWindow
                + " layered=" + Layered;
        }
    }

    public static class OverlayWindowInterop
    {
        private const int ExtendedStyleIndex = -20;
        private const long ExtendedTransparent = 0x00000020L;
        private const long ExtendedToolWindow = 0x00000080L;
        private const long ExtendedLayered = 0x00080000L;
        private const long ExtendedNoActivate = 0x08000000L;
        private const int MessageMouseActivate = 0x0021;
        private const int MessageNcHitTest = 0x0084;
        private const int MouseActivateNoActivate = 3;
        private const int HitTestTransparent = -1;
        private const uint SetWindowNoActivate = 0x0010;
        private const uint SetWindowShow = 0x0040;
        private const uint SetWindowNoSize = 0x0001;
        private const uint SetWindowNoMove = 0x0002;
        private const uint SetWindowNoZOrder = 0x0004;
        private const uint SetWindowFrameChanged = 0x0020;
        private const int ShowNoActivate = 4;
        private static readonly IntPtr TopMost = new IntPtr(-1);
        private static readonly object StateGate = new object();
        private static readonly HashSet<IntPtr> EditingHandles = new HashSet<IntPtr>();

        public sealed class DisplayInfo
        {
            public string DeviceName { get; internal set; } = string.Empty;
            public string DeviceId { get; internal set; } = string.Empty;
            public OverlayBounds Bounds { get; internal set; }
            public OverlayBounds WorkArea { get; internal set; }
            public uint DpiX { get; internal set; }
            public uint DpiY { get; internal set; }
            public bool Primary { get; internal set; }
        }
        private static List<DisplayInfo>? _displays;
        private static OverlayBounds[] _displayBounds = Array.Empty<OverlayBounds>(), _workAreas = Array.Empty<OverlayBounds>();
        public static int DisplayRevision { get; private set; }
        // UI-thread cache, invalidated by native display/settings/DPI messages, never enumerated per frame.
        public static IReadOnlyList<DisplayInfo> Displays
        {
            get
            {
                if (_displays != null) return _displays;
                var result = new List<DisplayInfo>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) =>
                {
                    var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), Device = string.Empty };
                    if (!GetMonitorInfoEx(monitor, ref info)) return true;
                    uint dx = 96, dy = 96;
                    if (GetDpiForMonitor(monitor, 0, out dx, out dy) != 0) dx = dy = 96;
                    var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                    string id = EnumDisplayDevices(info.Device, 0, ref device, 1) ? device.DeviceId : string.Empty;
                    result.Add(new DisplayInfo { DeviceName = info.Device, DeviceId = id,
                        Bounds = Bounds(info.Monitor), WorkArea = Bounds(info.Work), DpiX = dx, DpiY = dy, Primary = (info.Flags & 1) != 0 });
                    return true;
                }, IntPtr.Zero);
                _displayBounds = result.ConvertAll(d => d.Bounds).ToArray();
                _workAreas = result.ConvertAll(d => d.WorkArea).ToArray();
                return _displays = result;
            }
        }
        public static OverlayBounds RecoverToDisplay(OverlayBounds desired)
        {
            _ = Displays;
            return OverlayScreenPlacement.Recover(desired, _displayBounds, _workAreas);
        }
        private static OverlayBounds Bounds(NativeRect r) => new OverlayBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoEx(IntPtr monitor, ref MonitorInfoEx info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfoEx
        { public int Size; public NativeRect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }

        public static GameWindowSnapshot GetLayoutPreviewArea(IntPtr owner)
        {
            IntPtr monitor = MonitorFromWindow(owner, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("편집할 모니터 정보를 읽지 못했습니다.");
            uint dpi = owner == IntPtr.Zero ? 96 : GetDpiForWindow(owner);
            NativeRect area = info.Monitor;
            return new GameWindowSnapshot(IntPtr.Zero, area.Left, area.Top,
                area.Right - area.Left, area.Bottom - area.Top, dpi == 0 ? 96 : dpi,
                true, false, monitor.ToInt64());
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        public static void Configure(IntPtr handle)
        {
            long styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
            styles |= ExtendedTransparent | ExtendedToolWindow | ExtendedLayered | ExtendedNoActivate;
            SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(styles));

            HwndSource? source = HwndSource.FromHwnd(handle);
            // Renderer matrix: Default hardware + independent Monitor motion reduced CPU and delivery gaps.
            // WPF retains its device-loss/software fallback; VR-only surfaces select SoftwareOnly below.
            if (source?.CompositionTarget != null)
                source.CompositionTarget.RenderMode = RenderMode.Default;
            source?.AddHook(WindowProcedure);
        }

        public static void SetEditMode(IntPtr handle, bool enabled)
        {
            if (handle == IntPtr.Zero) return;
            lock (StateGate)
            {
                if (enabled) EditingHandles.Add(handle);
                else EditingHandles.Remove(handle);
            }

            long styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
            if (enabled)
            {
                styles &= ~ExtendedTransparent;
                styles &= ~ExtendedNoActivate;
            }
            else
            {
                styles |= ExtendedTransparent | ExtendedNoActivate;
            }
            SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(styles));
            SetWindowPos(
                handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SetWindowNoMove | SetWindowNoSize | SetWindowNoZOrder | SetWindowNoActivate | SetWindowFrameChanged);
        }

        public static OverlayBounds ReadPhysicalBounds(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out NativeRect rect))
            {
                return new OverlayBounds(0, 0, 0, 0);
            }
            return new OverlayBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public static void Forget(IntPtr handle)
        {
            lock (StateGate) EditingHandles.Remove(handle);
        }

        public static OverlayStyleState ReadStyleState(IntPtr handle)
        {
            long styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
            return new OverlayStyleState
            {
                NoActivate = (styles & ExtendedNoActivate) != 0,
                ClickThrough = (styles & ExtendedTransparent) != 0,
                ToolWindow = (styles & ExtendedToolWindow) != 0,
                Layered = (styles & ExtendedLayered) != 0
            };
        }

        public static void ShowWithoutActivation(IntPtr handle)
        {
            ShowWindow(handle, ShowNoActivate);
        }

        public static void SetPhysicalBounds(IntPtr handle, int x, int y, int width, int height)
        {
            SetWindowPos(handle, TopMost, x, y, width, height, SetWindowNoActivate | SetWindowShow);
        }

        private static IntPtr WindowProcedure(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x007e || message == 0x001a || message == 0x02e0)
            { _displays = null; DisplayRevision++; }
            bool editing;
            lock (StateGate) editing = EditingHandles.Contains(handle);
            if (!editing && message == MessageMouseActivate)
            {
                handled = true;
                return new IntPtr(MouseActivateNoActivate);
            }

            if (!editing && message == MessageNcHitTest)
            {
                handled = true;
                return new IntPtr(HitTestTransparent);
            }

            return IntPtr.Zero;
        }

        private static IntPtr GetWindowLongPtr(IntPtr handle, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index));
        }

        private static IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(handle, index, value) : new IntPtr(SetWindowLong32(handle, index, value.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr handle, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr handle, int index, IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr handle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
