using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Session
{
    public sealed class RelativeDrivers
    {
        public RelativeDrivers(ParticipantSnapshot? ahead, ParticipantSnapshot? behind)
        {
            Ahead = ahead;
            Behind = behind;
        }

        public ParticipantSnapshot? Ahead { get; }
        public ParticipantSnapshot? Behind { get; }
    }

    public sealed class RelativeDriverResolver
    {
        public RelativeDrivers Resolve(TelemetrySnapshot snapshot, ParticipantSnapshot local)
        {
            ParticipantSnapshot? ahead = null;
            ParticipantSnapshot? behind = null;

            uint aheadPosition = local.RacePosition > 1 ? local.RacePosition - 1 : 0;
            uint behindPosition = local.RacePosition + 1;

            foreach (ParticipantSnapshot candidate in snapshot.Participants)
            {
                if (!IsEligible(candidate) || candidate.Index == local.Index)
                {
                    continue;
                }

                if (candidate.RacePosition == aheadPosition && ahead == null)
                {
                    ahead = candidate;
                }
                else if (candidate.RacePosition == behindPosition && behind == null)
                {
                    behind = candidate;
                }
            }

            return new RelativeDrivers(ahead, behind);
        }

        private static bool IsEligible(ParticipantSnapshot participant)
        {
            if (!participant.IsActive || participant.RacePosition == 0)
            {
                return false;
            }

            RaceState? state = participant.KnownRaceState;
            return state != RaceState.Disqualified
                && state != RaceState.Retired
                && state != RaceState.Dnf
                && state != RaceState.Finished;
        }
    }
}
