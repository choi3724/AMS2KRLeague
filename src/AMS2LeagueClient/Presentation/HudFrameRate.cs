using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AMS2LeagueClient.Presentation
{
    // The user-selected HUD motion/redraw rate (DrivingHudSettings.HudRefreshRate). 0 follows the
    // monitor: the DWM composition rate, re-read at most every 5 seconds. Higher rates look
    // smoother and use more CPU; a rate above the monitor's cannot be shown.
    internal static class HudFrameRate
    {
        private const double Fallback = 60, Minimum = 24, Maximum = 360;
        private static int _setting;
        private static double _monitorRate = Fallback;
        private static long _monitorReadAt = long.MinValue;
        private static readonly object MonitorGate = new object();

        internal static int Setting => Volatile.Read(ref _setting);
        internal static void Apply(int setting) => Volatile.Write(ref _setting, Math.Max(0, setting));

        internal static double Current
        {
            get
            {
                int setting = Setting;
                return setting > 0 ? setting : MonitorRate();
            }
        }

        // Stopwatch ticks between two HUD frames at the current rate.
        internal static long FrameTicks => (long)(Stopwatch.Frequency / Current);

        // WPF Timeline.DesiredFrameRate: null lets WPF follow the display.
        internal static int? TimelineRate => Setting > 0 ? Setting : (int?)null;

        private static double MonitorRate()
        {
            long now = Stopwatch.GetTimestamp();
            lock (MonitorGate)
            {
                if (now - _monitorReadAt < Stopwatch.Frequency * 5) return _monitorRate;
                _monitorReadAt = now;
                var info = new DWM_TIMING_INFO { cbSize = (uint)Marshal.SizeOf<DWM_TIMING_INFO>() };
                try
                {
                    if (DwmGetCompositionTimingInfo(IntPtr.Zero, ref info) >= 0 && info.rateRefresh.uiDenominator != 0)
                    {
                        double rate = info.rateRefresh.uiNumerator / (double)info.rateRefresh.uiDenominator;
                        if (double.IsFinite(rate)) _monitorRate = Math.Clamp(rate, Minimum, Maximum);
                    }
                }
                catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
                {
                    _monitorRate = Fallback; // No DWM timing on this system; keep a conventional rate.
                }
                return _monitorRate;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNSIGNED_RATIO { public uint uiNumerator, uiDenominator; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct DWM_TIMING_INFO
        {
            public uint cbSize;
            public UNSIGNED_RATIO rateRefresh;
            public ulong qpcRefreshPeriod;
            public UNSIGNED_RATIO rateCompose;
            public ulong qpcVBlank, cRefresh;
            public uint cDXRefresh;
            public ulong qpcCompose, cFrame;
            public uint cDXPresent;
            public ulong cRefreshFrame, cFrameSubmitted;
            public uint cDXPresentSubmitted;
            public ulong cFrameConfirmed;
            public uint cDXPresentConfirmed;
            public ulong cRefreshConfirmed;
            public uint cDXRefreshConfirmed;
            public ulong cFramesLate;
            public uint cFramesOutstanding;
            public ulong cFrameDisplayed, qpcFrameDisplayed, cRefreshFrameDisplayed, cFrameComplete, qpcFrameComplete,
                cFramePending, qpcFramePending, cFramesDisplayed, cFramesComplete, cFramesPending, cFramesAvailable,
                cFramesDropped, cFramesMissed, cRefreshNextDisplayed, cRefreshNextPresented, cRefreshesDisplayed,
                cRefreshesPresented, cRefreshStarted, cPixelsReceived, cPixelsDrawn, cBuffersEmpty;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref DWM_TIMING_INFO timingInfo);
    }
}
