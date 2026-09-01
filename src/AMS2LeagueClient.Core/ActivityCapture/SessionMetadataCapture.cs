using System;
using System.Collections.Generic;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    public sealed class WeatherTimelineCompressor
    {
        public const int MaximumPoints = 256;
        private static readonly TimeSpan PeriodicInterval = TimeSpan.FromMinutes(1);
        private readonly List<ObservedWeatherPoint> _points = new List<ObservedWeatherPoint>();
        private readonly DateTimeOffset _startedAtUtc;

        public WeatherTimelineCompressor(DateTimeOffset startedAtUtc)
        {
            _startedAtUtc = startedAtUtc.ToUniversalTime();
        }

        public IReadOnlyList<ObservedWeatherPoint> Points => _points;

        public bool Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ObservedWeatherPoint candidate = FromSnapshot(snapshot, _startedAtUtc);
            if (_points.Count == 0)
            {
                _points.Add(candidate);
                return true;
            }

            ObservedWeatherPoint last = _points[_points.Count - 1];
            bool periodic = candidate.CapturedAtUtc - last.CapturedAtUtc >= PeriodicInterval;
            bool changed = Difference(candidate.RainDensity, last.RainDensity) >= 0.01f
                || Difference(candidate.SnowDensity, last.SnowDensity) >= 0.01f
                || Difference(candidate.AmbientTemperatureCelsius, last.AmbientTemperatureCelsius) >= 0.5f
                || Difference(candidate.TrackTemperatureCelsius, last.TrackTemperatureCelsius) >= 0.5f
                || Difference(candidate.WindSpeed, last.WindSpeed) >= 0.5f
                || Difference(candidate.WindDirectionX, last.WindDirectionX) >= 0.05f
                || Difference(candidate.WindDirectionY, last.WindDirectionY) >= 0.05f
                || Difference(candidate.CloudBrightness, last.CloudBrightness) >= 0.05f;
            if (!periodic && !changed) return false;

            if (_points.Count == MaximumPoints)
            {
                // Keep the first observation and the most recent change points.
                _points.RemoveAt(1);
            }
            _points.Add(candidate);
            return true;
        }

        public List<ObservedWeatherPoint> Snapshot()
            => new List<ObservedWeatherPoint>(_points);

        private static ObservedWeatherPoint FromSnapshot(TelemetrySnapshot snapshot, DateTimeOffset startedAtUtc)
            => new ObservedWeatherPoint
            {
                CapturedAtUtc = snapshot.CapturedAt.ToUniversalTime(),
                SessionElapsedSeconds = Math.Max(0, (snapshot.CapturedAt.ToUniversalTime() - startedAtUtc).TotalSeconds),
                AmbientTemperatureCelsius = snapshot.AmbientTemperature,
                TrackTemperatureCelsius = snapshot.TrackTemperature,
                RainDensity = snapshot.RainDensity,
                WindSpeed = snapshot.WindSpeed,
                WindDirectionX = snapshot.WindDirectionX,
                WindDirectionY = snapshot.WindDirectionY,
                CloudBrightness = snapshot.CloudBrightness,
                SnowDensity = snapshot.SnowDensity
            };

        private static float Difference(float first, float second)
        {
            if (!IsFinite(first) || !IsFinite(second)) return float.MaxValue;
            return Math.Abs(first - second);
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class SessionMetadataAccumulator
    {
        private readonly DateTimeOffset _startedAtUtc;
        private readonly string _sessionType;
        private readonly WeatherTimelineCompressor _weather;
        private ConfiguredSessionSettings _configured = new ConfiguredSessionSettings();
        private bool _privateObserved;

        public SessionMetadataAccumulator(DateTimeOffset startedAtUtc, string sessionType)
        {
            _startedAtUtc = startedAtUtc.ToUniversalTime();
            _sessionType = sessionType ?? string.Empty;
            _weather = new WeatherTimelineCompressor(_startedAtUtc);
        }

        public void Observe(TelemetrySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            _weather.Observe(snapshot);
            _privateObserved |= snapshot.SessionIsPrivate;

            var next = new ConfiguredSessionSettings
            {
                Enabled = null,
                EnabledStatus = CaptureCapabilityStatus.Unknown,
                DurationMinutes = IsFinitePositive(snapshot.SessionDuration) ? snapshot.SessionDuration : (double?)null,
                DurationStatus = IsFinitePositive(snapshot.SessionDuration)
                    ? CaptureCapabilityStatus.ObservedOnly
                    : CaptureCapabilityStatus.Unknown,
                ConfiguredLaps = !IsFinitePositive(snapshot.SessionDuration) && snapshot.LapsInEvent > 0
                    ? checked((int)snapshot.LapsInEvent)
                    : (int?)null,
                ConfiguredLapsStatus = !IsFinitePositive(snapshot.SessionDuration) && snapshot.LapsInEvent > 0
                    ? CaptureCapabilityStatus.ObservedOnly
                    : CaptureCapabilityStatus.Unknown,
                MandatoryPitLap = snapshot.EnforcedPitStopLap >= 0 ? snapshot.EnforcedPitStopLap : (int?)null,
                MandatoryPitStatus = snapshot.EnforcedPitStopLap >= 0
                    ? CaptureCapabilityStatus.ObservedOnly
                    : CaptureCapabilityStatus.Unknown,
                InGameDate = null,
                InGameDateStatus = CaptureCapabilityStatus.NotExposed,
                StartTime = null,
                StartTimeStatus = CaptureCapabilityStatus.NotExposed,
                WeatherSlots = null,
                WeatherSlotsStatus = CaptureCapabilityStatus.NotExposed,
                WeatherProgression = null,
                WeatherProgressionStatus = CaptureCapabilityStatus.NotExposed,
                TimeProgression = null,
                TimeProgressionStatus = CaptureCapabilityStatus.NotExposed,
                FormationLapEnabled = null,
                FormationLapStatus = CaptureCapabilityStatus.Unknown,
                FuelUsageMultiplier = null,
                FuelUsageStatus = CaptureCapabilityStatus.NotExposed,
                TyreWearMultiplier = null,
                TyreWearStatus = CaptureCapabilityStatus.NotExposed,
                DamageSetting = null,
                DamageStatus = CaptureCapabilityStatus.NotExposed,
                AssistsAndRules = null,
                AssistsAndRulesStatus = CaptureCapabilityStatus.NotExposed
            };
            _configured = next;
        }

        public ConfiguredSessionSettings ConfiguredSettings => _configured;

        public ObservedSessionConditions BuildObserved(DateTimeOffset endedAtUtc)
            => new ObservedSessionConditions
            {
                Observed = true,
                SessionType = _sessionType,
                ActualStartTimestampUtc = _startedAtUtc,
                ActualEndTimestampUtc = endedAtUtc.ToUniversalTime(),
                SessionIsPrivate = _privateObserved,
                RaceMode = "UNKNOWN",
                WeatherTimeline = _weather.Snapshot(),
                WeatherStatus = CaptureCapabilityStatus.ObservedOnly
            };

        private static bool IsFinitePositive(float value)
            => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
