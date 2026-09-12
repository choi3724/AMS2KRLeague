using System;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    // Display calibration only. None of these values changes a captured RPM sample.
    public sealed class AvanteRpmCalibration
    {
        public double Maximum { get; set; } = 8000;
        public bool AutomaticMaximum { get; set; } // Absent in legacy JSON means preserve its manual maximum.
        public double YellowStart { get; set; } = 5000;
        public double RedStart { get; set; } = 6000;
        public bool IsValid => double.IsFinite(Maximum) && Maximum >= 1000 && Maximum <= 100000
            && double.IsFinite(YellowStart) && double.IsFinite(RedStart)
            && YellowStart >= 0 && YellowStart < RedStart && RedStart > 0
            && (AutomaticMaximum ? RedStart < 100000 : RedStart < Maximum);
        public AvanteRpmCalibration Copy() => new AvanteRpmCalibration { Maximum = Maximum, YellowStart = YellowStart, RedStart = RedStart, AutomaticMaximum = AutomaticMaximum };
    }

    public readonly struct AvanteRpmScale : IEquatable<AvanteRpmScale>
    {
        public double Maximum { get; }
        public double YellowStart { get; }
        public double RedStart { get; }
        public bool Calibrated { get; }
        public bool VehicleProfile { get; }
        public bool AutomaticMaximum { get; }
        public bool HasWarningThresholds => double.IsFinite(RedStart);
        public static double MaximumAboveRed(double redStart) => (Math.Floor(redStart / 1000) + 1) * 1000;
        // Exact participant identity from the 2026-09-12 local capture; no family-wide matching.
        public const string AstonMartinLowDownforce = "Aston Martin Vantage GT3 Evo - Low Downforce";
        public const string LolaSuperspeedway = "Lola B2K00 Ford-Cosworth - Superspeedway";
        public const string IvecoStralis = "Iveco Stralis";
        public const string ArcCamaro = "ARC Camaro";
        public string SourceDescription => Calibrated ? (AutomaticMaximum ? "사용자 경고 RPM · 레드존 기준 자동 눈금 적용" : "사용자 경고 RPM · 최대 눈금 수동 지정 적용")
            : HasWarningThresholds ? "공통 표시 정책: mMaxRPM의 90%부터 노랑, 97%부터 빨강·점멸·외곽 완등. 게임이 직접 제공한 경계값이 아닙니다."
            : "유효한 mMaxRPM 미확인: 초기 눈금만 표시하고 경고 기준은 생성하지 않습니다.";
        public static bool IsValidEngineMaximum(double? rpm) => rpm is double value && double.IsFinite(value) && value >= 1000 && value <= 100000;
        public static string ProfileVehicleName(TelemetrySnapshot? session)
        {
            if (session == null) return "";
            for (int i = 0; i < session.Participants.Count; i++)
            {
                var participant = session.Participants[i];
                if (participant.Index == session.ViewedParticipantIndex && !string.IsNullOrWhiteSpace(participant.VehicleName))
                    return participant.VehicleName;
            }
            return session.RootCarName;
        }
        public AvanteRpmScale(double maximum, double yellowStart, double redStart, bool calibrated, bool vehicleProfile = false, bool automaticMaximum = false)
        { Maximum = maximum; YellowStart = yellowStart; RedStart = redStart; Calibrated = calibrated; VehicleProfile = vehicleProfile; AutomaticMaximum = automaticMaximum; }
        public bool Equals(AvanteRpmScale other) => Maximum == other.Maximum && YellowStart == other.YellowStart && RedStart == other.RedStart && Calibrated == other.Calibrated && VehicleProfile == other.VehicleProfile && AutomaticMaximum == other.AutomaticMaximum;
        public override bool Equals(object? other) => other is AvanteRpmScale scale && Equals(scale);
        public override int GetHashCode() => HashCode.Combine(Maximum, YellowStart, RedStart, Calibrated, VehicleProfile, AutomaticMaximum);
        public static bool operator ==(AvanteRpmScale left, AvanteRpmScale right) => left.Equals(right);
        public static bool operator !=(AvanteRpmScale left, AvanteRpmScale right) => !left.Equals(right);
        // User-approved display policy, not game-provided warning boundaries. Vehicle names
        // no longer select built-in thresholds; only explicitly saved calibration overrides it.
        public static AvanteRpmScale Resolve(double? engineMaximum, AvanteRpmCalibration? calibration = null, string vehicleName = "")
        {
            if (calibration?.IsValid == true)
                return new AvanteRpmScale(calibration.AutomaticMaximum ? MaximumAboveRed(calibration.RedStart) : calibration.Maximum,
                    calibration.YellowStart, calibration.RedStart, true, false, calibration.AutomaticMaximum);
            if (!IsValidEngineMaximum(engineMaximum))
                return new AvanteRpmScale(8000, double.PositiveInfinity, double.PositiveInfinity, false);
            double engine = engineMaximum!.Value, red = engine * .97;
            return new AvanteRpmScale(MaximumAboveRed(red), engine * .90, red, false, false, true);
        }
        public double Angle(double rpm) => 150 + (double.IsFinite(rpm) ? Math.Clamp(rpm / Maximum, 0, 1) : 0) * 240;
        public int Band(double rpm) => !double.IsFinite(rpm) || !HasWarningThresholds ? 0 : rpm >= RedStart ? 2 : rpm >= YellowStart ? 1 : 0;
        public int LitPairs(double rpm)
        {
            if (!double.IsFinite(rpm)) return 0;
            // Without warnings, white pairs show range progression without inventing a redline.
            if (!HasWarningThresholds) return (int)Math.Clamp(Math.Floor(rpm / Maximum * 5), 0, 5);
            // The outer warning gauge completes at redline, independently of dial headroom.
            // Keep the first three stages; the fourth fills through yellow and the fifth at red.
            if (rpm >= RedStart) return 5;
            double reference = rpm < YellowStart ? (YellowStart > 0 ? rpm / YellowStart * 5000 : 0)
                : 5000 + (rpm - YellowStart) / (RedStart - YellowStart) * 1000;
            return reference >= 5500 ? 4 : reference >= 4500 ? 3 : reference >= 4250 ? 2 : reference >= 4000 ? 1 : 0;
        }
    }
}
