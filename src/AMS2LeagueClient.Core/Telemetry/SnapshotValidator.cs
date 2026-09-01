namespace AMS2LeagueClient.Core.Telemetry
{
    public static class SnapshotValidator
    {
        public static bool IsConsistent(uint sequenceBefore, uint copiedSequence, uint sequenceAfter)
        {
            return (sequenceBefore & 1U) == 0U
                && sequenceBefore == copiedSequence
                && sequenceBefore == sequenceAfter;
        }

        public static bool IsSupportedVersion(uint version)
        {
            return version == SharedMemoryLayout.SupportedVersion;
        }

        public static bool IsParticipantCountValid(int participantCount)
        {
            return participantCount >= 0 && participantCount <= SharedMemoryLayout.MaxParticipants;
        }
    }
}
