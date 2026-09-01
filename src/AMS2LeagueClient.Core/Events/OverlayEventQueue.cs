using System;
using System.Collections.Generic;
using System.Linq;

namespace AMS2LeagueClient.Core.Events
{
    public sealed class OverlayEventQueue
    {
        public const int MaximumWaitingEvents = 12;
        private readonly List<OverlayEvent> _waiting = new List<OverlayEvent>();
        private DateTimeOffset _currentStartedAt;

        public OverlayEvent? Current { get; private set; }
        public int WaitingCount => _waiting.Count;

        public void Enqueue(OverlayEvent item, DateTimeOffset now)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            RemoveExpired(now);

            if (Current == null)
            {
                Start(item, now);
                return;
            }

            if (item.Priority > Current.Priority)
            {
                // A higher-priority race-control event replaces the current card.
                // The interrupted lower-priority card is deliberately not replayed.
                Start(item, now);
                return;
            }

            _waiting.Add(item);
            TrimToBound();
        }

        public OverlayEvent? Tick(DateTimeOffset now)
        {
            RemoveExpired(now);
            if (Current != null
                && (now >= Current.ExpiresAt || now - _currentStartedAt >= Current.DisplayDuration))
            {
                Current = null;
            }

            if (Current == null)
            {
                OverlayEvent? next = _waiting
                    .Where(item => item.ExpiresAt > now)
                    .OrderByDescending(item => item.Priority)
                    .ThenBy(item => item.DetectedAt)
                    .FirstOrDefault();
                if (next != null)
                {
                    _waiting.Remove(next);
                    Start(next, now);
                }
            }

            return Current;
        }

        public void Clear()
        {
            _waiting.Clear();
            Current = null;
            _currentStartedAt = default;
        }

        private void Start(OverlayEvent item, DateTimeOffset now)
        {
            Current = item;
            _currentStartedAt = now;
        }

        private void RemoveExpired(DateTimeOffset now)
        {
            _waiting.RemoveAll(item => item.ExpiresAt <= now);
        }

        private void TrimToBound()
        {
            while (_waiting.Count > MaximumWaitingEvents)
            {
                OverlayEvent remove = _waiting
                    .OrderBy(item => item.Priority)
                    .ThenBy(item => item.DetectedAt)
                    .First();
                _waiting.Remove(remove);
            }
        }
    }
}
