using System;
using System.Collections.Generic;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Session
{
    public sealed class LeagueParticipant
    {
        public LeagueParticipant(ParticipantSnapshot source, uint leaguePosition, ParticipantRole role)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            LeaguePosition = leaguePosition;
            Role = role;
        }

        public ParticipantSnapshot Source { get; }
        public uint LeaguePosition { get; }
        public ParticipantRole Role { get; }
    }

    public sealed class LeagueClassification
    {
        public LeagueClassification(
            int rawParticipantCount,
            IReadOnlyList<LeagueParticipant> participants,
            int safetyCarsExcluded,
            LeagueParticipant? local,
            LeagueParticipant? ahead,
            LeagueParticipant? behind,
            ParticipantSnapshot? rawAhead,
            ParticipantSnapshot? rawBehind,
            bool canUseAheadGameSplit,
            bool canUseBehindGameSplit)
        {
            RawParticipantCount = rawParticipantCount;
            Participants = participants;
            SafetyCarsExcluded = safetyCarsExcluded;
            Local = local;
            Ahead = ahead;
            Behind = behind;
            RawAhead = rawAhead;
            RawBehind = rawBehind;
            CanUseAheadGameSplit = canUseAheadGameSplit;
            CanUseBehindGameSplit = canUseBehindGameSplit;
        }

        public int RawParticipantCount { get; }
        public int LeagueParticipantCount => Participants.Count;
        public IReadOnlyList<LeagueParticipant> Participants { get; }
        public int SafetyCarsExcluded { get; }
        public LeagueParticipant? Local { get; }
        public LeagueParticipant? Ahead { get; }
        public LeagueParticipant? Behind { get; }
        public ParticipantSnapshot? RawAhead { get; }
        public ParticipantSnapshot? RawBehind { get; }
        public bool CanUseAheadGameSplit { get; }
        public bool CanUseBehindGameSplit { get; }
        public bool IsLocalEligible => Local != null;

        public LeagueParticipant? FastestLapParticipant
            => Participants
                .Where(item => IsPositiveFinite(item.Source.BestLapTime))
                .OrderBy(item => item.Source.BestLapTime)
                .ThenBy(item => item.LeaguePosition)
                .FirstOrDefault();

        private static bool IsPositiveFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
    }

    public sealed class LeagueClassificationResolver
    {
        private readonly ParticipantRoleClassifier _roles;

        public LeagueClassificationResolver(ParticipantRoleClassifier? roles = null)
        {
            _roles = roles ?? new ParticipantRoleClassifier();
        }

        public LeagueClassification Resolve(TelemetrySnapshot snapshot, ParticipantSnapshot local)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (local == null) throw new ArgumentNullException(nameof(local));

            List<ParticipantSnapshot> rawOrder = snapshot.Participants
                .Where(item => item.IsActive && item.RacePosition > 0)
                .OrderBy(item => item.RacePosition)
                .ThenBy(item => item.Index)
                .ToList();

            int excluded = rawOrder.Count(item => _roles.Classify(item) == ParticipantRole.SafetyCar);
            var leagueOrder = new List<LeagueParticipant>(Math.Max(0, rawOrder.Count - excluded));
            foreach (ParticipantSnapshot participant in rawOrder)
            {
                ParticipantRole role = _roles.Classify(participant);
                if (role == ParticipantRole.SafetyCar)
                {
                    continue;
                }

                leagueOrder.Add(new LeagueParticipant(participant, (uint)leagueOrder.Count + 1, role));
            }

            LeagueParticipant? leagueLocal = leagueOrder.FirstOrDefault(item => item.Source.Index == local.Index);
            List<LeagueParticipant> competitive = leagueOrder.Where(item => IsEligibleRelative(item.Source) || item.Source.Index == local.Index).ToList();
            int localOffset = competitive.FindIndex(item => item.Source.Index == local.Index);
            LeagueParticipant? ahead = localOffset > 0 ? competitive[localOffset - 1] : null;
            LeagueParticipant? behind = localOffset >= 0 && localOffset + 1 < competitive.Count ? competitive[localOffset + 1] : null;

            ParticipantSnapshot? rawAhead = FindRawAdjacent(rawOrder, local, local.RacePosition > 1 ? local.RacePosition - 1 : 0);
            ParticipantSnapshot? rawBehind = FindRawAdjacent(rawOrder, local, local.RacePosition + 1);
            bool aheadMatches = ahead != null && rawAhead != null && ahead.Source.Index == rawAhead.Index;
            bool behindMatches = behind != null && rawBehind != null && behind.Source.Index == rawBehind.Index;

            return new LeagueClassification(
                snapshot.NumParticipants,
                leagueOrder,
                excluded,
                leagueLocal,
                ahead,
                behind,
                rawAhead,
                rawBehind,
                aheadMatches,
                behindMatches);
        }

        private static ParticipantSnapshot? FindRawAdjacent(
            IEnumerable<ParticipantSnapshot> rawOrder,
            ParticipantSnapshot local,
            uint position)
        {
            if (position == 0) return null;
            return rawOrder.FirstOrDefault(item => item.Index != local.Index
                && item.RacePosition == position
                && IsEligibleRelative(item));
        }

        private static bool IsEligibleRelative(ParticipantSnapshot participant)
        {
            if (!participant.IsActive || participant.RacePosition == 0) return false;
            RaceState? state = participant.KnownRaceState;
            return state != RaceState.Disqualified
                && state != RaceState.Retired
                && state != RaceState.Dnf
                && state != RaceState.Finished;
        }
    }
}
