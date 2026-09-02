using System;
using System.Diagnostics;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    public interface ITelemetryMonotonicClock
    {
        long Timestamp { get; }
        long Frequency { get; }
    }

    public sealed class StopwatchTelemetryMonotonicClock : ITelemetryMonotonicClock
    {
        public long Timestamp => Stopwatch.GetTimestamp();
        public long Frequency => Stopwatch.Frequency;
    }

    public sealed class TelemetryCaptureStamp
    {
        public DateTimeOffset CapturedAtUtc { get; set; }
        public long SessionElapsedMs { get; set; }
        public string ClockSource { get; set; } = "MONOTONIC_CAPTURE_CLOCK";
    }

    /// <summary>
    /// Provides a non-decreasing capture clock from session start. This is deliberately
    /// independent from AMS2 current-lap time. Timed-session duration/remaining values
    /// are auxiliary metadata and must not replace this primary timeline.
    /// </summary>
    public sealed class TelemetrySessionClock
    {
        private readonly ITelemetryMonotonicClock _clock;
        private readonly long _startedAt;
        private long _lastElapsedMs;

        public TelemetrySessionClock(ITelemetryMonotonicClock? clock = null)
        {
            _clock = clock ?? new StopwatchTelemetryMonotonicClock();
            if (_clock.Frequency <= 0) throw new ArgumentOutOfRangeException(nameof(clock));
            _startedAt = _clock.Timestamp;
        }

        public TelemetryCaptureStamp Capture(DateTimeOffset capturedAtUtc)
        {
            long delta = Math.Max(0, _clock.Timestamp - _startedAt);
            long elapsed = checked((long)(delta * 1000.0 / _clock.Frequency));
            if (elapsed < _lastElapsedMs) elapsed = _lastElapsedMs;
            _lastElapsedMs = elapsed;
            return new TelemetryCaptureStamp
            {
                CapturedAtUtc = capturedAtUtc,
                SessionElapsedMs = elapsed
            };
        }
    }
}
