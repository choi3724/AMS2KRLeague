using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Presentation
{
    internal enum AvanteIndicatorState { Unknown, Off, On, Active }

    internal static class AvanteIndicators
    {
        internal static AvanteIndicatorState Abs(ViewedVehicleTelemetrySnapshot? vehicle, DrivingTelemetrySample? sample)
        {
            if (sample?.AbsActive == true || vehicle?.AntiLockActive == true) return AvanteIndicatorState.Active;
            if (vehicle == null) return AvanteIndicatorState.Unknown;
            return (vehicle.CarFlagsRaw & (1u << 4)) != 0 || vehicle.AntiLockSetting > 0
                ? AvanteIndicatorState.On : AvanteIndicatorState.Off;
        }

        internal static AvanteIndicatorState Tcs(ViewedVehicleTelemetrySnapshot? vehicle)
        {
            if (vehicle == null) return AvanteIndicatorState.Unknown;
            bool enabled = (vehicle.CarFlagsRaw & (1u << 6)) != 0 || vehicle.TractionControlSetting > 0;
            if (!enabled) return AvanteIndicatorState.Off;
            // Shared Memory v14 exposes no direct TCS-intervening bit. This only
            // marks a strong throttle cut as an inferred intervention.
            bool cut = vehicle.Gear > 0 && vehicle.SpeedMetresPerSecond > 5
                && vehicle.UnfilteredThrottle > .35f && vehicle.UnfilteredThrottle - vehicle.Throttle > .2f;
            return cut ? AvanteIndicatorState.Active : AvanteIndicatorState.On;
        }

        internal static bool Headlights(ViewedVehicleTelemetrySnapshot? vehicle) =>
            vehicle != null && (vehicle.CarFlagsRaw & 1u) != 0;
    }
}
