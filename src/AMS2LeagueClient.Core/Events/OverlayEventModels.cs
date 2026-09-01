using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Events
{
    public enum OverlayEventType
    {
        PositionGained,
        PositionLost,
        PersonalBest,
        RaceFastestLap,
        FinalLap,
        PitEntry,
        PitExit,
        Finish,
        Retired,
        Disqualified,
        InvalidLap,
        Battle,
        OpeningStart,
        LeaderChange,
        PodiumEntry,
        PodiumExit
    }

    public enum OverlayEventPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public sealed class OverlayEvent
    {
        public OverlayEvent(
            OverlayEventType type,
            OverlayEventPriority priority,
            DateTimeOffset detectedAt,
            TimeSpan displayDuration,
            TimeSpan queueLifetime,
            string title,
            string primaryText,
            string secondaryText,
            string sourceKind,
            string cooldownKey = "",
            string driver = "",
            uint oldPosition = 0,
            uint newPosition = 0,
            float lapTime = -1,
            float delta = 0)
        {
            Id = type.ToString().ToUpperInvariant() + ":" + Guid.NewGuid().ToString("N");
            Type = type;
            Priority = priority;
            DetectedAt = detectedAt;
            ExpiresAt = detectedAt + queueLifetime;
            DisplayDuration = displayDuration;
            Title = title ?? string.Empty;
            PrimaryText = primaryText ?? string.Empty;
            SecondaryText = secondaryText ?? string.Empty;
            SourceKind = sourceKind ?? string.Empty;
            CooldownKey = cooldownKey ?? string.Empty;
            Driver = driver ?? string.Empty;
            OldPosition = oldPosition;
            NewPosition = newPosition;
            LapTime = lapTime;
            Delta = delta;
        }

        public string Id { get; }
        public OverlayEventType Type { get; }
        public OverlayEventPriority Priority { get; }
        public DateTimeOffset DetectedAt { get; }
        public DateTimeOffset ExpiresAt { get; }
        public TimeSpan DisplayDuration { get; }
        public string Title { get; }
        public string PrimaryText { get; }
        public string SecondaryText { get; }
        public string Driver { get; }
        public uint OldPosition { get; }
        public uint NewPosition { get; }
        public float LapTime { get; }
        public float Delta { get; }
        public string SourceKind { get; }
        public string CooldownKey { get; }
    }

    public sealed class RaceEventUpdate
    {
        public RaceEventUpdate(
            IReadOnlyList<OverlayEvent> detectedEvents,
            OverlayEvent? currentEvent,
            int queuedCount,
            bool stateReset)
        {
            DetectedEvents = detectedEvents;
            CurrentEvent = currentEvent;
            QueuedCount = queuedCount;
            StateReset = stateReset;
        }

        public IReadOnlyList<OverlayEvent> DetectedEvents { get; }
        public OverlayEvent? CurrentEvent { get; }
        public int QueuedCount { get; }
        public bool StateReset { get; }
    }
}
