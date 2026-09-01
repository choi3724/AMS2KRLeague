using System;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    /// <summary>
    /// Optional context supplied by Cafe24. This is captured as a hint only;
    /// the client never classifies an activity or treats the hint as official.
    /// Server-side schedule matching remains authoritative.
    /// </summary>
    public sealed class ScheduledLeagueEvent
    {
        public string EventId { get; set; } = string.Empty;
        public DateTimeOffset? CaptureOpensAtUtc { get; set; }
        public DateTimeOffset? ScheduledAtUtc { get; set; }
        public string? ExpectedTrack { get; set; }
        public string? ExpectedVehicleClass { get; set; }
    }
}
