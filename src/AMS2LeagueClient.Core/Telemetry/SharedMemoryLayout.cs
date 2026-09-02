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
        public const int ParticipantWorldPosition = 68;
        public const int ParticipantWorldPositionX = 68;
        public const int ParticipantWorldPositionY = 72;
        public const int ParticipantWorldPositionZ = 76;
        public const int ParticipantCurrentLapDistance = 80;
        public const int ParticipantRacePosition = 84;
        public const int ParticipantLapsCompleted = 88;
        public const int ParticipantCurrentLap = 92;
        public const int ParticipantCurrentSector = 96;

        public const int UnfilteredThrottle = 6428;
        public const int UnfilteredBrake = 6432;
        public const int UnfilteredSteering = 6436;
        public const int UnfilteredClutch = 6440;
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
        public const int SplitTime = 6736;
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
        public const int CarFlags = 6816;
        public const int OilTempCelsius = 6820;
        public const int OilPressureKPa = 6824;
        public const int WaterTempCelsius = 6828;
        public const int WaterPressureKPa = 6832;
        public const int FuelPressureKPa = 6836;
        public const int FuelLevel = 6840;
        public const int FuelCapacity = 6844;
        public const int Speed = 6848;
        public const int Rpm = 6852;
        public const int MaxRpm = 6856;
        public const int Brake = 6860;
        public const int Throttle = 6864;
        public const int Clutch = 6868;
        public const int Steering = 6872;
        public const int Gear = 6876;
        public const int NumGears = 6880;
        public const int OdometerKm = 6884;
        public const int AntiLockActive = 6888;
        public const int LastOpponentCollisionIndex = 6892;
        public const int LastOpponentCollisionMagnitude = 6896;
        public const int BoostActive = 6900;
        public const int BoostAmount = 6904;
        public const int Orientation = 6908;
        public const int LocalVelocity = 6920;
        public const int WorldVelocity = 6932;
        public const int AngularVelocity = 6944;
        public const int LocalAcceleration = 6956;
        public const int WorldAcceleration = 6968;
        public const int ExtentsCentre = 6980;
        public const int TyreFlags = 6992;
        public const int Terrain = 7008;
        public const int TyreY = 7024;
        public const int TyreRps = 7040;
        public const int TyreSlipSpeed = 7056;
        public const int TyreTemp = 7072;
        public const int TyreGrip = 7088;
        public const int TyreHeightAboveGround = 7104;
        public const int TyreLateralStiffness = 7120;
        public const int TyreWear = 7136;
        public const int BrakeDamage = 7152;
        public const int SuspensionDamage = 7168;
        public const int BrakeTempCelsius = 7184;
        public const int TyreTreadTemp = 7200;
        public const int TyreLayerTemp = 7216;
        public const int TyreCarcassTemp = 7232;
        public const int TyreRimTemp = 7248;
        public const int TyreInternalAirTemp = 7264;
        public const int CrashState = 7280;
        public const int AeroDamage = 7284;
        public const int EngineDamage = 7288;
        public const int AmbientTemperature = 7292;
        public const int TrackTemperature = 7296;
        public const int RainDensity = 7300;
        public const int WindSpeed = 7304;
        public const int WindDirectionX = 7308;
        public const int WindDirectionY = 7312;
        public const int CloudBrightness = 7316;
        public const int SequenceNumber = 7320;
        public const int WheelLocalPositionY = 7324;
        public const int SuspensionTravel = 7340;
        public const int SuspensionVelocity = 7356;
        public const int AirPressure = 7372;
        public const int EngineSpeed = 7388;
        public const int EngineTorque = 7392;
        public const int Wings = 7396;
        public const int HandBrake = 7404;

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
        public const int TranslatedTrackLocation = 19252;
        public const int TranslatedTrackVariation = 19316;
        public const int BrakeBias = 19380;
        public const int TurboBoostPressure = 19384;
        public const int TyreCompound = 19388;
        public const int PitSchedules = 19548;
        public const int HighestFlagColours = 19804;
        public const int HighestFlagReasons = 20060;
        public const int Nationalities = 20316;
        public const int SnowDensity = 20572;
        public const int SessionDuration = 20576;
        public const int SessionAdditionalLaps = 20580;
        public const int TyreTempLeft = 20584;
        public const int TyreTempCenter = 20600;
        public const int TyreTempRight = 20616;
        public const int DrsState = 20632;
        public const int RideHeight = 20636;
        public const int JoyPad0 = 20652;
        public const int DPad = 20656;
        public const int AntiLockSetting = 20660;
        public const int TractionControlSetting = 20664;
        public const int ErsDeploymentMode = 20668;
        public const int ErsAutoModeEnabled = 20672;
        public const int ClutchTemp = 20676;
        public const int ClutchWear = 20680;
        public const int ClutchOverheated = 20684;
        public const int ClutchSlipping = 20685;
        public const int YellowFlagState = 20688;
        public const int SessionIsPrivate = 20692;
        public const int LaunchStage = 20696;
        public const int RequiredBytes = 20700;

        public const int VectorLength = 3;
        public const int TyreCount = 4;
        public const int TyreCompoundLength = 40;

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

        public static TelemetryVector3 ReadVector3(byte[] buffer, int offset)
        {
            EnsureRange(buffer, offset, VectorLength * sizeof(float));
            return new TelemetryVector3(
                BitConverter.ToSingle(buffer, offset),
                BitConverter.ToSingle(buffer, offset + sizeof(float)),
                BitConverter.ToSingle(buffer, offset + (2 * sizeof(float))));
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
