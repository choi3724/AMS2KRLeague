using System;

namespace AMS2LeagueClient.Presentation
{
    // A one-shot mode-entry effect, independent of RPM and telemetry cadence.
    internal static class AvanteIgnitionSweep
    {
        internal const double DurationSeconds = .9;
        internal const double StartAngle = 140;
        internal const double EndAngle = 400;
        internal static double Angle(double progress) => StartAngle +
            (EndAngle - StartAngle) * Math.Clamp(progress / .82, 0, 1);

        internal static double Opacity(double progress) =>
            Math.Clamp((1 - progress) / .18, 0, 1);
    }
}
