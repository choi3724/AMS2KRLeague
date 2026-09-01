using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    public sealed class ScheduledLeagueEvent
    {
        public string EventId { get; set; } = string.Empty;
        public DateTimeOffset? CaptureOpensAtUtc { get; set; }
        public DateTimeOffset? ScheduledAtUtc { get; set; }
        public string? ExpectedTrack { get; set; }
        public string? ExpectedVehicleClass { get; set; }
    }

    public sealed class LeagueCaptureDecision
    {
        public bool IsLeagueCandidate { get; set; }
        public string? LeagueCandidateId { get; set; }
        public string? ScheduledEventId { get; set; }
        public DateTimeOffset? ChainAnchorUtc { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class LeagueCapturePolicy
    {
        private static readonly TimeSpan ChainContinuationLimit = TimeSpan.FromHours(12);
        private static readonly TimeSpan ScheduledEventTail = TimeSpan.FromHours(12);
        private readonly TimeZoneInfo _kst;

        public LeagueCapturePolicy()
        {
            _kst = ResolveKoreaTimeZone();
        }

        public LeagueCaptureDecision Classify(
            DateTimeOffset startedAtUtc,
            DateTimeOffset? chainAnchorUtc = null,
            ScheduledLeagueEvent? scheduledEvent = null,
            string? observedTrack = null,
            string? observedVehicleClass = null)
        {
            DateTimeOffset utc = startedAtUtc.ToUniversalTime();
            if (scheduledEvent != null && MatchesScheduledEvent(utc, scheduledEvent, observedTrack, observedVehicleClass))
            {
                DateTimeOffset anchor = scheduledEvent.CaptureOpensAtUtc
                    ?? scheduledEvent.ScheduledAtUtc
                    ?? utc;
                return new LeagueCaptureDecision
                {
                    IsLeagueCandidate = true,
                    ChainAnchorUtc = anchor.ToUniversalTime(),
                    LeagueCandidateId = string.IsNullOrWhiteSpace(scheduledEvent.EventId)
                        ? CreateCandidateId(anchor)
                        : "league-" + scheduledEvent.EventId,
                    ScheduledEventId = string.IsNullOrWhiteSpace(scheduledEvent.EventId) ? null : scheduledEvent.EventId,
                    Reason = "SERVER_SCHEDULED_EVENT"
                };
            }

            if (chainAnchorUtc.HasValue)
            {
                TimeSpan elapsed = utc - chainAnchorUtc.Value.ToUniversalTime();
                if (elapsed >= TimeSpan.Zero && elapsed <= ChainContinuationLimit)
                {
                    return new LeagueCaptureDecision
                    {
                        IsLeagueCandidate = true,
                        ChainAnchorUtc = chainAnchorUtc.Value.ToUniversalTime(),
                        LeagueCandidateId = CreateCandidateId(chainAnchorUtc.Value),
                        Reason = "ACTIVE_TUESDAY_CHAIN_CONTINUATION"
                    };
                }
            }

            DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, _kst);
            if (local.DayOfWeek == DayOfWeek.Tuesday && local.TimeOfDay >= TimeSpan.FromHours(22))
            {
                return new LeagueCaptureDecision
                {
                    IsLeagueCandidate = true,
                    ChainAnchorUtc = utc,
                    LeagueCandidateId = CreateCandidateId(utc),
                    Reason = "TUESDAY_2200_KST_FALLBACK"
                };
            }

            return new LeagueCaptureDecision
            {
                IsLeagueCandidate = false,
                Reason = "OUTSIDE_LEAGUE_CAPTURE_WINDOW"
            };
        }

        public DateTimeOffset ToKoreaTime(DateTimeOffset value)
            => TimeZoneInfo.ConvertTime(value, _kst);

        private static bool MatchesScheduledEvent(
            DateTimeOffset startUtc,
            ScheduledLeagueEvent scheduledEvent,
            string? observedTrack,
            string? observedVehicleClass)
        {
            DateTimeOffset? open = scheduledEvent.CaptureOpensAtUtc ?? scheduledEvent.ScheduledAtUtc;
            if (!open.HasValue || startUtc < open.Value.ToUniversalTime() || startUtc > open.Value.ToUniversalTime() + ScheduledEventTail)
            {
                return false;
            }

            if (!MatchesOptional(scheduledEvent.ExpectedTrack, observedTrack)) return false;
            if (!MatchesOptional(scheduledEvent.ExpectedVehicleClass, observedVehicleClass)) return false;
            return true;
        }

        private static bool MatchesOptional(string? expected, string? observed)
        {
            if (string.IsNullOrWhiteSpace(expected)) return true;
            return !string.IsNullOrWhiteSpace(observed)
                && string.Equals(expected.Trim(), observed.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string CreateCandidateId(DateTimeOffset anchor)
        {
            string input = anchor.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            using SHA256 sha = SHA256.Create();
            string hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
            return "league-chain-" + hash.Substring(0, 20);
        }

        private static TimeZoneInfo ResolveKoreaTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
            }
        }
    }
}
