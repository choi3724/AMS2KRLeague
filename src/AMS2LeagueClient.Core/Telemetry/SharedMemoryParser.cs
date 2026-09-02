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
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.HighestFlagReasons + (index * sizeof(uint))),
                    SharedMemoryLayout.ReadVector3(
                        buffer,
                        participantOffset + SharedMemoryLayout.ParticipantWorldPosition),
                    SharedMemoryLayout.ReadVector3(
                        buffer,
                        SharedMemoryLayout.Orientations + (index * SharedMemoryLayout.VectorLength * sizeof(float))),
                    SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Speeds + (index * sizeof(float))),
                    SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.Nationalities + (index * sizeof(uint))));
            }

            ViewedVehicleTelemetrySnapshot viewedVehicleTelemetry = ParseViewedVehicleTelemetry(buffer);

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
                buffer[SharedMemoryLayout.SessionIsPrivate] != 0,
                SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.YellowFlagState),
                SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.SplitTime),
                SharedMemoryLayout.ReadNullTerminatedAscii(
                    buffer,
                    SharedMemoryLayout.TranslatedTrackLocation,
                    SharedMemoryLayout.StringLength),
                SharedMemoryLayout.ReadNullTerminatedAscii(
                    buffer,
                    SharedMemoryLayout.TranslatedTrackVariation,
                    SharedMemoryLayout.StringLength),
                viewedVehicleTelemetry);

            return TelemetryReadResult.Success(snapshot, sequenceRetries);
        }

        private static ViewedVehicleTelemetrySnapshot ParseViewedVehicleTelemetry(byte[] buffer)
        {
            var telemetry = new ViewedVehicleTelemetrySnapshot
            {
                UnfilteredThrottle = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.UnfilteredThrottle),
                UnfilteredBrake = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.UnfilteredBrake),
                UnfilteredSteering = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.UnfilteredSteering),
                UnfilteredClutch = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.UnfilteredClutch),
                CarFlagsRaw = SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.CarFlags),
                OilTemperatureCelsius = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.OilTempCelsius),
                OilPressureKPa = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.OilPressureKPa),
                WaterTemperatureCelsius = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WaterTempCelsius),
                WaterPressureKPa = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.WaterPressureKPa),
                FuelPressureKPa = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FuelPressureKPa),
                FuelLevel = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FuelLevel),
                FuelCapacityLitres = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.FuelCapacity),
                SpeedMetresPerSecond = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Speed),
                Rpm = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Rpm),
                MaxRpm = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.MaxRpm),
                Brake = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Brake),
                Throttle = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Throttle),
                Clutch = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Clutch),
                Steering = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Steering),
                Gear = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.Gear),
                NumGears = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.NumGears),
                OdometerKilometres = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.OdometerKm),
                AntiLockActive = buffer[SharedMemoryLayout.AntiLockActive] != 0,
                LastOpponentCollisionIndex = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.LastOpponentCollisionIndex),
                LastOpponentCollisionMagnitude = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.LastOpponentCollisionMagnitude),
                BoostActive = buffer[SharedMemoryLayout.BoostActive] != 0,
                BoostAmount = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.BoostAmount),
                Orientation = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.Orientation),
                LocalVelocity = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.LocalVelocity),
                WorldVelocity = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.WorldVelocity),
                AngularVelocity = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.AngularVelocity),
                LocalAcceleration = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.LocalAcceleration),
                WorldAcceleration = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.WorldAcceleration),
                ExtentsCentre = SharedMemoryLayout.ReadVector3(buffer, SharedMemoryLayout.ExtentsCentre),
                EngineSpeedRadiansPerSecond = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.EngineSpeed),
                EngineTorqueNewtonMetres = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.EngineTorque),
                FrontWing = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Wings),
                RearWing = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.Wings + sizeof(float)),
                HandBrake = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.HandBrake),
                CrashStateRaw = SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.CrashState),
                AeroDamage = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.AeroDamage),
                EngineDamage = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.EngineDamage),
                BrakeBias = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.BrakeBias),
                TurboBoostPressure = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.TurboBoostPressure),
                DrsStateRaw = SharedMemoryLayout.ReadUInt32(buffer, SharedMemoryLayout.DrsState),
                AntiLockSetting = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.AntiLockSetting),
                TractionControlSetting = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.TractionControlSetting),
                ErsDeploymentModeRaw = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.ErsDeploymentMode),
                ErsAutoModeEnabled = buffer[SharedMemoryLayout.ErsAutoModeEnabled] != 0,
                ClutchTemperatureKelvin = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.ClutchTemp),
                ClutchWear = SharedMemoryLayout.ReadSingle(buffer, SharedMemoryLayout.ClutchWear),
                ClutchOverheated = buffer[SharedMemoryLayout.ClutchOverheated] != 0,
                ClutchSlipping = buffer[SharedMemoryLayout.ClutchSlipping] != 0,
                LaunchStageRaw = SharedMemoryLayout.ReadInt32(buffer, SharedMemoryLayout.LaunchStage)
            };

            telemetry.SetTyres(ParseTyres(buffer));
            return telemetry;
        }

        private static TyreTelemetrySnapshot[] ParseTyres(byte[] buffer)
        {
            var tyres = new TyreTelemetrySnapshot[SharedMemoryLayout.TyreCount];
            for (int index = 0; index < tyres.Length; index++)
            {
                tyres[index] = new TyreTelemetrySnapshot
                {
                    Index = index,
                    FlagsRaw = ReadTyreUInt32(buffer, SharedMemoryLayout.TyreFlags, index),
                    TerrainRaw = ReadTyreUInt32(buffer, SharedMemoryLayout.Terrain, index),
                    LocalY = ReadTyreSingle(buffer, SharedMemoryLayout.TyreY, index),
                    RevolutionsPerSecond = ReadTyreSingle(buffer, SharedMemoryLayout.TyreRps, index),
                    TemperatureCelsius = ReadTyreSingle(buffer, SharedMemoryLayout.TyreTemp, index),
                    HeightAboveGround = ReadTyreSingle(buffer, SharedMemoryLayout.TyreHeightAboveGround, index),
                    Wear = ReadTyreSingle(buffer, SharedMemoryLayout.TyreWear, index),
                    BrakeDamage = ReadTyreSingle(buffer, SharedMemoryLayout.BrakeDamage, index),
                    SuspensionDamage = ReadTyreSingle(buffer, SharedMemoryLayout.SuspensionDamage, index),
                    BrakeTemperatureCelsius = ReadTyreSingle(buffer, SharedMemoryLayout.BrakeTempCelsius, index),
                    TreadTemperatureKelvin = ReadTyreSingle(buffer, SharedMemoryLayout.TyreTreadTemp, index),
                    LayerTemperatureKelvin = ReadTyreSingle(buffer, SharedMemoryLayout.TyreLayerTemp, index),
                    CarcassTemperatureKelvin = ReadTyreSingle(buffer, SharedMemoryLayout.TyreCarcassTemp, index),
                    RimTemperatureKelvin = ReadTyreSingle(buffer, SharedMemoryLayout.TyreRimTemp, index),
                    InternalAirTemperatureKelvin = ReadTyreSingle(buffer, SharedMemoryLayout.TyreInternalAirTemp, index),
                    WheelLocalPositionY = ReadTyreSingle(buffer, SharedMemoryLayout.WheelLocalPositionY, index),
                    SuspensionTravelMetres = ReadTyreSingle(buffer, SharedMemoryLayout.SuspensionTravel, index),
                    SuspensionVelocity = ReadTyreSingle(buffer, SharedMemoryLayout.SuspensionVelocity, index),
                    AirPressurePsi = ReadTyreSingle(buffer, SharedMemoryLayout.AirPressure, index),
                    Compound = SharedMemoryLayout.ReadNullTerminatedAscii(
                        buffer,
                        SharedMemoryLayout.TyreCompound + (index * SharedMemoryLayout.TyreCompoundLength),
                        SharedMemoryLayout.TyreCompoundLength),
                    LeftTemperatureCelsius = ReadTyreSingle(buffer, SharedMemoryLayout.TyreTempLeft, index),
                    CenterTemperatureCelsius = ReadTyreSingle(buffer, SharedMemoryLayout.TyreTempCenter, index),
                    RightTemperatureCelsius = ReadTyreSingle(buffer, SharedMemoryLayout.TyreTempRight, index),
                    RideHeightCentimetres = ReadTyreSingle(buffer, SharedMemoryLayout.RideHeight, index)
                };
            }

            return tyres;
        }

        private static float ReadTyreSingle(byte[] buffer, int arrayOffset, int index)
            => SharedMemoryLayout.ReadSingle(buffer, arrayOffset + (index * sizeof(float)));

        private static uint ReadTyreUInt32(byte[] buffer, int arrayOffset, int index)
            => SharedMemoryLayout.ReadUInt32(buffer, arrayOffset + (index * sizeof(uint)));
    }
}
