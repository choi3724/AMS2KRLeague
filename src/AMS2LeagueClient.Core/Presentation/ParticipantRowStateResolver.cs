using System;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public enum ParticipantRowDisplayState
    {
        Active,
        Finalized,
        TerminalInactive,
        Disconnected
    }

    public static class ParticipantRowStateResolver
    {
        public static ParticipantRowDisplayState Resolve(ParticipantSnapshot participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (!participant.IsActive) return ParticipantRowDisplayState.Disconnected;
            switch (participant.KnownRaceState)
            {
                case RaceState.Disqualified:
                case RaceState.Retired:
                case RaceState.Dnf:
                    return ParticipantRowDisplayState.TerminalInactive;
                case RaceState.Finished:
                    return ParticipantRowDisplayState.Finalized;
                default:
                    // Pit state and temporary timing gaps are still active.
                    return ParticipantRowDisplayState.Active;
            }
        }

        public static bool ShouldDim(ParticipantRowDisplayState state)
            => state == ParticipantRowDisplayState.TerminalInactive
                || state == ParticipantRowDisplayState.Disconnected;
    }
}
