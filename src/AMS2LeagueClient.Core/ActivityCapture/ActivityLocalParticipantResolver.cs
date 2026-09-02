using System;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.ActivityCapture
{
    public sealed class ActivityLocalParticipantResolution
    {
        private ActivityLocalParticipantResolution(bool isValid, ParticipantSnapshot? participant, string reason)
        {
            IsValid = isValid;
            Participant = participant;
            Reason = reason ?? string.Empty;
        }

        public bool IsValid { get; }
        public ParticipantSnapshot? Participant { get; }
        public string Reason { get; }

        public static ActivityLocalParticipantResolution Valid(ParticipantSnapshot participant)
            => new ActivityLocalParticipantResolution(
                true,
                participant ?? throw new ArgumentNullException(nameof(participant)),
                string.Empty);

        public static ActivityLocalParticipantResolution Invalid(string reason)
            => new ActivityLocalParticipantResolution(false, null, reason);
    }

    /// <summary>
    /// Resolves the active viewed participant for activity recording without
    /// applying HUD classification rules. This is not authoritative proof that
    /// the viewed participant belongs to the local installation: SHM v14 exposes
    /// no local-owner or spectator flag. Callers handling private data must apply
    /// a separate ownership/fail-closed policy. Time Attack can legitimately
    /// expose race position zero while still providing an active participant.
    /// </summary>
    public sealed class ActivityLocalParticipantResolver
    {
        public ActivityLocalParticipantResolution Resolve(TelemetrySnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.KnownGameState != GameState.InGamePlaying)
            {
                return ActivityLocalParticipantResolution.Invalid(
                    "Activity capture requires GAME_INGAME_PLAYING; replay, menus, pause and restart are not local driving evidence.");
            }

            int viewedIndex = snapshot.ViewedParticipantIndex;
            if (viewedIndex < 0
                || viewedIndex >= snapshot.NumParticipants
                || viewedIndex >= snapshot.Participants.Count)
            {
                return ActivityLocalParticipantResolution.Invalid(
                    "Viewed participant index is outside the current participant range.");
            }

            ParticipantSnapshot participant = snapshot.Participants[viewedIndex];
            if (!participant.IsActive)
            {
                return ActivityLocalParticipantResolution.Invalid("Viewed participant is inactive.");
            }

            return ActivityLocalParticipantResolution.Valid(participant);
        }
    }
}
