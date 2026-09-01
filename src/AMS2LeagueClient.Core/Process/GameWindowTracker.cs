using System;
using System.Runtime.InteropServices;

namespace AMS2LeagueClient.Core.Process
{
    public sealed class GameWindowSnapshot
    {
        public GameWindowSnapshot(
            IntPtr handle,
            int left,
            int top,
            int width,
            int height,
            uint dpi,
            bool isForeground,
            bool isMinimized,
            long monitorHandle)
        {
            Handle = handle;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Dpi = dpi;
            IsForeground = isForeground;
            IsMinimized = isMinimized;
            MonitorHandle = monitorHandle;
        }

        public IntPtr Handle { get; }
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public uint Dpi { get; }
        public bool IsForeground { get; }
        public bool IsMinimized { get; }
        public long MonitorHandle { get; }
        public bool HasValidClientRect => Width > 0 && Height > 0;
        public string RectKey => Left + "," + Top + "," + Width + "x" + Height;
    }

    public sealed class GameWindowTracker
    {
        private const uint MonitorDefaultToNearest = 2;

        public GameWindowSnapshot? TryGetWindow(int processId)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;

            EnumWindows((handle, parameter) =>
            {
                GetWindowThreadProcessId(handle, out uint ownerPid);
                if (ownerPid != (uint)processId || !IsWindowVisible(handle))
                {
                    return true;
                }

                if (!GetClientRect(handle, out NativeRect clientRect))
                {
                    return true;
                }

                long area = (long)(clientRect.Right - clientRect.Left) * (clientRect.Bottom - clientRect.Top);
                if (area > bestArea)
                {
                    best = handle;
                    bestArea = area;
                }

                return true;
            }, IntPtr.Zero);

            if (best == IntPtr.Zero || !GetClientRect(best, out NativeRect rect))
            {
                return null;
            }

            var topLeft = new NativePoint { X = rect.Left, Y = rect.Top };
            var bottomRight = new NativePoint { X = rect.Right, Y = rect.Bottom };
            if (!ClientToScreen(best, ref topLeft) || !ClientToScreen(best, ref bottomRight))
            {
                return null;
            }

            IntPtr foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out uint foregroundPid);
            IntPtr monitor = MonitorFromWindow(best, MonitorDefaultToNearest);

            return new GameWindowSnapshot(
                best,
                topLeft.X,
                topLeft.Y,
                bottomRight.X - topLeft.X,
                bottomRight.Y - topLeft.Y,
                GetWindowDpi(best),
                foregroundPid == (uint)processId,
                IsIconic(best),
                monitor.ToInt64());
        }

        private static uint GetWindowDpi(IntPtr handle)
        {
            try
            {
                uint dpi = GetDpiForWindow(handle);
                return dpi == 0 ? 96U : dpi;
            }
            catch (EntryPointNotFoundException)
            {
                return 96U;
            }
        }

        private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr handle, ref NativePoint point);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    }
}
