using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Session
{
    public sealed class LocalParticipantResolution
    {
        private LocalParticipantResolution(bool isValid, ParticipantSnapshot? participant, string reason)
        {
            IsValid = isValid;
            Participant = participant;
            Reason = reason;
        }

        public bool IsValid { get; }
        public ParticipantSnapshot? Participant { get; }
        public string Reason { get; }

        public static LocalParticipantResolution Valid(ParticipantSnapshot participant)
            => new LocalParticipantResolution(true, participant, string.Empty);

        public static LocalParticipantResolution Invalid(string reason)
            => new LocalParticipantResolution(false, null, reason);
    }

    public sealed class LocalParticipantResolver
    {
        public LocalParticipantResolution Resolve(TelemetrySnapshot snapshot)
        {
            if (snapshot.KnownGameState != GameState.InGamePlaying)
            {
                return LocalParticipantResolution.Invalid(
                    "Local HUD is enabled only for GAME_INGAME_PLAYING; replay, menus, pause and restart are hidden.");
            }

            int viewedIndex = snapshot.ViewedParticipantIndex;
            if (viewedIndex < 0 || viewedIndex >= snapshot.NumParticipants || viewedIndex >= snapshot.Participants.Count)
            {
                return LocalParticipantResolution.Invalid("Viewed participant index is outside the active participant range.");
            }

            ParticipantSnapshot participant = snapshot.Participants[viewedIndex];
            if (!participant.IsActive)
            {
                return LocalParticipantResolution.Invalid("Viewed participant is inactive.");
            }

            if (participant.RacePosition == 0)
            {
                return LocalParticipantResolution.Invalid("Viewed participant has no valid race position.");
            }

            return LocalParticipantResolution.Valid(participant);
        }
    }
}
