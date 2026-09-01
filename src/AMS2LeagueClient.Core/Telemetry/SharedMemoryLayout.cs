using System;
using System.Text;

namespace AMS2LeagueClient.Core.Telemetry
{
    public static class SharedMemoryLayout
    {
        public const string MappingName = "$pcars2$";
        public const uint SupportedVersion = 14;
        public const int MaxParticipants = 64;
        public const int StringLength = 64;

        public const int Version = 0;
        public const int BuildVersion = 4;
        public const int GameState = 8;
        public const int SessionState = 12;
        public const int RaceState = 16;
        public const int ViewedParticipantIndex = 20;
        public const int NumParticipants = 24;

        public const int ParticipantInfo = 28;
        public const int ParticipantSize = 100;
        public const int ParticipantIsActive = 0;
        public const int ParticipantName = 1;
        public const int ParticipantCurrentLapDistance = 80;
        public const int ParticipantRacePosition = 84;
        public const int ParticipantLapsCompleted = 88;
        public const int ParticipantCurrentLap = 92;
        public const int ParticipantCurrentSector = 96;

        public const int CarName = 6444;
        public const int CarClassName = 6508;
        public const int LapsInEvent = 6572;
        public const int TrackLocation = 6576;
        public const int TrackVariation = 6640;
        public const int TrackLength = 6704;
        public const int NumSectors = 6708;
        public const int LapInvalidated = 6712;
        public const int BestLapTime = 6716;
        public const int LastLapTime = 6720;
        public const int CurrentTime = 6724;
        public const int SplitTimeAhead = 6728;
        public const int SplitTimeBehind = 6732;
        public const int EventTimeRemaining = 6740;
        public const int PersonalFastestLapTime = 6744;
        public const int WorldFastestLapTime = 6748;
        public const int CurrentSector1Time = 6752;
        public const int CurrentSector2Time = 6756;
        public const int CurrentSector3Time = 6760;
        public const int FastestSector1Time = 6764;
        public const int FastestSector2Time = 6768;
        public const int FastestSector3Time = 6772;
        public const int PersonalFastestSector1Time = 6776;
        public const int PersonalFastestSector2Time = 6780;
        public const int PersonalFastestSector3Time = 6784;
        public const int WorldFastestSector1Time = 6788;
        public const int WorldFastestSector2Time = 6792;
        public const int WorldFastestSector3Time = 6796;
        public const int HighestFlagColour = 6800;
        public const int HighestFlagReason = 6804;
        public const int PitMode = 6808;
        public const int PitSchedule = 6812;
        public const int AmbientTemperature = 7292;
        public const int TrackTemperature = 7296;
        public const int RainDensity = 7300;
        public const int WindSpeed = 7304;
        public const int WindDirectionX = 7308;
        public const int WindDirectionY = 7312;
        public const int CloudBrightness = 7316;
        public const int SequenceNumber = 7320;

        public const int CurrentSector1Times = 7408;
        public const int CurrentSector2Times = 7664;
        public const int CurrentSector3Times = 7920;
        public const int FastestSector1Times = 8176;
        public const int FastestSector2Times = 8432;
        public const int FastestSector3Times = 8688;
        public const int FastestLapTimes = 8944;
        public const int LastLapTimes = 9200;
        public const int LapsInvalidated = 9456;
        public const int RaceStates = 9520;
        public const int PitModes = 9776;
        public const int Orientations = 10032;
        public const int Speeds = 10800;
        public const int CarNames = 11056;
        public const int CarClassNames = 15152;
        public const int EnforcedPitStopLap = 19248;
        public const int PitSchedules = 19548;
        public const int HighestFlagColours = 19804;
        public const int HighestFlagReasons = 20060;
        public const int SnowDensity = 20572;
        public const int SessionDuration = 20576;
        public const int SessionAdditionalLaps = 20580;
        public const int SessionIsPrivate = 20692;
        public const int RequiredBytes = 20700;

        public static int ParticipantOffset(int index)
        {
            if (index < 0 || index >= MaxParticipants)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ParticipantInfo + (index * ParticipantSize);
        }

        public static uint ReadUInt32(byte[] buffer, int offset)
        {
            EnsureRange(buffer, offset, sizeof(uint));
            return BitConverter.ToUInt32(buffer, offset);
        }

        public static int ReadInt32(byte[] buffer, int offset)
        {
            EnsureRange(buffer, offset, sizeof(int));
            return BitConverter.ToInt32(buffer, offset);
        }

        public static float ReadSingle(byte[] buffer, int offset)
        {
            EnsureRange(buffer, offset, sizeof(float));
            return BitConverter.ToSingle(buffer, offset);
        }

        public static string ReadNullTerminatedAscii(byte[] buffer, int offset, int maxLength)
        {
            EnsureRange(buffer, offset, maxLength);
            int length = 0;
            while (length < maxLength && buffer[offset + length] != 0)
            {
                length++;
            }

            return Encoding.UTF8.GetString(buffer, offset, length).Trim();
        }

        private static void EnsureRange(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset > buffer.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }
    }
}
