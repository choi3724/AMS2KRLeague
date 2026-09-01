using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;

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

        public static void Configure(IntPtr handle)
        {
            long styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
            styles |= ExtendedTransparent | ExtendedToolWindow | ExtendedLayered | ExtendedNoActivate;
            SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(styles));

            HwndSource? source = HwndSource.FromHwnd(handle);
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
