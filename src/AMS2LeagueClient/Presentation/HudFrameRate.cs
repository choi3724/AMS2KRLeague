using System.Diagnostics;
using System.Threading;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Presentation
{
    // The user-selected HUD frame limit (DrivingHudSettings.HudFrameLimit): the most HUD frames
    // drawn per second. It is an FPS cap, not tied to the monitor; HUDs still draw only when a
    // value or a motion changes. Higher limits look smoother and use more CPU.
    internal static class HudFrameRate
    {
        private static int _limit = DrivingHudSettings.DefaultHudFrameLimit;

        internal static int Limit => Volatile.Read(ref _limit);
        internal static void Apply(int limit) => Volatile.Write(ref _limit, limit > 0 ? limit : DrivingHudSettings.DefaultHudFrameLimit);

        internal static double Current => Limit;

        // Stopwatch ticks between two HUD frames at the current limit.
        internal static long FrameTicks => Stopwatch.Frequency / Limit;
    }
}
