using System;

namespace AMS2LeagueClient.Presentation
{
    // A one-shot mode-entry effect, independent of RPM and telemetry cadence.
    internal static class AvanteIgnitionSweep
    {
        // The supplied four-second cluster recording lights the ring at about
        // 2.1 s, completes the sweep near 2.35 s, and settles near 3.2 s.
        internal const double DurationSeconds = 1.15;
        // Screen-space angles increase clockwise. The source transition starts
        // at the lower right and wraps upward, across the top, then left.
        internal const double StartAngle = 400;
        internal const double EndAngle = 40.5;
        internal static double Angle(double progress) => StartAngle -
            (StartAngle - EndAngle) * Math.Clamp(progress / .22, 0, 1);

        internal static double Opacity(double progress) =>
            Math.Clamp((1 - progress) / .22, 0, 1);

        // The cyan field leads the new face, then clears while the flame holds.
        internal static double CoreOpacity(double progress) =>
            Math.Clamp(progress / .06, 0, 1) * Math.Clamp((.85 - progress) / .57, 0, 1);
    }
}
