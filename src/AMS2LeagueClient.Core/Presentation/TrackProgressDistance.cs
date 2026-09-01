using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public enum RelativeDistanceTrend
    {
        None,
        Increasing,
        Decreasing
    }

    /// <summary>
    /// Keeps the last displayed whole-metre distance for the same physical car.
    /// Retaining the last non-neutral trend avoids 20 Hz arrow flicker while the
    /// rounded distance remains unchanged.
    /// </summary>
    public sealed class RelativeDistanceTrendTracker
    {
        private readonly TrendState _ahead = new TrendState();
        private readonly TrendState _behind = new TrendState();
        private int _sessionGeneration = int.MinValue;

        public void Apply(OverlayViewModel viewModel, int sessionGeneration)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (_sessionGeneration != sessionGeneration)
            {
                _sessionGeneration = sessionGeneration;
                _ahead.Reset();
                _behind.Reset();
            }

            RelativeDistanceTrend ahead = _ahead.Observe(viewModel.AheadParticipantIndex, viewModel.AheadDistanceMeters);
            RelativeDistanceTrend behind = _behind.Observe(viewModel.BehindParticipantIndex, viewModel.BehindDistanceMeters);
            ApplyVisual(ahead, out string aheadArrow, out string aheadColor);
            ApplyVisual(behind, out string behindArrow, out string behindColor);
            viewModel.AheadDistanceTrendArrow = aheadArrow;
            viewModel.AheadDistanceColor = aheadColor;
            viewModel.BehindDistanceTrendArrow = behindArrow;
            viewModel.BehindDistanceColor = behindColor;
        }

        public void Reset()
        {
            _sessionGeneration = int.MinValue;
            _ahead.Reset();
            _behind.Reset();
        }

        private static void ApplyVisual(RelativeDistanceTrend trend, out string arrow, out string color)
        {
            switch (trend)
            {
                case RelativeDistanceTrend.Increasing:
                    arrow = "▲";
                    color = "#57D5FF";
                    return;
                case RelativeDistanceTrend.Decreasing:
                    arrow = "▼";
                    color = "#FF7777";
                    return;
                default:
                    arrow = string.Empty;
                    color = "#F1F5F9";
                    return;
            }
        }

        private sealed class TrendState
        {
            private int _participantIndex = -1;
            private int? _meters;
            private RelativeDistanceTrend _trend;

            public RelativeDistanceTrend Observe(int participantIndex, int? meters)
            {
                if (participantIndex < 0 || !meters.HasValue)
                {
                    Reset();
                    return RelativeDistanceTrend.None;
                }
                if (_participantIndex != participantIndex || !_meters.HasValue)
                {
                    _participantIndex = participantIndex;
                    _meters = meters;
                    _trend = RelativeDistanceTrend.None;
                    return _trend;
                }

                if (meters.Value > _meters.Value) _trend = RelativeDistanceTrend.Increasing;
                else if (meters.Value < _meters.Value) _trend = RelativeDistanceTrend.Decreasing;
                _meters = meters;
                return _trend;
            }

            public void Reset()
            {
                _participantIndex = -1;
                _meters = null;
                _trend = RelativeDistanceTrend.None;
            }
        }
    }

    public sealed class TrackProgressDistance
    {
        private TrackProgressDistance(bool isAvailable, double signedMeters, string text)
        {
            IsAvailable = isAvailable;
            SignedMeters = signedMeters;
            Text = text;
        }

        public bool IsAvailable { get; }
        public double SignedMeters { get; }
        public string Text { get; }

        public static TrackProgressDistance Unknown()
            => new TrackProgressDistance(false, 0, "—");

        public static TrackProgressDistance FromMeters(double signedMeters, float trackLength)
        {
            double absoluteMeters = Math.Abs(signedMeters);
            if (absoluteMeters >= trackLength)
            {
                int laps = Math.Max(1, (int)Math.Floor((absoluteMeters + 0.5) / trackLength));
                return new TrackProgressDistance(true, signedMeters, "+" + laps.ToString(CultureInfo.InvariantCulture) + " LAP");
            }

            return new TrackProgressDistance(
                true,
                signedMeters,
                Math.Round(absoluteMeters, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "m");
        }
    }

    public sealed class TrackProgressDistanceResolver
    {
        public TrackProgressDistance Resolve(float trackLength, ParticipantSnapshot local, ParticipantSnapshot? opponent)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            if (opponent == null || !IsFinite(trackLength) || trackLength <= 0)
            {
                return TrackProgressDistance.Unknown();
            }

            if (!IsValidLapDistance(local.CurrentLapDistance, trackLength)
                || !IsValidLapDistance(opponent.CurrentLapDistance, trackLength)
                || local.LapsCompleted > 10000
                || opponent.LapsCompleted > 10000)
            {
                return TrackProgressDistance.Unknown();
            }

            long lapDelta = (long)opponent.LapsCompleted - local.LapsCompleted;
            double signedMeters = lapDelta * (double)trackLength
                + opponent.CurrentLapDistance
                - local.CurrentLapDistance;
            if (double.IsNaN(signedMeters) || double.IsInfinity(signedMeters))
            {
                return TrackProgressDistance.Unknown();
            }

            return TrackProgressDistance.FromMeters(signedMeters, trackLength);
        }

        private static bool IsValidLapDistance(float value, float trackLength)
            => IsFinite(value) && value >= 0 && value <= trackLength * 1.05f;

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class TrackProximity
    {
        public TrackProximity(
            ParticipantSnapshot? ahead,
            TrackProgressDistance aheadDistance,
            ParticipantSnapshot? behind,
            TrackProgressDistance behindDistance)
        {
            Ahead = ahead;
            AheadDistance = aheadDistance;
            Behind = behind;
            BehindDistance = behindDistance;
        }

        public ParticipantSnapshot? Ahead { get; }
        public TrackProgressDistance AheadDistance { get; }
        public ParticipantSnapshot? Behind { get; }
        public TrackProgressDistance BehindDistance { get; }
    }

    public sealed class TrackProximityResolver
    {
        public TrackProximity Resolve(
            float trackLength,
            ParticipantSnapshot local,
            IEnumerable<ParticipantSnapshot> participants)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            if (!IsValidLapDistance(local.CurrentLapDistance, trackLength))
            {
                return Unknown();
            }

            ProximityCandidate[] candidates = participants
                .Where(item => item.IsActive && item.Index != local.Index)
                .Where(item => IsValidLapDistance(item.CurrentLapDistance, trackLength))
                .Select(item => new ProximityCandidate(
                    item,
                    ForwardDistance(local.CurrentLapDistance, item.CurrentLapDistance, trackLength),
                    ForwardDistance(item.CurrentLapDistance, local.CurrentLapDistance, trackLength)))
                .ToArray();

            if (candidates.Length == 0)
            {
                return Unknown();
            }

            ProximityCandidate ahead = candidates
                .OrderBy(item => item.ForwardMeters)
                .ThenBy(item => item.Participant.Index)
                .First();
            ProximityCandidate behind = candidates
                .OrderBy(item => item.BehindMeters)
                .ThenBy(item => item.Participant.Index)
                .First();

            return new TrackProximity(
                ahead.Participant,
                TrackProgressDistance.FromMeters(ahead.ForwardMeters, trackLength),
                behind.Participant,
                TrackProgressDistance.FromMeters(-behind.BehindMeters, trackLength));
        }

        private static TrackProximity Unknown()
            => new TrackProximity(
                null,
                TrackProgressDistance.Unknown(),
                null,
                TrackProgressDistance.Unknown());

        private static double ForwardDistance(float from, float to, float trackLength)
        {
            double distance = (to - from) % trackLength;
            if (distance < 0) distance += trackLength;
            return distance;
        }

        private static bool IsValidLapDistance(float value, float trackLength)
            => IsFinite(trackLength)
                && trackLength > 0
                && IsFinite(value)
                && value >= 0
                && value <= trackLength * 1.05f;

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class ProximityCandidate
        {
            public ProximityCandidate(ParticipantSnapshot participant, double forwardMeters, double behindMeters)
            {
                Participant = participant;
                ForwardMeters = forwardMeters;
                BehindMeters = behindMeters;
            }

            public ParticipantSnapshot Participant { get; }
            public double ForwardMeters { get; }
            public double BehindMeters { get; }
        }
    }
}
