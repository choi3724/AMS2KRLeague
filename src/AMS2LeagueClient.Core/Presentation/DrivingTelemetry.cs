using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class DrivingHudSettings
    {
        public const string DefaultFontName = "Pretendard";
        public string BrakeColor { get; set; } = "#FF3030";
        public string ThrottleColor { get; set; } = "#20E050";
        public string ClutchColor { get; set; } = "#3399FF";
        public string HandBrakeColor { get; set; } = "#BE60FF";
        public string SpeedFont { get; set; } = DefaultFontName;
        public string GearFont { get; set; } = DefaultFontName;
        public string SpeedShadowColor { get; set; } = "#000000";
        public string GearShadowColor { get; set; } = "#000000";

        public DrivingHudSettings Normalize() => new DrivingHudSettings
        {
            BrakeColor = Color(BrakeColor, "#FF3030"),
            ThrottleColor = Color(ThrottleColor, "#20E050"),
            ClutchColor = Color(ClutchColor, "#3399FF"),
            HandBrakeColor = Color(HandBrakeColor, "#BE60FF"),
            SpeedFont = Font(SpeedFont), GearFont = Font(GearFont),
            SpeedShadowColor = Color(SpeedShadowColor, "#000000"),
            GearShadowColor = Color(GearShadowColor, "#000000")
        };

        private static string Color(string value, string fallback)
            => value != null && value.Length == 7 && value[0] == '#'
                && value.Skip(1).All(Uri.IsHexDigit) ? value.ToUpperInvariant() : fallback;
        private static string Font(string value)
            => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl)
                && value.IndexOfAny(new[] { '/', '\\', ':', '#' }) < 0 ? value.Trim() : DefaultFontName;
    }

    public sealed class DrivingTelemetrySample
    {
        public DrivingTelemetrySample(DateTimeOffset capturedAt, int generation, int participantIndex,
            double brake, double throttle, double clutch, double handBrake, double speedMetresPerSecond, int gear)
        {
            CapturedAt = capturedAt; Generation = generation; ParticipantIndex = participantIndex;
            Pedals = Array.AsReadOnly(new[] { Pedal(brake), Pedal(throttle), Pedal(clutch), Pedal(handBrake) });
            SpeedKmh = double.IsFinite(speedMetresPerSecond) && speedMetresPerSecond >= 0
                && speedMetresPerSecond <= 1000 ? (double?)(speedMetresPerSecond * 3.6) : null;
            Gear = gear >= -1 && gear <= 32 ? (int?)gear : null;
        }

        public DateTimeOffset CapturedAt { get; }
        public int Generation { get; }
        public int ParticipantIndex { get; }
        public IReadOnlyList<double?> Pedals { get; }
        public double? SpeedKmh { get; }
        public int? Gear { get; }
        public string SpeedText => (SpeedKmh?.ToString("0", CultureInfo.InvariantCulture) ?? "—") + " km/h";
        public string GearText => Gear?.ToString(CultureInfo.InvariantCulture) ?? "—";

        public static DrivingTelemetrySample? FromSnapshot(TelemetrySnapshot snapshot, int localIndex, int generation)
        {
            ViewedVehicleTelemetrySnapshot? vehicle = snapshot.ViewedVehicleTelemetry;
            if (vehicle == null || localIndex < 0 || snapshot.ViewedParticipantIndex != localIndex) return null;
            return new DrivingTelemetrySample(snapshot.CapturedAt, generation, localIndex, vehicle.Brake,
                vehicle.Throttle, vehicle.Clutch, vehicle.HandBrake, vehicle.SpeedMetresPerSecond, vehicle.Gear);
        }

        private static double? Pedal(double value)
            => double.IsFinite(value) && value >= 0 && value <= 1 ? (double?)value : null;
    }

    public sealed class DrivingTelemetryHistory
    {
        public const int MaximumSamples = 256;
        public const double DurationSeconds = 10;
        private readonly Queue<DrivingTelemetrySample> _samples = new Queue<DrivingTelemetrySample>();
        public IEnumerable<DrivingTelemetrySample> Samples => _samples;
        public int Count => _samples.Count;
        public DrivingTelemetrySample? Current { get; private set; }

        public void Clear() { _samples.Clear(); Current = null; }

        public void Add(DrivingTelemetrySample? sample)
        {
            if (sample == null) { Clear(); return; }
            if (Current != null)
            {
                double interval = (sample.CapturedAt - Current.CapturedAt).TotalSeconds;
                if (sample.Generation != Current.Generation || sample.ParticipantIndex != Current.ParticipantIndex
                    || interval < 0 || interval > 1) Clear();
                else if (interval == 0) return;
            }
            Current = sample;
            _samples.Enqueue(sample);
            while (_samples.Count > MaximumSamples
                || (sample.CapturedAt - _samples.Peek().CapturedAt).TotalSeconds > DurationSeconds)
                _samples.Dequeue();
        }
    }
}
