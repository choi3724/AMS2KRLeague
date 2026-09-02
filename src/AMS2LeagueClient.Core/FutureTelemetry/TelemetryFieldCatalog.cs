using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AMS2LeagueClient.Core.FutureTelemetry
{
    /// <summary>
    /// Stable compact-row schemas. Fields are append-only: consumers may safely
    /// read the historical prefix while newer clients append raw source values.
    /// </summary>
    public static class TelemetryFieldCatalog
    {
        private static readonly string[] StoryBase =
        {
            "sessionElapsedMs", "capturedAtUnixMs", "eventTypeRef", "eventIdRef", "factCodeRef",
            "participantRef", "lap", "sector", "lapDistanceMeters", "worldX", "worldY", "worldZ",
            "positionBefore", "positionAfter", "lapTimeMs", "raceStateRaw", "pitStateRaw",
            "flagColourRaw", "flagReasonRaw", "penaltyTypeRaw", "resultStateRaw"
        };

        private static readonly string[] ReplayBase =
        {
            "sessionElapsedMs", "participantRef", "slot", "generation", "lap", "lapDistanceMeters",
            "racePosition", "worldX", "worldY", "worldZ", "raceStateRaw", "pitStateRaw",
            "nameRef", "vehicleRef", "vehicleClassRef", "headingRadians", "speedMetersPerSecond"
        };

        private static readonly string[] ReplayExtension =
        {
            "lapsCompleted", "sectorRaw", "currentSector1TimeSeconds", "currentSector2TimeSeconds",
            "currentSector3TimeSeconds", "lapInvalidated", "orientationRawX", "orientationRawY",
            "orientationRawZ", "nationalityRaw", "pitScheduleRaw", "highestFlagColourRaw",
            "highestFlagReasonRaw", "bestLapTimeSeconds", "lastLapTimeSeconds",
            "fastestSector1TimeSeconds", "fastestSector2TimeSeconds", "fastestSector3TimeSeconds",
            "isActive"
        };

        private static readonly string[] DriverBase =
        {
            "sessionElapsedMs", "capturedAtUnixMs", "driverRef", "lap", "sector", "lapDistanceMeters",
            "worldX", "worldY", "worldZ", "speedMetersPerSecond", "rpm", "gearRaw", "throttle",
            "brake", "steering", "clutch", "unfilteredThrottle", "unfilteredBrake",
            "unfilteredSteering", "unfilteredClutch", "longitudinalAccelerationMetersPerSecondSquared",
            "lateralAccelerationMetersPerSecondSquared", "verticalAccelerationMetersPerSecondSquared",
            "headingRadians", "velocityX", "velocityY", "velocityZ", "fuelLevelRatio",
            "fuelCapacityLiters", "fuelLiters", "brakeBias", "engineDamage", "aeroDamage",
            "suspensionDamage", "tyreTempFrontLeftCelsius", "tyreTempFrontRightCelsius",
            "tyreTempRearLeftCelsius", "tyreTempRearRightCelsius", "tyrePressureFrontLeftKpa",
            "tyrePressureFrontRightKpa", "tyrePressureRearLeftKpa", "tyrePressureRearRightKpa",
            "tyreWearFrontLeft", "tyreWearFrontRight", "tyreWearRearLeft", "tyreWearRearRight",
            "trackTemperatureCelsius", "ambientTemperatureCelsius", "rainDensity", "pitStateRaw",
            "lapValid", "currentLapTimeMs"
        };

        private static readonly string[] DriverScalarExtension =
        {
            "rootLapInvalidated", "participantLapInvalidated", "bestLapTimeSeconds", "lastLapTimeSeconds",
            "splitTimeAheadSeconds", "splitTimeBehindSeconds", "splitTimeSeconds",
            "personalFastestLapTimeSeconds", "worldFastestLapTimeSeconds", "currentSector1TimeSeconds",
            "currentSector2TimeSeconds", "currentSector3TimeSeconds", "fastestSector1TimeSeconds",
            "fastestSector2TimeSeconds", "fastestSector3TimeSeconds", "personalFastestSector1TimeSeconds",
            "personalFastestSector2TimeSeconds", "personalFastestSector3TimeSeconds",
            "worldFastestSector1TimeSeconds", "worldFastestSector2TimeSeconds", "worldFastestSector3TimeSeconds",
            "rootPitModeRaw", "rootPitScheduleRaw", "participantPitScheduleRaw", "highestFlagColourRaw",
            "highestFlagReasonRaw", "participantHighestFlagColourRaw", "participantHighestFlagReasonRaw",
            "carFlagsRaw", "oilTemperatureCelsius", "oilPressureKPa", "waterTemperatureCelsius",
            "waterPressureKPa", "fuelPressureKPa", "maxRpm", "numGears", "odometerKilometres",
            "antiLockActive", "lastOpponentCollisionIndex", "lastOpponentCollisionMagnitude",
            "boostActive", "boostAmount", "orientationRawX", "orientationRawY", "orientationRawZ",
            "localVelocityRawX", "localVelocityRawY", "localVelocityRawZ", "worldVelocityRawX",
            "worldVelocityRawY", "worldVelocityRawZ", "angularVelocityRawX", "angularVelocityRawY",
            "angularVelocityRawZ", "localAccelerationRawX", "localAccelerationRawY", "localAccelerationRawZ",
            "worldAccelerationRawX", "worldAccelerationRawY", "worldAccelerationRawZ", "extentsCentreRawX",
            "extentsCentreRawY", "extentsCentreRawZ", "engineSpeedRadiansPerSecond",
            "engineTorqueNewtonMetres", "frontWingRaw", "rearWingRaw", "handBrake", "crashStateRaw",
            "turboBoostPressure", "drsStateRaw", "antiLockSetting", "tractionControlSetting",
            "ersDeploymentModeRaw", "ersAutoModeEnabled", "clutchTemperatureKelvin", "clutchWear",
            "clutchOverheated", "clutchSlipping", "launchStageRaw", "currentTimeSecondsRaw",
            "sequenceNumberRaw"
        };

        private static readonly string[] IncidentBase =
        {
            "relativeTimeMs", "sessionElapsedMs", "capturedAtUnixMs", "candidateRef", "triggerCodeRef",
            "participantRef", "slot", "generation", "lap", "lapDistanceMeters", "racePosition",
            "worldX", "worldY", "worldZ", "raceStateRaw", "pitStateRaw", "flagColourRaw",
            "flagReasonRaw", "participantDisappeared", "positionChangeMagnitude", "headingRadians",
            "speedMetersPerSecond"
        };

        private static readonly string[] IncidentExtension =
        {
            "lapsCompleted", "sectorRaw", "currentSector1TimeSeconds", "currentSector2TimeSeconds",
            "currentSector3TimeSeconds", "lapInvalidated", "orientationRawX", "orientationRawY",
            "orientationRawZ", "nationalityRaw", "pitScheduleRaw", "highestParticipantFlagColourRaw",
            "highestParticipantFlagReasonRaw", "bestLapTimeSeconds", "lastLapTimeSeconds",
            "fastestSector1TimeSeconds", "fastestSector2TimeSeconds", "fastestSector3TimeSeconds",
            "isActive", "yellowFlagStateRaw", "viewedParticipantRef", "collisionOpponentSlotRaw",
            "collisionOpponentRef", "collisionMagnitude", "crashStateRaw"
        };

        private static readonly string[] WheelPositions =
        {
            "FrontLeft", "FrontRight", "RearLeft", "RearRight"
        };

        private static readonly string[] WheelValuePrefixes =
        {
            "tyreFlags", "tyreTerrain", "tyreLocalY", "tyreRevolutionsPerSecond",
            "tyreHeightAboveGround", "tyreBrakeDamage", "tyreSuspensionDamage",
            "tyreBrakeTemperatureCelsius", "tyreTreadTemperatureKelvin", "tyreLayerTemperatureKelvin",
            "tyreCarcassTemperatureKelvin", "tyreRimTemperatureKelvin", "tyreInternalAirTemperatureKelvin",
            "wheelLocalPositionY", "tyreSuspensionTravelMetres", "tyreSuspensionVelocity",
            "tyreAirPressurePsi", "tyreCompound", "tyreLeftTemperatureCelsius",
            "tyreCenterTemperatureCelsius", "tyreRightTemperatureCelsius", "rideHeightCentimetres"
        };

        private static readonly string[] DriverWheelExtension = BuildWheelFields();
        private static readonly string[] Story = StoryBase
            .Concat(new[] { "yellowFlagStateRaw", "participantIsActiveRaw" })
            .ToArray();
        private static readonly string[] Replay = ReplayBase.Concat(ReplayExtension).ToArray();
        private static readonly string[] Driver = DriverBase.Concat(DriverScalarExtension).Concat(DriverWheelExtension).ToArray();
        private static readonly string[] Incident = IncidentBase.Concat(IncidentExtension).ToArray();

        public const int RaceStoryBaseFieldCount = 21;
        public const int ParticipantReplayBaseFieldCount = 17;
        public const int DriverTelemetryBaseFieldCount = 52;
        public const int IncidentTraceBaseFieldCount = 22;

        public static IReadOnlyList<string> RaceStoryFields { get; } = Array.AsReadOnly(Story);
        public static IReadOnlyList<string> ParticipantReplayFields { get; } = Array.AsReadOnly(Replay);
        public static IReadOnlyList<string> DriverTelemetryFields { get; } = Array.AsReadOnly(Driver);
        public static IReadOnlyList<string> IncidentTraceFields { get; } = Array.AsReadOnly(Incident);
        public static IReadOnlyList<string> DriverAdditionalScalarFields { get; } = Array.AsReadOnly(DriverScalarExtension);
        public static IReadOnlyList<string> DriverAdditionalWheelFields { get; } = Array.AsReadOnly(DriverWheelExtension);

        private static string[] BuildWheelFields()
        {
            var values = new List<string>(WheelPositions.Length * WheelValuePrefixes.Length);
            foreach (string prefix in WheelValuePrefixes)
            {
                foreach (string position in WheelPositions)
                {
                    values.Add(prefix + position + (prefix == "tyreCompound" ? "Ref" : string.Empty));
                }
            }
            return values.ToArray();
        }
    }
}
