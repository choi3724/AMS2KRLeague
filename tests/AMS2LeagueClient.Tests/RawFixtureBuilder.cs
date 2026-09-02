using System;
using System.Text;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Tests
{
    public sealed class RawFixtureBuilder
    {
        public RawFixtureBuilder(int participantCount = 12)
        {
            Buffer = new byte[SharedMemoryLayout.RequiredBytes];
            WriteUInt(SharedMemoryLayout.Version, SharedMemoryLayout.SupportedVersion);
            WriteUInt(SharedMemoryLayout.BuildVersion, 24132163);
            WriteUInt(SharedMemoryLayout.GameState, (uint)GameState.InGamePlaying);
            WriteUInt(SharedMemoryLayout.SessionState, (uint)SessionState.Race);
            WriteUInt(SharedMemoryLayout.RaceState, (uint)RaceState.Racing);
            WriteUInt(SharedMemoryLayout.HighestFlagColour, (uint)FlagColour.None);
            WriteUInt(SharedMemoryLayout.HighestFlagReason, (uint)FlagReason.None);
            WriteUInt(SharedMemoryLayout.PitMode, (uint)PitMode.None);
            WriteUInt(SharedMemoryLayout.PitSchedule, (uint)PitSchedule.None);
            WriteInt(SharedMemoryLayout.YellowFlagState, (int)YellowFlagState.None);
            WriteInt(SharedMemoryLayout.ViewedParticipantIndex, 3);
            WriteInt(SharedMemoryLayout.NumParticipants, participantCount);
            WriteUInt(SharedMemoryLayout.LapsInEvent, 10);
            WriteString(SharedMemoryLayout.CarName, "Formula Trainer", SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.CarClassName, "F-Trainer", SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.TrackLocation, "Interlagos", SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.TrackVariation, "GP", SharedMemoryLayout.StringLength);
            WriteFloat(SharedMemoryLayout.LastLapTime, 138.421f);
            WriteFloat(SharedMemoryLayout.BestLapTime, 137.881f);
            WriteInt(SharedMemoryLayout.NumSectors, 3);
            WriteFloat(SharedMemoryLayout.CurrentTime, 54.321f);
            WriteFloat(SharedMemoryLayout.CurrentSector1Time, 31.125f);
            WriteFloat(SharedMemoryLayout.CurrentSector2Time, -1.0f);
            WriteFloat(SharedMemoryLayout.CurrentSector3Time, -1.0f);
            WriteFloat(SharedMemoryLayout.SplitTimeAhead, 1.214f);
            WriteFloat(SharedMemoryLayout.SplitTimeBehind, 0.873f);
            WriteUInt(SharedMemoryLayout.SequenceNumber, 200);

            for (int index = 0; index < participantCount; index++)
            {
                SetParticipant(index, true, "DRIVER_" + index, (uint)(index + 1), 2, 3, RaceState.Racing, PitMode.None);
            }

            if (participantCount >= 8)
            {
                SetParticipant(3, true, "LEE", 4, 2, 3, RaceState.Racing, PitMode.None);
                SetParticipant(7, true, "KIM", 3, 2, 3, RaceState.Racing, PitMode.None);
                SetParticipant(2, true, "PARK", 5, 2, 3, RaceState.Racing, PitMode.None);
            }
        }

        public byte[] Buffer { get; }

        public RawFixtureBuilder SetVersion(uint version)
        {
            WriteUInt(SharedMemoryLayout.Version, version);
            return this;
        }

        public RawFixtureBuilder SetBuildVersion(uint version)
        {
            WriteUInt(SharedMemoryLayout.BuildVersion, version);
            return this;
        }

        public RawFixtureBuilder SetParticipantCount(int count)
        {
            WriteInt(SharedMemoryLayout.NumParticipants, count);
            return this;
        }

        public RawFixtureBuilder SetViewedIndex(int index)
        {
            WriteInt(SharedMemoryLayout.ViewedParticipantIndex, index);
            return this;
        }

        public RawFixtureBuilder SetSession(SessionState state)
        {
            WriteUInt(SharedMemoryLayout.SessionState, (uint)state);
            return this;
        }

        public RawFixtureBuilder SetGameState(GameState state)
        {
            WriteUInt(SharedMemoryLayout.GameState, (uint)state);
            return this;
        }

        public RawFixtureBuilder SetSplitAhead(float value)
        {
            WriteFloat(SharedMemoryLayout.SplitTimeAhead, value);
            return this;
        }

        public RawFixtureBuilder SetSplitBehind(float value)
        {
            WriteFloat(SharedMemoryLayout.SplitTimeBehind, value);
            return this;
        }

        public RawFixtureBuilder SetSequence(uint value)
        {
            WriteUInt(SharedMemoryLayout.SequenceNumber, value);
            return this;
        }

        public RawFixtureBuilder SetLapsInEvent(uint value)
        {
            WriteUInt(SharedMemoryLayout.LapsInEvent, value);
            return this;
        }

        public RawFixtureBuilder SetTrackTelemetry(float trackLength, float eventTimeRemaining)
        {
            WriteFloat(SharedMemoryLayout.TrackLength, trackLength);
            WriteFloat(SharedMemoryLayout.EventTimeRemaining, eventTimeRemaining);
            return this;
        }

        public RawFixtureBuilder SetSessionTiming(float durationMinutes, int additionalLaps, float eventTimeRemaining)
        {
            WriteFloat(SharedMemoryLayout.SessionDuration, durationMinutes);
            WriteInt(SharedMemoryLayout.SessionAdditionalLaps, additionalLaps);
            WriteFloat(SharedMemoryLayout.EventTimeRemaining, eventTimeRemaining);
            return this;
        }

        public RawFixtureBuilder SetRootVehicle(string vehicle, string vehicleClass)
        {
            WriteString(SharedMemoryLayout.CarName, vehicle, SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.CarClassName, vehicleClass, SharedMemoryLayout.StringLength);
            return this;
        }

        public RawFixtureBuilder SetRootFastestTiming(
            float personalLap,
            float worldLap,
            float personalSector1,
            float personalSector2,
            float personalSector3,
            float worldSector1,
            float worldSector2,
            float worldSector3)
        {
            WriteFloat(SharedMemoryLayout.PersonalFastestLapTime, personalLap);
            WriteFloat(SharedMemoryLayout.WorldFastestLapTime, worldLap);
            WriteFloat(SharedMemoryLayout.PersonalFastestSector1Time, personalSector1);
            WriteFloat(SharedMemoryLayout.PersonalFastestSector2Time, personalSector2);
            WriteFloat(SharedMemoryLayout.PersonalFastestSector3Time, personalSector3);
            WriteFloat(SharedMemoryLayout.WorldFastestSector1Time, worldSector1);
            WriteFloat(SharedMemoryLayout.WorldFastestSector2Time, worldSector2);
            WriteFloat(SharedMemoryLayout.WorldFastestSector3Time, worldSector3);
            return this;
        }

        public RawFixtureBuilder SetWeather(
            float ambientTemperature,
            float trackTemperature,
            float rainDensity,
            float windSpeed,
            float windDirectionX,
            float windDirectionY,
            float cloudBrightness,
            float snowDensity)
        {
            WriteFloat(SharedMemoryLayout.AmbientTemperature, ambientTemperature);
            WriteFloat(SharedMemoryLayout.TrackTemperature, trackTemperature);
            WriteFloat(SharedMemoryLayout.RainDensity, rainDensity);
            WriteFloat(SharedMemoryLayout.WindSpeed, windSpeed);
            WriteFloat(SharedMemoryLayout.WindDirectionX, windDirectionX);
            WriteFloat(SharedMemoryLayout.WindDirectionY, windDirectionY);
            WriteFloat(SharedMemoryLayout.CloudBrightness, cloudBrightness);
            WriteFloat(SharedMemoryLayout.SnowDensity, snowDensity);
            return this;
        }

        public RawFixtureBuilder SetSessionActivityMetadata(int enforcedPitStopLap, bool sessionIsPrivate)
        {
            WriteInt(SharedMemoryLayout.EnforcedPitStopLap, enforcedPitStopLap);
            Buffer[SharedMemoryLayout.SessionIsPrivate] = sessionIsPrivate ? (byte)1 : (byte)0;
            return this;
        }

        public RawFixtureBuilder SetRootControl(FlagColour colour, FlagReason reason = FlagReason.None, PitMode pitMode = PitMode.None, PitSchedule pitSchedule = PitSchedule.None)
        {
            WriteUInt(SharedMemoryLayout.HighestFlagColour, (uint)colour);
            WriteUInt(SharedMemoryLayout.HighestFlagReason, (uint)reason);
            WriteUInt(SharedMemoryLayout.PitMode, (uint)pitMode);
            WriteUInt(SharedMemoryLayout.PitSchedule, (uint)pitSchedule);
            return this;
        }

        public RawFixtureBuilder SetYellowFlagState(YellowFlagState state)
        {
            WriteInt(SharedMemoryLayout.YellowFlagState, (int)state);
            return this;
        }

        public RawFixtureBuilder SetParticipantControl(int index, PitSchedule pitSchedule, FlagColour colour = FlagColour.None, FlagReason reason = FlagReason.None)
        {
            WriteUInt(SharedMemoryLayout.PitSchedules + (index * 4), (uint)pitSchedule);
            WriteUInt(SharedMemoryLayout.HighestFlagColours + (index * 4), (uint)colour);
            WriteUInt(SharedMemoryLayout.HighestFlagReasons + (index * 4), (uint)reason);
            return this;
        }

        public RawFixtureBuilder SetParticipantLapTimes(int index, float bestLap, float lastLap)
        {
            WriteFloat(SharedMemoryLayout.FastestLapTimes + (index * 4), bestLap);
            WriteFloat(SharedMemoryLayout.LastLapTimes + (index * 4), lastLap);
            return this;
        }

        public RawFixtureBuilder SetCurrentTiming(
            float currentTime,
            float sector1,
            float sector2,
            float sector3,
            bool invalidated = false,
            int participantIndex = 3)
        {
            WriteInt(SharedMemoryLayout.NumSectors, 3);
            Buffer[SharedMemoryLayout.LapInvalidated] = invalidated ? (byte)1 : (byte)0;
            WriteFloat(SharedMemoryLayout.CurrentTime, currentTime);
            WriteFloat(SharedMemoryLayout.CurrentSector1Time, sector1);
            WriteFloat(SharedMemoryLayout.CurrentSector2Time, sector2);
            WriteFloat(SharedMemoryLayout.CurrentSector3Time, sector3);
            Buffer[SharedMemoryLayout.LapsInvalidated + participantIndex] = invalidated ? (byte)1 : (byte)0;
            WriteFloat(SharedMemoryLayout.CurrentSector1Times + (participantIndex * 4), sector1);
            WriteFloat(SharedMemoryLayout.CurrentSector2Times + (participantIndex * 4), sector2);
            WriteFloat(SharedMemoryLayout.CurrentSector3Times + (participantIndex * 4), sector3);
            return this;
        }

        public RawFixtureBuilder SetParticipantCurrentSector(int participantIndex, int currentSector)
        {
            int offset = SharedMemoryLayout.ParticipantOffset(participantIndex);
            WriteInt(offset + SharedMemoryLayout.ParticipantCurrentSector, currentSector);
            return this;
        }

        public RawFixtureBuilder SetParticipant(
            int index,
            bool active,
            string name,
            uint position,
            uint lapsCompleted,
            uint currentLap,
            RaceState raceState,
            PitMode pitMode)
        {
            int offset = SharedMemoryLayout.ParticipantOffset(index);
            Buffer[offset + SharedMemoryLayout.ParticipantIsActive] = active ? (byte)1 : (byte)0;
            WriteString(offset + SharedMemoryLayout.ParticipantName, name, SharedMemoryLayout.StringLength);
            WriteUInt(offset + SharedMemoryLayout.ParticipantRacePosition, position);
            WriteUInt(offset + SharedMemoryLayout.ParticipantLapsCompleted, lapsCompleted);
            WriteUInt(offset + SharedMemoryLayout.ParticipantCurrentLap, currentLap);
            WriteInt(offset + SharedMemoryLayout.ParticipantCurrentSector, 1);
            WriteFloat(offset + SharedMemoryLayout.ParticipantCurrentLapDistance, 1234.5f + index);
            WriteUInt(SharedMemoryLayout.RaceStates + (index * 4), (uint)raceState);
            WriteUInt(SharedMemoryLayout.PitModes + (index * 4), (uint)pitMode);
            WriteUInt(SharedMemoryLayout.PitSchedules + (index * 4), (uint)PitSchedule.None);
            WriteUInt(SharedMemoryLayout.HighestFlagColours + (index * 4), (uint)FlagColour.None);
            WriteUInt(SharedMemoryLayout.HighestFlagReasons + (index * 4), (uint)FlagReason.None);
            WriteFloat(SharedMemoryLayout.FastestLapTimes + (index * 4), 137.881f + index);
            WriteFloat(SharedMemoryLayout.LastLapTimes + (index * 4), 138.421f + index);
            WriteString(SharedMemoryLayout.CarNames + (index * SharedMemoryLayout.StringLength), "Formula Trainer", SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.CarClassNames + (index * SharedMemoryLayout.StringLength), "F-Trainer", SharedMemoryLayout.StringLength);
            return this;
        }

        public RawFixtureBuilder SetGlobalRaceState(RaceState state)
        {
            WriteUInt(SharedMemoryLayout.RaceState, (uint)state);
            return this;
        }

        public RawFixtureBuilder SetTrack(string location, string variation)
        {
            WriteString(SharedMemoryLayout.TrackLocation, location, SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.TrackVariation, variation, SharedMemoryLayout.StringLength);
            return this;
        }

        public RawFixtureBuilder SetParticipantVehicle(int index, string vehicle, string vehicleClass)
        {
            WriteString(SharedMemoryLayout.CarNames + (index * SharedMemoryLayout.StringLength), vehicle, SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.CarClassNames + (index * SharedMemoryLayout.StringLength), vehicleClass, SharedMemoryLayout.StringLength);
            return this;
        }

        public RawFixtureBuilder SetParticipantMotion(
            int index,
            float worldX,
            float worldY,
            float worldZ,
            float orientationX,
            float orientationY,
            float orientationZ,
            float speedMetresPerSecond)
        {
            int participantOffset = SharedMemoryLayout.ParticipantOffset(index);
            WriteVector3(participantOffset + SharedMemoryLayout.ParticipantWorldPosition, worldX, worldY, worldZ);
            WriteVector3(
                SharedMemoryLayout.Orientations + (index * SharedMemoryLayout.VectorLength * sizeof(float)),
                orientationX,
                orientationY,
                orientationZ);
            WriteFloat(SharedMemoryLayout.Speeds + (index * sizeof(float)), speedMetresPerSecond);
            WriteUInt(SharedMemoryLayout.Nationalities + (index * sizeof(uint)), 82);
            return this;
        }

        public RawFixtureBuilder SetExtendedTimingAndTrack(float splitTime, string translatedLocation, string translatedVariation)
        {
            WriteFloat(SharedMemoryLayout.SplitTime, splitTime);
            WriteString(SharedMemoryLayout.TranslatedTrackLocation, translatedLocation, SharedMemoryLayout.StringLength);
            WriteString(SharedMemoryLayout.TranslatedTrackVariation, translatedVariation, SharedMemoryLayout.StringLength);
            return this;
        }

        public RawFixtureBuilder SetViewedVehicleTelemetry()
        {
            WriteFloat(SharedMemoryLayout.UnfilteredThrottle, 0.71f);
            WriteFloat(SharedMemoryLayout.UnfilteredBrake, 0.22f);
            WriteFloat(SharedMemoryLayout.UnfilteredSteering, -0.33f);
            WriteFloat(SharedMemoryLayout.UnfilteredClutch, 0.44f);
            WriteUInt(SharedMemoryLayout.CarFlags, 0x25);
            WriteFloat(SharedMemoryLayout.OilTempCelsius, 101.5f);
            WriteFloat(SharedMemoryLayout.OilPressureKPa, 345.6f);
            WriteFloat(SharedMemoryLayout.WaterTempCelsius, 91.2f);
            WriteFloat(SharedMemoryLayout.WaterPressureKPa, 222.3f);
            WriteFloat(SharedMemoryLayout.FuelPressureKPa, 444.5f);
            WriteFloat(SharedMemoryLayout.FuelLevel, 0.63f);
            WriteFloat(SharedMemoryLayout.FuelCapacity, 110.0f);
            WriteFloat(SharedMemoryLayout.Speed, 72.25f);
            WriteFloat(SharedMemoryLayout.Rpm, 7123.0f);
            WriteFloat(SharedMemoryLayout.MaxRpm, 9000.0f);
            WriteFloat(SharedMemoryLayout.Brake, 0.24f);
            WriteFloat(SharedMemoryLayout.Throttle, 0.69f);
            WriteFloat(SharedMemoryLayout.Clutch, 0.41f);
            WriteFloat(SharedMemoryLayout.Steering, -0.31f);
            WriteInt(SharedMemoryLayout.Gear, 4);
            WriteInt(SharedMemoryLayout.NumGears, 6);
            WriteFloat(SharedMemoryLayout.OdometerKm, 123.45f);
            Buffer[SharedMemoryLayout.AntiLockActive] = 1;
            WriteInt(SharedMemoryLayout.LastOpponentCollisionIndex, 9);
            WriteFloat(SharedMemoryLayout.LastOpponentCollisionMagnitude, 12.75f);
            Buffer[SharedMemoryLayout.BoostActive] = 1;
            WriteFloat(SharedMemoryLayout.BoostAmount, 56.5f);
            WriteVector3(SharedMemoryLayout.Orientation, 0.1f, 0.2f, 0.3f);
            WriteVector3(SharedMemoryLayout.LocalVelocity, 1.1f, 1.2f, 1.3f);
            WriteVector3(SharedMemoryLayout.WorldVelocity, 2.1f, 2.2f, 2.3f);
            WriteVector3(SharedMemoryLayout.AngularVelocity, 3.1f, 3.2f, 3.3f);
            WriteVector3(SharedMemoryLayout.LocalAcceleration, 4.1f, 4.2f, 4.3f);
            WriteVector3(SharedMemoryLayout.WorldAcceleration, 5.1f, 5.2f, 5.3f);
            WriteVector3(SharedMemoryLayout.ExtentsCentre, 6.1f, 6.2f, 6.3f);
            WriteFloat(SharedMemoryLayout.EngineSpeed, 777.25f);
            WriteFloat(SharedMemoryLayout.EngineTorque, 498.5f);
            WriteFloat(SharedMemoryLayout.Wings, 0.17f);
            WriteFloat(SharedMemoryLayout.Wings + sizeof(float), 0.23f);
            WriteFloat(SharedMemoryLayout.HandBrake, 0.05f);
            WriteUInt(SharedMemoryLayout.CrashState, 3);
            WriteFloat(SharedMemoryLayout.AeroDamage, 0.12f);
            WriteFloat(SharedMemoryLayout.EngineDamage, 0.08f);
            WriteFloat(SharedMemoryLayout.BrakeBias, 0.57f);
            WriteFloat(SharedMemoryLayout.TurboBoostPressure, 1.42f);
            WriteUInt(SharedMemoryLayout.DrsState, 0x18);
            WriteInt(SharedMemoryLayout.AntiLockSetting, 4);
            WriteInt(SharedMemoryLayout.TractionControlSetting, 3);
            WriteInt(SharedMemoryLayout.ErsDeploymentMode, 4);
            Buffer[SharedMemoryLayout.ErsAutoModeEnabled] = 1;
            WriteFloat(SharedMemoryLayout.ClutchTemp, 355.5f);
            WriteFloat(SharedMemoryLayout.ClutchWear, 0.18f);
            Buffer[SharedMemoryLayout.ClutchOverheated] = 1;
            Buffer[SharedMemoryLayout.ClutchSlipping] = 1;
            WriteInt(SharedMemoryLayout.LaunchStage, 2);
            return this;
        }

        public RawFixtureBuilder SetTyreTelemetry(int index)
        {
            WriteUInt(SharedMemoryLayout.TyreFlags + (index * sizeof(uint)), 7);
            WriteUInt(SharedMemoryLayout.Terrain + (index * sizeof(uint)), 10);
            WriteFloat(SharedMemoryLayout.TyreY + (index * sizeof(float)), 0.11f);
            WriteFloat(SharedMemoryLayout.TyreRps + (index * sizeof(float)), 22.2f);
            WriteFloat(SharedMemoryLayout.TyreTemp + (index * sizeof(float)), 83.3f);
            WriteFloat(SharedMemoryLayout.TyreHeightAboveGround + (index * sizeof(float)), 0.04f);
            WriteFloat(SharedMemoryLayout.TyreWear + (index * sizeof(float)), 0.81f);
            WriteFloat(SharedMemoryLayout.BrakeDamage + (index * sizeof(float)), 0.02f);
            WriteFloat(SharedMemoryLayout.SuspensionDamage + (index * sizeof(float)), 0.03f);
            WriteFloat(SharedMemoryLayout.BrakeTempCelsius + (index * sizeof(float)), 612.5f);
            WriteFloat(SharedMemoryLayout.TyreTreadTemp + (index * sizeof(float)), 355.1f);
            WriteFloat(SharedMemoryLayout.TyreLayerTemp + (index * sizeof(float)), 354.2f);
            WriteFloat(SharedMemoryLayout.TyreCarcassTemp + (index * sizeof(float)), 353.3f);
            WriteFloat(SharedMemoryLayout.TyreRimTemp + (index * sizeof(float)), 352.4f);
            WriteFloat(SharedMemoryLayout.TyreInternalAirTemp + (index * sizeof(float)), 351.5f);
            WriteFloat(SharedMemoryLayout.WheelLocalPositionY + (index * sizeof(float)), -0.21f);
            WriteFloat(SharedMemoryLayout.SuspensionTravel + (index * sizeof(float)), 0.06f);
            WriteFloat(SharedMemoryLayout.SuspensionVelocity + (index * sizeof(float)), -0.7f);
            WriteFloat(SharedMemoryLayout.AirPressure + (index * sizeof(float)), 27.8f);
            WriteString(
                SharedMemoryLayout.TyreCompound + (index * SharedMemoryLayout.TyreCompoundLength),
                "Soft Slick",
                SharedMemoryLayout.TyreCompoundLength);
            WriteFloat(SharedMemoryLayout.TyreTempLeft + (index * sizeof(float)), 80.1f);
            WriteFloat(SharedMemoryLayout.TyreTempCenter + (index * sizeof(float)), 81.2f);
            WriteFloat(SharedMemoryLayout.TyreTempRight + (index * sizeof(float)), 82.3f);
            WriteFloat(SharedMemoryLayout.RideHeight + (index * sizeof(float)), 7.4f);
            return this;
        }

        public RawFixtureBuilder SetRawNameBytes(int index, byte[] bytes)
        {
            int offset = SharedMemoryLayout.ParticipantOffset(index) + SharedMemoryLayout.ParticipantName;
            Array.Clear(Buffer, offset, SharedMemoryLayout.StringLength);
            Array.Copy(bytes, 0, Buffer, offset, Math.Min(bytes.Length, SharedMemoryLayout.StringLength));
            return this;
        }

        public RawFixtureBuilder SetRawGameState(uint raw)
        {
            WriteUInt(SharedMemoryLayout.GameState, raw);
            return this;
        }

        private void WriteUInt(int offset, uint value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, Buffer, offset, 4);
        }

        private void WriteInt(int offset, int value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, Buffer, offset, 4);
        }

        private void WriteFloat(int offset, float value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, Buffer, offset, 4);
        }

        private void WriteVector3(int offset, float x, float y, float z)
        {
            WriteFloat(offset, x);
            WriteFloat(offset + sizeof(float), y);
            WriteFloat(offset + (2 * sizeof(float)), z);
        }

        private void WriteString(int offset, string value, int maxLength)
        {
            Array.Clear(Buffer, offset, maxLength);
            byte[] encoded = Encoding.UTF8.GetBytes(value);
            Array.Copy(encoded, 0, Buffer, offset, Math.Min(encoded.Length, maxLength - 1));
        }
    }
}
