using System;

namespace AMS2LeagueClient.Core.Telemetry
{
    public sealed class SharedMemoryParser
    {
        public TelemetryReadResult Parse(byte[] buffer, DateTimeOffset capturedAt, int sequenceRetries = 0)
        {
            if (buffer == null || buffer.Length < SharedMemoryLayout.RequiredBytes)
            {
                return TelemetryReadResult.Failure(TelemetryReadStatus.InvalidData, "Shared Memory buffer is too small.", sequenceRetries);
            }

            uint version = SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.Version);
            if (!SnapshotValidator.IsSupportedVersion(version))
            {
                return TelemetryReadResult.Failure(
                    TelemetryReadStatus.UnsupportedVersion,
                    "Unsupported Shared Memory version " + version + "; expected 14.",
                    sequenceRetries);
            }

            uint gameStateRaw = SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.GameState);
            int rawCount = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.NumParticipants);
            int count = rawCount == -1 && gameStateRaw != (uint)GameState.InGamePlaying ? 0 : rawCount;
            if (!SnapshotValidator.IsParticipantCountValid(count))
            {
                return TelemetryReadResult.Failure(
                    TelemetryReadStatus.InvalidData,
                    "Participant count is outside 0..64 for the current game state: " + rawCount + ".",
                    sequenceRetries);
            }

            var participants = new ParticipantSnapshot[count];
            for (int index = 0; index < count; index++)
            {
                int participantOffset = SharedMemoryLayout.ParticipantOffset(index);
                participants[index] = new ParticipantSnapshot(
                    index,
                    buffer[participantOffset + SharedMemoryLayout.ParticipantIsActive] != 0,
                    SharedMemoryLayout.ReadNullTerminatedAscii(
                        buffer,
                        participantOffset + SharedMemoryLayout.ParticipantName,
                        SharedMemoryLayout.StringLength),
                    SharedMemoryLayout.ReadUInt32(buffer, participantOffset + SharedMemoryLayout.ParticipantRacePosition),
                    SharedMemoryLayout.ReadUInt32(buffer, participantOffset + SharedMemoryLayout.ParticipantLapsCompleted),
                    SharedMemoryLayout.ReadUInt32(buffer, participantOffset + SharedMemoryLayout.ParticipantCurrentLap),
                    SharedMemoryLayout.ReadInt32(buffer, participantOffset + SharedMemoryLayout.ParticipantCurrentSector),
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.RaceStates + (index * sizeof(uint))),
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.PitModes + (index * sizeof(uint))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestLapTimes + (index * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.LastLapTimes + (index * sizeof(float))),
                    SharedMemoryLayout.ReadNullTerminatedAscii(
                        buffer,
                        SharedMemoryLayout.CarNames + (index * SharedMemoryLayout.StringLength),
                        SharedMemoryLayout.StringLength),
                     SharedMemoryLayout.ReadNullTerminatedAscii(
                         buffer,
                         SharedMemoryLayout.CarClassNames + (index * SharedMemoryLayout.StringLength),
                         SharedMemoryLayout.StringLength),
                    SharedMemoryLayout.ReadSingle(buffer, participantOffset + SharedMemoryLayout.ParticipantCurrentLapDistance),
                    buffer[SharedMemoryLayout.LapsInvalidated + index] != 0,
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentSector1Times + (index * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentSector2Times + (index * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentSector3Times + (index * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestSector1Times + (index * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestSector2Times + (index * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestSector3Times + (index * sizeof(float))),
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.PitSchedules + (index * sizeof(uint))),
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.HighestFlagColours + (index * sizeof(uint))),
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.HighestFlagReasons + (index * sizeof(uint))));
            }

            var snapshot = new TelemetrySnapshot(
                capturedAt,
                version,
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.BuildVersion),
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.SequenceNumber),
                gameStateRaw,
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.SessionState),
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.RaceState),
                SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.ViewedParticipantIndex),
                count,
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.LapsInEvent),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.LastLapTime),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.BestLapTime),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.SplitTimeAhead),
                 SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.SplitTimeBehind),
                 participants,
                 SharedMemoryLayout.ReadNullTerminatedAscii(buffer, SharedMemoryLayout.TrackLocation, SharedMemoryLayout.StringLength),
                 SharedMemoryLayout.ReadNullTerminatedAscii(buffer, SharedMemoryLayout.TrackVariation, SharedMemoryLayout.StringLength),
                SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.NumSectors),
                buffer[SharedMemoryLayout.LapInvalidated] != 0,
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentTime),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentSector1Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentSector2Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CurrentSector3Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestSector1Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestSector2Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FastestSector3Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.TrackLength),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.EventTimeRemaining),
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.HighestFlagColour),
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.HighestFlagReason),
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.PitMode),
                SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.PitSchedule),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.SessionDuration),
                SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.SessionAdditionalLaps),
                SharedMemoryLayout.ReadNullTerminatedAscii(buffer, SharedMemoryLayout.CarName, SharedMemoryLayout.StringLength),
                SharedMemoryLayout.ReadNullTerminatedAscii(buffer, SharedMemoryLayout.CarClassName, SharedMemoryLayout.StringLength),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.PersonalFastestLapTime),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WorldFastestLapTime),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.PersonalFastestSector1Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.PersonalFastestSector2Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.PersonalFastestSector3Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WorldFastestSector1Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WorldFastestSector2Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WorldFastestSector3Time),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.AmbientTemperature),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.TrackTemperature),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.RainDensity),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WindSpeed),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WindDirectionX),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WindDirectionY),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.CloudBrightness),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.SnowDensity),
                SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.EnforcedPitStopLap),
                buffer[SharedMemoryLayout.SessionIsPrivate] != 0);

            return TelemetryReadResult.Success(snapshot, sequenceRetries);
        }
    }
}
