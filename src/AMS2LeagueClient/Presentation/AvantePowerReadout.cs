using System;
using System.Globalization;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Presentation
{
    internal sealed class AvantePowerReadout
    {
        internal string Label { get; }
        internal string Value { get; }
        internal string Unit { get; }
        internal string ErsMode { get; }
        internal string OtherBoost { get; }
        internal string SecondaryText => OtherBoost.Length == 0 ? (ErsMode.Length == 0 ? "" : "ERS 모드: " + ErsMode)
            : ErsMode.Length == 0 ? OtherBoost : OtherBoost + " · ERS " + ErsMode;
        private AvantePowerReadout(string label, string value, string unit, string ersMode, string otherBoost = "")
        { Label = label; Value = value; Unit = unit; ErsMode = ersMode; OtherBoost = otherBoost; }
        internal int ValueSize => Label == "ERS 모드" ? 86 : 140;

        internal static AvantePowerReadout From(ViewedVehicleTelemetrySnapshot? vehicle, bool preview = false)
        {
            if (preview) return new AvantePowerReadout("터보", "1.10", "bar", "");
            if (vehicle == null) return new AvantePowerReadout("터보", "—", "bar", "");

            string mode = ErsModeText(vehicle.ErsDeploymentModeRaw);
            float pressure = vehicle.TurboBoostPressure;
            float boost = vehicle.BoostAmount;
            bool hasBoost = float.IsFinite(boost) && boost >= 0 && boost <= 100
                && (vehicle.BoostActive || boost > 0);
            // Archived AMS2 SHM samples use roughly 100,000 per bar; keep the header's 0..1+ scale too.
            float bar = pressure > 10f ? pressure / 100000f : pressure;
            if (float.IsFinite(pressure) && pressure > 0 && float.IsFinite(bar) && bar <= 5f)
                return new AvantePowerReadout("터보", bar.ToString("0.00", CultureInfo.InvariantCulture), "bar", mode,
                    hasBoost ? "부스트 " + boost.ToString("0.#", CultureInfo.InvariantCulture) : "");

            if (hasBoost)
                return new AvantePowerReadout("부스트량", boost.ToString("0.#", CultureInfo.InvariantCulture), "", mode);

            if (mode.Length != 0) return new AvantePowerReadout("ERS 모드", mode, "", "");
            if (float.IsFinite(pressure) && pressure == 0) return new AvantePowerReadout("터보", "0.00", "bar", "");
            return new AvantePowerReadout("터보", "—", "bar", "");
        }

        internal static string Torque(float? torque)
        {
            if (torque is float value && float.IsFinite(value) && value >= -4000 && value <= 4000)
            {
                float rounded = MathF.Round(value, MidpointRounding.AwayFromZero);
                return rounded == 0 ? "0" : rounded.ToString("0", CultureInfo.InvariantCulture);
            }
            return "—";
        }

        private static string ErsModeText(int mode) => mode switch
        {
            1 => "끔",
            2 => "충전",
            3 => "균형",
            4 => "공격",
            5 => "최대",
            _ => ""
        };
    }
}
