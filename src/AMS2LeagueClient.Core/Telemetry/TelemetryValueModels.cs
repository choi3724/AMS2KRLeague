using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Telemetry
{
    public readonly struct TelemetryVector3 : IEquatable<TelemetryVector3>
    {
        public TelemetryVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public bool Equals(TelemetryVector3 other)
            => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object? obj)
            => obj is TelemetryVector3 other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(X, Y, Z);
    }

    public sealed class TyreTelemetrySnapshot
    {
        internal TyreTelemetrySnapshot()
        {
        }

        public int Index { get; internal set; }
        public uint FlagsRaw { get; internal set; }
        public uint TerrainRaw { get; internal set; }
        public float LocalY { get; internal set; }
        public float RevolutionsPerSecond { get; internal set; }
        public float TemperatureCelsius { get; internal set; }
        public float HeightAboveGround { get; internal set; }
        public float Wear { get; internal set; }
        public float BrakeDamage { get; internal set; }
        public float SuspensionDamage { get; internal set; }
        public float BrakeTemperatureCelsius { get; internal set; }
        public float TreadTemperatureKelvin { get; internal set; }
        public float LayerTemperatureKelvin { get; internal set; }
        public float CarcassTemperatureKelvin { get; internal set; }
        public float RimTemperatureKelvin { get; internal set; }
        public float InternalAirTemperatureKelvin { get; internal set; }
        public float WheelLocalPositionY { get; internal set; }
        public float SuspensionTravelMetres { get; internal set; }
        public float SuspensionVelocity { get; internal set; }
        public float AirPressurePsi { get; internal set; }
        public string Compound { get; internal set; } = string.Empty;
        public float LeftTemperatureCelsius { get; internal set; }
        public float CenterTemperatureCelsius { get; internal set; }
        public float RightTemperatureCelsius { get; internal set; }
        public float RideHeightCentimetres { get; internal set; }
    }

    /// <summary>
    /// Root-scoped vehicle telemetry. The v14 header exposes these values for the
    /// viewed participant. SHM v14 has no authoritative local-owner/spectator
    /// signal, so viewed/root consistency alone is insufficient for treating
    /// these values as private driver coaching telemetry.
    /// </summary>
    public sealed class ViewedVehicleTelemetrySnapshot
    {
        private TyreTelemetrySnapshot[] _tyres = Array.Empty<TyreTelemetrySnapshot>();

        internal ViewedVehicleTelemetrySnapshot()
        {
        }

        public float UnfilteredThrottle { get; internal set; }
        public float UnfilteredBrake { get; internal set; }
        public float UnfilteredSteering { get; internal set; }
        public float UnfilteredClutch { get; internal set; }
        public uint CarFlagsRaw { get; internal set; }
        public float OilTemperatureCelsius { get; internal set; }
        public float OilPressureKPa { get; internal set; }
        public float WaterTemperatureCelsius { get; internal set; }
        public float WaterPressureKPa { get; internal set; }
        public float FuelPressureKPa { get; internal set; }
        public float FuelLevel { get; internal set; }
        public float FuelCapacityLitres { get; internal set; }
        public float SpeedMetresPerSecond { get; internal set; }
        public float Rpm { get; internal set; }
        public float MaxRpm { get; internal set; }
        public float Brake { get; internal set; }
        public float Throttle { get; internal set; }
        public float Clutch { get; internal set; }
        public float Steering { get; internal set; }
        public int Gear { get; internal set; }
        public int NumGears { get; internal set; }
        public float OdometerKilometres { get; internal set; }
        public bool AntiLockActive { get; internal set; }
        public int LastOpponentCollisionIndex { get; internal set; }
        public float LastOpponentCollisionMagnitude { get; internal set; }
        public bool BoostActive { get; internal set; }
        public float BoostAmount { get; internal set; }
        public TelemetryVector3 Orientation { get; internal set; }
        public TelemetryVector3 LocalVelocity { get; internal set; }
        public TelemetryVector3 WorldVelocity { get; internal set; }
        public TelemetryVector3 AngularVelocity { get; internal set; }
        public TelemetryVector3 LocalAcceleration { get; internal set; }
        public TelemetryVector3 WorldAcceleration { get; internal set; }
        public TelemetryVector3 ExtentsCentre { get; internal set; }
        public float EngineSpeedRadiansPerSecond { get; internal set; }
        public float EngineTorqueNewtonMetres { get; internal set; }
        public float FrontWing { get; internal set; }
        public float RearWing { get; internal set; }
        public float HandBrake { get; internal set; }
        public uint CrashStateRaw { get; internal set; }
        public float AeroDamage { get; internal set; }
        public float EngineDamage { get; internal set; }
        public float BrakeBias { get; internal set; }
        public float TurboBoostPressure { get; internal set; }
        public uint DrsStateRaw { get; internal set; }
        public int AntiLockSetting { get; internal set; }
        public int TractionControlSetting { get; internal set; }
        public int ErsDeploymentModeRaw { get; internal set; }
        public bool ErsAutoModeEnabled { get; internal set; }
        public float ClutchTemperatureKelvin { get; internal set; }
        public float ClutchWear { get; internal set; }
        public bool ClutchOverheated { get; internal set; }
        public bool ClutchSlipping { get; internal set; }
        public int LaunchStageRaw { get; internal set; }
        public IReadOnlyList<TyreTelemetrySnapshot> Tyres => _tyres;

        internal void SetTyres(TyreTelemetrySnapshot[] tyres)
            => _tyres = (TyreTelemetrySnapshot[])(tyres ?? throw new ArgumentNullException(nameof(tyres))).Clone();
    }
}
