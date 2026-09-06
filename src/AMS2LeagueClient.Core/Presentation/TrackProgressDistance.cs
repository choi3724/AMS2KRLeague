using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Session;
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
        private readonly LapGapState _aheadLap = new LapGapState();
        private readonly LapGapState _behindLap = new LapGapState();
        private int _sessionGeneration = int.MinValue;

        public void Apply(OverlayViewModel viewModel, int sessionGeneration)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));
            if (_sessionGeneration != sessionGeneration)
            {
                _sessionGeneration = sessionGeneration;
                _ahead.Reset();
                _behind.Reset();
                _aheadLap.Reset();
                _behindLap.Reset();
            }

            RelativeDistanceTrend ahead = _ahead.Observe(viewModel.AheadParticipantIndex, viewModel.AheadDistanceMeters,
                viewModel.AheadDistance.StartsWith("~", StringComparison.Ordinal));
            RelativeDistanceTrend behind = _behind.Observe(viewModel.BehindParticipantIndex, viewModel.BehindDistanceMeters,
                viewModel.BehindDistance.StartsWith("~", StringComparison.Ordinal));
            ApplyVisual(ahead, true, out string aheadArrow, out string aheadColor);
            ApplyVisual(behind, false, out string behindArrow, out string behindColor);
            viewModel.AheadDistanceTrendArrow = aheadArrow;
            viewModel.AheadDistanceColor = aheadColor;
            viewModel.BehindDistanceTrendArrow = behindArrow;
            viewModel.BehindDistanceColor = behindColor;
            int aheadLaps = _aheadLap.Observe(viewModel.AheadParticipantKey, viewModel.AheadLapGapCandidate);
            int behindLaps = _behindLap.Observe(viewModel.BehindParticipantKey, viewModel.BehindLapGapCandidate);
            // Lap difference is independent of the game-provided time gap.
            viewModel.AheadLapGap = aheadLaps > 0 ? "LAP " + aheadLaps.ToString(CultureInfo.InvariantCulture) : string.Empty;
            viewModel.BehindLapGap = behindLaps > 0 ? "LAP " + behindLaps.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        public void Reset()
        {
            _sessionGeneration = int.MinValue;
            _ahead.Reset();
            _behind.Reset();
            _aheadLap.Reset();
            _behindLap.Reset();
        }

        private static void ApplyVisual(RelativeDistanceTrend trend, bool ahead, out string arrow, out string color)
        {
            switch (trend)
            {
                case RelativeDistanceTrend.Increasing:
                    arrow = "▲";
                    color = ahead ? "#FF7777" : "#57D5FF";
                    return;
                case RelativeDistanceTrend.Decreasing:
                    arrow = "▼";
                    color = ahead ? "#57D5FF" : "#FF7777";
                    return;
                default:
                    arrow = string.Empty;
                    color = "#F1F5F9";
                    return;
            }
        }

        private sealed class TrendState
        {
            private const int DeadbandMeters = 2;
            private int _participantIndex = -1;
            private int? _meters;
            private RelativeDistanceTrend _trend;
            private bool _worldDistance;

            public RelativeDistanceTrend Observe(int participantIndex, int? meters, bool worldDistance)
            {
                if (participantIndex < 0 || !meters.HasValue)
                {
                    Reset();
                    return RelativeDistanceTrend.None;
                }
                if (_participantIndex != participantIndex || !_meters.HasValue || _worldDistance != worldDistance)
                {
                    _participantIndex = participantIndex;
                    _meters = meters;
                    _worldDistance = worldDistance;
                    _trend = RelativeDistanceTrend.None;
                    return _trend;
                }

                int delta = meters.Value - _meters.Value;
                if (Math.Abs(delta) <= DeadbandMeters) return _trend;
                _trend = delta > 0 ? RelativeDistanceTrend.Increasing : RelativeDistanceTrend.Decreasing;
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

        private sealed class LapGapState
        {
            private const int RequiredSnapshots = 2;
            private string _participantKey = string.Empty;
            private int _confirmed;
            private int _candidate;
            private int _candidateCount;

            public int Observe(string participantKey, int? laps)
            {
                if (string.IsNullOrEmpty(participantKey) || !laps.HasValue)
                {
                    Reset();
                    return 0;
                }
                if (!string.Equals(_participantKey, participantKey, StringComparison.Ordinal))
                {
                    Reset();
                    _participantKey = participantKey;
                }

                int observed = Math.Max(0, laps.Value);
                if (observed == _confirmed)
                {
                    _candidateCount = 0;
                    return _confirmed;
                }
                if (observed != _candidate)
                {
                    _candidate = observed;
                    _candidateCount = 1;
                    return _confirmed;
                }
                if (++_candidateCount >= RequiredSnapshots)
                {
                    _confirmed = observed;
                    _candidateCount = 0;
                }
                return _confirmed;
            }

            public void Reset()
            {
                _participantKey = string.Empty;
                _confirmed = 0;
                _candidate = 0;
                _candidateCount = 0;
            }
        }
    }

    public sealed class TrackProgressDistance
    {
        private TrackProgressDistance(bool isAvailable, double signedMeters, string text, int? lapGap)
        {
            IsAvailable = isAvailable;
            SignedMeters = signedMeters;
            Text = text;
            LapGap = lapGap;
        }

        public bool IsAvailable { get; }
        public double SignedMeters { get; }
        public string Text { get; }
        public int? LapGap { get; }

        public static TrackProgressDistance Unknown()
            => new TrackProgressDistance(false, 0, "—", null);

        // World-space proximity is approximate, not cumulative track/lap progress.
        public static TrackProgressDistance FromWorldMeters(double signedMeters)
            => new TrackProgressDistance(true, signedMeters,
                "~" + Math.Round(Math.Abs(signedMeters), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "m", null);

        public static TrackProgressDistance FromMeters(double signedMeters, float trackLength)
        {
            double absoluteMeters = Math.Abs(signedMeters);
            if (absoluteMeters >= trackLength)
            {
                int laps = Math.Max(1, (int)Math.Floor(absoluteMeters / trackLength));
                return new TrackProgressDistance(true, signedMeters, "LAP " + laps.ToString(CultureInfo.InvariantCulture), laps);
            }

            return new TrackProgressDistance(
                true,
                signedMeters,
                Math.Round(absoluteMeters, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "m",
                0);
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
            => IsFinite(value) && value >= 0 && value <= trackLength;

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
        private const double GridRangeMeters = 150;
        private readonly ParticipantRoleClassifier _roles = new ParticipantRoleClassifier();

        public TrackProximity Resolve(
            float trackLength,
            ParticipantSnapshot local,
            IEnumerable<ParticipantSnapshot> participants)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            // PitMode is observed location/state; a pit request (PitSchedule) is not.
            if (!local.KnownPitMode.HasValue) return Unknown();
            bool inPit = local.KnownPitMode != PitMode.None;
            ParticipantSnapshot[] opponents = participants
                .Where(item => item.IsActive && item.Index != local.Index && _roles.IsLeagueDriver(item) && item.KnownPitMode.HasValue
                    && (item.KnownPitMode != PitMode.None) == inPit)
                .ToArray();
            // Pit/garage lap progress may be unset, retained or projected onto the
            // main circuit. Never use that progress to select a car on the parallel track.
            if (inPit) return ResolveWorldProximity(local, opponents, inPit: true);
            // AMS2 defines lap distance 0 as UNSET. The grid can stay at zero until
            // each car crosses the line; classification order is not physical proximity.
            if (local.LapsCompleted == 0 && local.CurrentLap <= 1
                && (HasUnstartedProgress(local, trackLength)
                    || (local.CurrentLapDistance <= GridRangeMeters && opponents.Any(item => HasUnstartedProgress(item, trackLength)))))
            {
                return ResolveWorldProximity(local, opponents, inPit: false);
            }
            if (!IsValidLapDistance(local.CurrentLapDistance, trackLength))
            {
                return Unknown();
            }

            ProximityCandidate[] candidates = opponents
                .Where(item => IsValidLapDistance(item.CurrentLapDistance, trackLength) && !HasUnstartedProgress(item, trackLength))
                .Select(item => new ProximityCandidate(
                    item,
                    ForwardDistance(local.CurrentLapDistance, item.CurrentLapDistance, trackLength),
                    ForwardDistance(item.CurrentLapDistance, local.CurrentLapDistance, trackLength)))
                .Where(item => item.ForwardMeters > 0.01 && item.BehindMeters > 0.01)
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

        private static bool HasUnstartedProgress(ParticipantSnapshot participant, float trackLength)
            => participant.LapsCompleted == 0 && participant.CurrentLap <= 1
                && (!IsValidLapDistance(participant.CurrentLapDistance, trackLength) || participant.CurrentLapDistance <= 0.01f);

        private static TrackProximity ResolveWorldProximity(ParticipantSnapshot local, ParticipantSnapshot[] opponents, bool inPit)
        {
            if (!HasWorldPose(local)) return Unknown();
            // AMS2 yaw 0 faces -Z; measured moving SHM samples confirm (-sin(yaw), -cos(yaw)).
            double forwardX = -Math.Sin(local.Orientation.Y);
            double forwardZ = -Math.Cos(local.Orientation.Y);
            ParticipantSnapshot? ahead = null, behind = null;
            double aheadMeters = double.PositiveInfinity, behindMeters = double.PositiveInfinity;
            foreach (ParticipantSnapshot item in opponents)
            {
                if (!HasWorldPose(item) || (item.KnownRaceState != RaceState.NotStarted && item.KnownRaceState != RaceState.Racing)) continue;
                double x = (double)item.WorldPosition.X - local.WorldPosition.X;
                double y = (double)item.WorldPosition.Y - local.WorldPosition.Y;
                double z = (double)item.WorldPosition.Z - local.WorldPosition.Z;
                double along = x * forwardX + z * forwardZ;
                double across = x * forwardZ - z * forwardX;
                double meters = Math.Sqrt(x * x + y * y + z * z);
                // ponytail: nearby world corridor, not a pit/track topology solver.
                // Missing/ambiguous geometry stays unknown; never fall back across areas.
                // Garage cars can face out of their boxes rather than along the lane.
                if (meters > GridRangeMeters || Math.Abs(across) > 15 || Math.Abs(y) > 6
                    || Math.Abs(along) <= 0.5
                    || (!inPit && Math.Cos(item.Orientation.Y - local.Orientation.Y) < 0.5)) continue;
                if (along > 0 && meters < aheadMeters)
                {
                    ahead = item;
                    aheadMeters = meters;
                }
                if (along < 0 && meters < behindMeters)
                {
                    behind = item;
                    behindMeters = meters;
                }
            }
            return new TrackProximity(
                ahead, ahead == null ? TrackProgressDistance.Unknown() : TrackProgressDistance.FromWorldMeters(aheadMeters),
                behind, behind == null ? TrackProgressDistance.Unknown() : TrackProgressDistance.FromWorldMeters(-behindMeters));
        }

        private static bool HasWorldPose(ParticipantSnapshot participant)
            => IsFinite(participant.WorldPosition.X) && IsFinite(participant.WorldPosition.Y) && IsFinite(participant.WorldPosition.Z)
                && IsFinite(participant.Orientation.Y)
                && (participant.WorldPosition.X != 0 || participant.WorldPosition.Y != 0 || participant.WorldPosition.Z != 0);

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
                && value <= trackLength;

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
