using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.CompactTelemetry;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.FutureTelemetry;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Security;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Runtime;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Overlay;

namespace AMS2LeagueClient.Tests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var application = new AMS2LeagueClient.App();
            application.InitializeComponent();
            var tests = new (string Name, Action Test)[]
            {
                ("Official v14 activity metadata offsets", ActivityMetadataLayoutOffsets),
                ("Official v14 future telemetry offsets", FutureTelemetryLayoutOffsets),
                ("Official v14 future telemetry parses", FutureTelemetryParses),
                ("Official v14 root vehicle parses", RootVehicleTelemetryParses),
                ("Official v14 fastest timing parses", RootFastestTimingParses),
                ("Official v14 weather parses", WeatherTelemetryParses),
                ("Official v14 pit, snow and privacy metadata parses", SessionActivityMetadataParses),
                ("RaceControl history alone stays hidden", RaceControlHistoryAloneHidden),
                ("RaceControl state-only card fits", RaceControlStateOnlyCardFits),
                ("Yellow semantics remain distinct", YellowSemanticsRemainDistinct),
                ("Relative distance trend colours persist", RelativeDistanceTrendColoursPersist),
                ("RaceControl clears AMS2 top-center alert", RaceControlLeftAuxiliaryPlacement),
                ("Compact UI metrics meet target", CompactUiMetricsMeetTarget),
                ("Compact anchors hold at target resolutions", CompactAnchorsHoldAtTargetResolutions),
                ("Independent layout profile scales and clamps", IndependentLayoutProfileScalesAndClamps),
                ("Timing rows expose class and current time", TimingRowsExposeClassAndCurrentTime),
                ("Timing tower removes redundant headers", TimingTowerRemovesRedundantHeaders),
                ("Overlay edit mode restores click-through", OverlayEditModeRestoresClickThrough),
                ("Multiplayer menu shows waiting overlay", MultiplayerMenuShowsWaitingOverlay),
                ("Multiplayer qualify-end transition shows waiting", MultiplayerQualifyEndShowsWaitingOverlay),
                ("Waiting overlay excludes single and replay", WaitingOverlayExcludesSingleAndReplay),
                ("Waiting overlay returns to gameplay", WaitingOverlayReturnsToGameplay),
                ("Remaining timer fallback is bounded to generation", RemainingTimerFallbackBounded),
                ("Waiting timer never fabricates countdown", WaitingTimerDoesNotFabricateCountdown),
                ("Session card uses observed terminal status", SessionCardUsesObservedTerminalStatus)
                ,("Packaged overlay XAML constructs", PackagedOverlayXamlConstructs)
                ,("Status window layout controls construct", StatusWindowLayoutControlsConstruct)
                ,("Public launch shows first-run status", PublicLaunchShowsStatus)
                ,("Background launch is explicit", BackgroundLaunchIsExplicit)
                ,("Fresh user has no pairing identity", FreshUserHasNoPairingIdentity)
                ,("Pairing credential is DPAPI protected", PairingCredentialIsProtected)
                ,("Unpair clears protected credential", UnpairClearsCredential)
                ,("Fresh install enrolls anonymously before upload", FreshInstallEnrollsBeforeUpload)
                ,("Two anonymous installs receive independent credentials", TwoAnonymousInstallsRemainIndependent)
                ,("Anonymous enrollment status never claims upload is disabled", AnonymousEnrollmentStatusIsAccurate)
                ,("Telemetry gzip HTTP contract is exact", TelemetryGzipHttpContractIsExact)
                ,("Compact telemetry gzip HTTP contract is exact", CompactTelemetryGzipHttpContractIsExact)
                ,("Activity runtime automatically uploads pending telemetry chunks", ActivityRuntimeUploadsPendingTelemetry)
            };
            int passed = 0;
            foreach ((string name, Action test) in tests)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("FAIL " + name);
                    Console.Error.WriteLine(exception);
                }
            }

            Console.WriteLine("RESULT: " + passed + " passed, " + (tests.Length - passed) + " failed, " + tests.Length + " total");
            application.Shutdown();
            return passed == tests.Length ? 0 : 1;
        }

        private static void PackagedOverlayXamlConstructs()
        {
            var window = new OverlayWindow(false);
            window.Close();
        }

        private static void StatusWindowLayoutControlsConstruct()
        {
            var window = new ClientStatusWindow(new ClientStatusViewModel());
            window.SetLayoutEditState(true, "레이아웃 편집 중");
            window.SetLayoutComponentStates(OverlayComponentKeys.All.ToDictionary(key => key, _ => true));
            window.SetLayoutEditState(false, "레이아웃이 잠겼습니다.");
            window.Close();
        }

        private static void ActivityMetadataLayoutOffsets()
        {
            AssertEqual(6444, SharedMemoryLayout.CarName);
            AssertEqual(6508, SharedMemoryLayout.CarClassName);
            AssertEqual(6744, SharedMemoryLayout.PersonalFastestLapTime);
            AssertEqual(6748, SharedMemoryLayout.WorldFastestLapTime);
            AssertEqual(6776, SharedMemoryLayout.PersonalFastestSector1Time);
            AssertEqual(6780, SharedMemoryLayout.PersonalFastestSector2Time);
            AssertEqual(6784, SharedMemoryLayout.PersonalFastestSector3Time);
            AssertEqual(6788, SharedMemoryLayout.WorldFastestSector1Time);
            AssertEqual(6792, SharedMemoryLayout.WorldFastestSector2Time);
            AssertEqual(6796, SharedMemoryLayout.WorldFastestSector3Time);
            AssertEqual(7292, SharedMemoryLayout.AmbientTemperature);
            AssertEqual(7296, SharedMemoryLayout.TrackTemperature);
            AssertEqual(7300, SharedMemoryLayout.RainDensity);
            AssertEqual(7304, SharedMemoryLayout.WindSpeed);
            AssertEqual(7308, SharedMemoryLayout.WindDirectionX);
            AssertEqual(7312, SharedMemoryLayout.WindDirectionY);
            AssertEqual(7316, SharedMemoryLayout.CloudBrightness);
            AssertEqual(19248, SharedMemoryLayout.EnforcedPitStopLap);
            AssertEqual(20572, SharedMemoryLayout.SnowDensity);
            AssertEqual(20692, SharedMemoryLayout.SessionIsPrivate);
            AssertEqual(20688, SharedMemoryLayout.YellowFlagState);
            AssertEqual(20696, SharedMemoryLayout.LaunchStage);
            AssertEqual(20700, SharedMemoryLayout.RequiredBytes);
        }

        private static void RootVehicleTelemetryParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder().SetRootVehicle("McLaren 720S GT3 Evo", "GT3 Gen2"));
            AssertEqual("McLaren 720S GT3 Evo", snapshot.RootCarName);
            AssertEqual("GT3 Gen2", snapshot.RootCarClassName);
        }

        private static void FutureTelemetryLayoutOffsets()
        {
            AssertEqual(68, SharedMemoryLayout.ParticipantWorldPosition);
            AssertEqual(80, SharedMemoryLayout.ParticipantCurrentLapDistance);
            AssertEqual(100, SharedMemoryLayout.ParticipantSize);
            AssertEqual(6428, SharedMemoryLayout.UnfilteredThrottle);
            AssertEqual(6440, SharedMemoryLayout.UnfilteredClutch);
            AssertEqual(6736, SharedMemoryLayout.SplitTime);
            AssertEqual(6816, SharedMemoryLayout.CarFlags);
            AssertEqual(6848, SharedMemoryLayout.Speed);
            AssertEqual(6876, SharedMemoryLayout.Gear);
            AssertEqual(6908, SharedMemoryLayout.Orientation);
            AssertEqual(6956, SharedMemoryLayout.LocalAcceleration);
            AssertEqual(6992, SharedMemoryLayout.TyreFlags);
            AssertEqual(7136, SharedMemoryLayout.TyreWear);
            AssertEqual(7280, SharedMemoryLayout.CrashState);
            AssertEqual(7324, SharedMemoryLayout.WheelLocalPositionY);
            AssertEqual(7372, SharedMemoryLayout.AirPressure);
            AssertEqual(7388, SharedMemoryLayout.EngineSpeed);
            AssertEqual(7404, SharedMemoryLayout.HandBrake);
            AssertEqual(10032, SharedMemoryLayout.Orientations);
            AssertEqual(10800, SharedMemoryLayout.Speeds);
            AssertEqual(19380, SharedMemoryLayout.BrakeBias);
            AssertEqual(19388, SharedMemoryLayout.TyreCompound);
            AssertEqual(20316, SharedMemoryLayout.Nationalities);
            AssertEqual(20584, SharedMemoryLayout.TyreTempLeft);
            AssertEqual(20632, SharedMemoryLayout.DrsState);
            AssertEqual(20668, SharedMemoryLayout.ErsDeploymentMode);
            AssertEqual(20676, SharedMemoryLayout.ClutchTemp);
            AssertEqual(20684, SharedMemoryLayout.ClutchOverheated);
            AssertEqual(20685, SharedMemoryLayout.ClutchSlipping);
            AssertEqual(20688, SharedMemoryLayout.YellowFlagState);
            AssertEqual(20696, SharedMemoryLayout.LaunchStage);
            AssertEqual(20700, SharedMemoryLayout.RequiredBytes);
        }

        private static void FutureTelemetryParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder()
                    .SetParticipantMotion(3, 101.25f, 12.5f, -44.75f, 0.01f, 1.02f, -0.03f, 66.6f)
                    .SetExtendedTimingAndTrack(1.234f, "인터라고스", "그랑프리")
                    .SetViewedVehicleTelemetry()
                    .SetTyreTelemetry(0));

            ParticipantSnapshot participant = snapshot.Participants[3];
            AssertEqual(new TelemetryVector3(101.25f, 12.5f, -44.75f), participant.WorldPosition);
            AssertEqual(new TelemetryVector3(0.01f, 1.02f, -0.03f), participant.Orientation);
            AssertEqual(66.6f, participant.SpeedMetresPerSecond);
            AssertEqual((uint)82, participant.NationalityRaw);
            AssertEqual(1.234f, snapshot.SplitTime);
            AssertEqual("인터라고스", snapshot.TranslatedTrackLocation);
            AssertEqual("그랑프리", snapshot.TranslatedTrackVariation);

            ViewedVehicleTelemetrySnapshot telemetry = snapshot.ViewedVehicleTelemetry
                ?? throw new InvalidOperationException("Viewed vehicle telemetry was not parsed.");
            AssertEqual(0.71f, telemetry.UnfilteredThrottle);
            AssertEqual(0.22f, telemetry.UnfilteredBrake);
            AssertEqual(-0.33f, telemetry.UnfilteredSteering);
            AssertEqual(0.44f, telemetry.UnfilteredClutch);
            AssertEqual((uint)0x25, telemetry.CarFlagsRaw);
            AssertEqual(101.5f, telemetry.OilTemperatureCelsius);
            AssertEqual(345.6f, telemetry.OilPressureKPa);
            AssertEqual(91.2f, telemetry.WaterTemperatureCelsius);
            AssertEqual(222.3f, telemetry.WaterPressureKPa);
            AssertEqual(444.5f, telemetry.FuelPressureKPa);
            AssertEqual(0.63f, telemetry.FuelLevel);
            AssertEqual(110.0f, telemetry.FuelCapacityLitres);
            AssertEqual(72.25f, telemetry.SpeedMetresPerSecond);
            AssertEqual(7123.0f, telemetry.Rpm);
            AssertEqual(9000.0f, telemetry.MaxRpm);
            AssertEqual(0.24f, telemetry.Brake);
            AssertEqual(0.69f, telemetry.Throttle);
            AssertEqual(0.41f, telemetry.Clutch);
            AssertEqual(-0.31f, telemetry.Steering);
            AssertEqual(4, telemetry.Gear);
            AssertEqual(6, telemetry.NumGears);
            AssertEqual(123.45f, telemetry.OdometerKilometres);
            AssertTrue(telemetry.AntiLockActive);
            AssertEqual(9, telemetry.LastOpponentCollisionIndex);
            AssertEqual(12.75f, telemetry.LastOpponentCollisionMagnitude);
            AssertTrue(telemetry.BoostActive);
            AssertEqual(56.5f, telemetry.BoostAmount);
            AssertEqual(new TelemetryVector3(0.1f, 0.2f, 0.3f), telemetry.Orientation);
            AssertEqual(new TelemetryVector3(1.1f, 1.2f, 1.3f), telemetry.LocalVelocity);
            AssertEqual(new TelemetryVector3(2.1f, 2.2f, 2.3f), telemetry.WorldVelocity);
            AssertEqual(new TelemetryVector3(3.1f, 3.2f, 3.3f), telemetry.AngularVelocity);
            AssertEqual(new TelemetryVector3(4.1f, 4.2f, 4.3f), telemetry.LocalAcceleration);
            AssertEqual(new TelemetryVector3(5.1f, 5.2f, 5.3f), telemetry.WorldAcceleration);
            AssertEqual(new TelemetryVector3(6.1f, 6.2f, 6.3f), telemetry.ExtentsCentre);
            AssertEqual(777.25f, telemetry.EngineSpeedRadiansPerSecond);
            AssertEqual(498.5f, telemetry.EngineTorqueNewtonMetres);
            AssertEqual(0.17f, telemetry.FrontWing);
            AssertEqual(0.23f, telemetry.RearWing);
            AssertEqual(0.05f, telemetry.HandBrake);
            AssertEqual((uint)3, telemetry.CrashStateRaw);
            AssertEqual(0.12f, telemetry.AeroDamage);
            AssertEqual(0.08f, telemetry.EngineDamage);
            AssertEqual(0.57f, telemetry.BrakeBias);
            AssertEqual(1.42f, telemetry.TurboBoostPressure);
            AssertEqual((uint)0x18, telemetry.DrsStateRaw);
            AssertEqual(4, telemetry.AntiLockSetting);
            AssertEqual(3, telemetry.TractionControlSetting);
            AssertEqual(4, telemetry.ErsDeploymentModeRaw);
            AssertTrue(telemetry.ErsAutoModeEnabled);
            AssertEqual(355.5f, telemetry.ClutchTemperatureKelvin);
            AssertEqual(0.18f, telemetry.ClutchWear);
            AssertTrue(telemetry.ClutchOverheated);
            AssertTrue(telemetry.ClutchSlipping);
            AssertEqual(2, telemetry.LaunchStageRaw);
            AssertEqual(4, telemetry.Tyres.Count);

            TyreTelemetrySnapshot tyre = telemetry.Tyres[0];
            AssertEqual((uint)7, tyre.FlagsRaw);
            AssertEqual((uint)10, tyre.TerrainRaw);
            AssertEqual(0.11f, tyre.LocalY);
            AssertEqual(22.2f, tyre.RevolutionsPerSecond);
            AssertEqual(83.3f, tyre.TemperatureCelsius);
            AssertEqual(0.04f, tyre.HeightAboveGround);
            AssertEqual(0.81f, tyre.Wear);
            AssertEqual(0.02f, tyre.BrakeDamage);
            AssertEqual(0.03f, tyre.SuspensionDamage);
            AssertEqual(612.5f, tyre.BrakeTemperatureCelsius);
            AssertEqual(355.1f, tyre.TreadTemperatureKelvin);
            AssertEqual(354.2f, tyre.LayerTemperatureKelvin);
            AssertEqual(353.3f, tyre.CarcassTemperatureKelvin);
            AssertEqual(352.4f, tyre.RimTemperatureKelvin);
            AssertEqual(351.5f, tyre.InternalAirTemperatureKelvin);
            AssertEqual(-0.21f, tyre.WheelLocalPositionY);
            AssertEqual(0.06f, tyre.SuspensionTravelMetres);
            AssertEqual(-0.7f, tyre.SuspensionVelocity);
            AssertEqual(27.8f, tyre.AirPressurePsi);
            AssertEqual("Soft Slick", tyre.Compound);
            AssertEqual(80.1f, tyre.LeftTemperatureCelsius);
            AssertEqual(81.2f, tyre.CenterTemperatureCelsius);
            AssertEqual(82.3f, tyre.RightTemperatureCelsius);
            AssertEqual(7.4f, tyre.RideHeightCentimetres);
        }

        private static void RootFastestTimingParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder().SetRootFastestTiming(
                    91.234f,
                    89.876f,
                    30.111f,
                    31.222f,
                    29.901f,
                    29.501f,
                    30.402f,
                    29.973f));
            AssertEqual(91.234f, snapshot.PersonalFastestLapTime);
            AssertEqual(89.876f, snapshot.WorldFastestLapTime);
            AssertEqual(30.111f, snapshot.PersonalFastestSector1Time);
            AssertEqual(31.222f, snapshot.PersonalFastestSector2Time);
            AssertEqual(29.901f, snapshot.PersonalFastestSector3Time);
            AssertEqual(29.501f, snapshot.WorldFastestSector1Time);
            AssertEqual(30.402f, snapshot.WorldFastestSector2Time);
            AssertEqual(29.973f, snapshot.WorldFastestSector3Time);
        }

        private static void WeatherTelemetryParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder().SetWeather(22.5f, 31.75f, 0.42f, 7.25f, -0.6f, 0.8f, 0.35f, 0.12f));
            AssertEqual(22.5f, snapshot.AmbientTemperature);
            AssertEqual(31.75f, snapshot.TrackTemperature);
            AssertEqual(0.42f, snapshot.RainDensity);
            AssertEqual(7.25f, snapshot.WindSpeed);
            AssertEqual(-0.6f, snapshot.WindDirectionX);
            AssertEqual(0.8f, snapshot.WindDirectionY);
            AssertEqual(0.35f, snapshot.CloudBrightness);
            AssertEqual(0.12f, snapshot.SnowDensity);
        }

        private static void SessionActivityMetadataParses()
        {
            TelemetrySnapshot snapshot = Parse(
                new RawFixtureBuilder()
                    .SetWeather(20, 26, 0, 3, 1, 0, 0.9f, 0.27f)
                    .SetSessionActivityMetadata(7, true));
            AssertEqual(7, snapshot.EnforcedPitStopLap);
            AssertEqual(0.27f, snapshot.SnowDensity);
            AssertEqual(true, snapshot.SessionIsPrivate);

            TelemetrySnapshot publicSession = Parse(
                new RawFixtureBuilder().SetSessionActivityMetadata(-1, false));
            AssertEqual(-1, publicSession.EnforcedPitStopLap);
            AssertEqual(false, publicSession.SessionIsPrivate);

            TelemetrySnapshot fullCourseYellow = Parse(
                new RawFixtureBuilder().SetYellowFlagState(YellowFlagState.PitsClosed));
            AssertEqual(YellowFlagState.PitsClosed, fullCourseYellow.KnownYellowFlagState);
        }

        private static void RaceControlHistoryAloneHidden()
        {
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            DateTimeOffset t = FixedTime();
            ObserveControl(analyzer, new RawFixtureBuilder(), t);
            var penalty = new RawFixtureBuilder().SetParticipantControl(1, PitSchedule.DriveThrough);
            RaceControlUpdate active = ObserveControl(analyzer, penalty, t.AddSeconds(1));
            AssertTrue(RaceControlViewModel.FromUpdate(active).IsVisible);

            RaceControlUpdate expired = ObserveControl(analyzer, penalty, t.AddSeconds(7));
            AssertNull(expired.ActiveEvent);
            AssertEqual(1, expired.History.Count);
            RaceControlViewModel hidden = RaceControlViewModel.FromUpdate(expired);
            AssertFalse(hidden.IsVisible);
            AssertTrue(hidden.HistoryText.Contains("드라이브스루", StringComparison.Ordinal));
        }

        private static void RaceControlStateOnlyCardFits()
        {
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            DateTimeOffset t = FixedTime();
            ObserveControl(analyzer, new RawFixtureBuilder(), t);
            var yellow = new RawFixtureBuilder().SetRootControl(FlagColour.Yellow);
            ObserveControl(analyzer, yellow, t.AddSeconds(1));
            RaceControlUpdate persistent = ObserveControl(analyzer, yellow, t.AddSeconds(7));
            RaceControlViewModel view = RaceControlViewModel.FromUpdate(persistent);

            AssertNull(persistent.ActiveEvent);
            AssertTrue(view.IsVisible);
            AssertFalse(view.IsExpanded);
            AssertEqual("! 황색기", view.StateLabel);
            AssertTrue(AuxiliaryOverlayLayoutMetrics.RaceControlCompactHeight >= 64);
            AssertTrue(AuxiliaryOverlayLayoutMetrics.RaceControlExpandedHeight > AuxiliaryOverlayLayoutMetrics.RaceControlCompactHeight);
        }

        private static void YellowSemanticsRemainDistinct()
        {
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            DateTimeOffset t = FixedTime();
            ObserveControl(analyzer, new RawFixtureBuilder(), t);
            var doubleYellow = new RawFixtureBuilder().SetRootControl(FlagColour.DoubleYellow);
            RaceControlUpdate active = ObserveControl(analyzer, doubleYellow, t.AddSeconds(1));
            AssertEqual("!! 이중 황색기", active.ActiveEvent?.Title);

            RaceControlUpdate persistent = ObserveControl(analyzer, doubleYellow, t.AddSeconds(7));
            AssertEqual("!! 이중 황색기", RaceControlViewModel.FromUpdate(persistent).StateLabel);

            doubleYellow.SetYellowFlagState(YellowFlagState.Pending);
            RaceControlUpdate fullCourse = ObserveControl(analyzer, doubleYellow, t.AddSeconds(8));
            AssertEqual(RaceControlEventType.FullCourseYellow, fullCourse.ActiveEvent?.Type);
            AssertEqual("전 코스 황색기", fullCourse.ActiveEvent?.Title);
            AssertEqual("전 코스 황색기", RaceControlViewModel.FromUpdate(fullCourse).StateLabel);
            AssertTrue((fullCourse.OverlayState & BroadcastOverlayState.FullCourseYellow) != 0);
            AssertFalse((fullCourse.OverlayState & BroadcastOverlayState.DoubleYellow) != 0);

            doubleYellow.SetYellowFlagState(YellowFlagState.None);
            RaceControlUpdate localDoubleYellow = ObserveControl(analyzer, doubleYellow, t.AddSeconds(9));
            AssertEqual(RaceControlEventType.DoubleYellow, localDoubleYellow.ActiveEvent?.Type);
            AssertEqual("!! 이중 황색기", RaceControlViewModel.FromUpdate(localDoubleYellow).StateLabel);
            AssertFalse((localDoubleYellow.OverlayState & BroadcastOverlayState.FullCourseYellow) != 0);
            AssertTrue((localDoubleYellow.OverlayState & BroadcastOverlayState.DoubleYellow) != 0);
        }

        private static void RelativeDistanceTrendColoursPersist()
        {
            var tracker = new RelativeDistanceTrendTracker();
            var view = new OverlayViewModel
            {
                AheadParticipantIndex = 4,
                AheadDistanceMeters = 57,
                BehindParticipantIndex = 5,
                BehindDistanceMeters = 74
            };
            tracker.Apply(view, 1);
            AssertEqual(string.Empty, view.AheadDistanceTrendArrow);
            AssertEqual(string.Empty, view.BehindDistanceTrendArrow);

            view.AheadDistanceMeters = 59;
            view.BehindDistanceMeters = 72;
            tracker.Apply(view, 1);
            AssertEqual("▲", view.AheadDistanceTrendArrow);
            AssertEqual("#57D5FF", view.AheadDistanceColor);
            AssertEqual("▼", view.BehindDistanceTrendArrow);
            AssertEqual("#FF7777", view.BehindDistanceColor);

            tracker.Apply(view, 1);
            AssertEqual("▲", view.AheadDistanceTrendArrow);
            AssertEqual("▼", view.BehindDistanceTrendArrow);

            view.AheadParticipantIndex = 6;
            tracker.Apply(view, 1);
            AssertEqual(string.Empty, view.AheadDistanceTrendArrow);
            tracker.Apply(view, 2);
            AssertEqual(string.Empty, view.BehindDistanceTrendArrow);
        }

        private static void CompactUiMetricsMeetTarget()
        {
            AssertEqual(1.00, OverlayUiMetrics.TargetScale);
            AssertTrue(OverlayUiMetrics.FontDriverName >= 24);
            AssertTrue(OverlayUiMetrics.RowPitch >= 37);
            AssertTrue(OverlayUiMetrics.TowerHeight + OverlayUiMetrics.ComponentGap + OverlayUiMetrics.RelativeHeight <= 700);
            AssertEqual(15, LeftTowerLayoutMetrics.RankingRows);
            AssertTrue(LeftTowerLayoutMetrics.RequiredHeight <= LeftTowerLayoutMetrics.DesiredHeight);
        }

        private static void CompactAnchorsHoldAtTargetResolutions()
        {
            foreach ((int width, int height) in new[] { (1920, 1080), (2560, 1440), (3440, 1440) })
            {
                OverlayComponentLayout layout = OverlayComponentLayoutCalculator.Calculate(width, height, 96, false, true);
                int expectedLeft = Math.Max(8, (int)Math.Round(width * 0.004));
                int expectedTop = Math.Max(8, (int)Math.Round(height * 0.008));
                int expectedBottom = (int)Math.Round(height * 0.09);
                AssertEqual(expectedLeft, layout.Timing.X);
                AssertEqual(expectedTop, layout.Timing.Y);
                AssertEqual(layout.Timing.Right + OverlayUiMetrics.ComponentGap, layout.Session.X);
                AssertEqual(layout.Session.X, layout.RaceControl.X);
                AssertEqual(layout.Timing.X, layout.Relative.X);
                AssertEqual(layout.Timing.Bottom + OverlayUiMetrics.ComponentGap, layout.Relative.Y);
                AssertEqual(layout.Session.X, layout.LapTiming.X);
                AssertEqual(layout.Session.Bottom + OverlayUiMetrics.ComponentGap, layout.LapTiming.Y);
                AssertEqual(layout.LapTiming.Bottom + OverlayUiMetrics.ComponentGap, layout.RaceControl.Y);
                AssertEqual(width / 2, layout.EventCard.X + (layout.EventCard.Width / 2));
                AssertEqual(height - expectedBottom, layout.EventCard.Bottom);
                AssertEqual(expectedLeft, layout.Waiting.X);
                AssertEqual(expectedTop, layout.Waiting.Y);
                AssertTrue(layout.Timing.Right < width);
                AssertTrue(layout.Session.Right < width);
                AssertTrue(layout.RaceControl.Right < width);
                AssertTrue(layout.EventCard.X >= 0 && layout.EventCard.Right <= width);
            }
        }

        private static void IndependentLayoutProfileScalesAndClamps()
        {
            var profile = new OverlayLayoutProfile();
            profile.Capture(OverlayComponentKeys.RelativeDrivers, new OverlayBounds(192, 108, 460, 96), 1920, 1080);
            OverlayBounds scaled = profile.Resolve(
                OverlayComponentKeys.RelativeDrivers,
                new OverlayBounds(0, 0, 1, 1),
                3840,
                2160);
            AssertEqual(384, scaled.X);
            AssertEqual(216, scaled.Y);
            AssertEqual(920, scaled.Width);
            AssertEqual(192, scaled.Height);

            profile.Components[OverlayComponentKeys.LapTiming] = new NormalizedOverlayBounds
            {
                X = 2,
                Y = 2,
                Width = 0.5,
                Height = 0.5
            };
            OverlayBounds clamped = profile.Resolve(
                OverlayComponentKeys.LapTiming,
                new OverlayBounds(0, 0, 1, 1),
                1920,
                1080);
            AssertEqual(960, clamped.X);
            AssertEqual(540, clamped.Y);
            AssertEqual(960, clamped.Width);
            AssertEqual(540, clamped.Height);
        }

        private static void TimingRowsExposeClassAndCurrentTime()
        {
            OverlayShellViewModel shell = DemoSnapshotFactory.CreateShell(false);
            RankingRowViewModel player = shell.Timing.RankingRows.Single(row => row.IsPlayer);
            AssertEqual("GT3", player.Class);
            AssertEqual("1:42.881", player.CurrentTime);
            AssertTrue(OverlayUiMetrics.FontDriverName > OverlayUiMetrics.FontTitle);
        }

        private static void TimingTowerRemovesRedundantHeaders()
        {
            var view = new OverlayHudView();
            view.SetViewModel(DemoSnapshotFactory.CreateShell(false).Timing);
            view.Measure(new Size(OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.Arrange(new Rect(0, 0, OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.UpdateLayout();
            string[] text = DescendantText(view).ToArray();
            AssertFalse(text.Contains("AMS2 LEAGUE · TIMING", StringComparer.Ordinal));
            AssertFalse(text.Contains("리그 순위", StringComparer.Ordinal));
            AssertTrue(text.Contains("GT3", StringComparer.Ordinal));
            AssertTrue(text.Contains("1:42.881", StringComparer.Ordinal));
        }

        private static void OverlayEditModeRestoresClickThrough()
        {
            string root = Path.Combine(Path.GetTempPath(), "ams2-layout-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = new OverlayWindow(false, Path.Combine(root, "overlay-layout.json"));
            try
            {
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                window.ShowDemoAt(-5000, -5000, 96);
                AssertTrue(window.GetStyleState().ClickThrough);
            AssertTrue(window.BeginLayoutEdit());
            AssertFalse(window.GetStyleState().ClickThrough);
            window.SetComponentEnabled(OverlayComponentKeys.RelativeDrivers, false);
            AssertFalse(window.IsComponentEnabled(OverlayComponentKeys.RelativeDrivers));
            window.EndLayoutEdit(true);
            AssertTrue(window.GetStyleState().ClickThrough);
            AssertTrue(File.Exists(Path.Combine(root, "overlay-layout.json")));
            AssertTrue(File.ReadAllText(Path.Combine(root, "overlay-layout.json")).Contains("\"relativeDrivers\": false", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
                Directory.Delete(root, true);
            }
        }

        private static IEnumerable<string> DescendantText(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is TextBlock textBlock) yield return textBlock.Text;
                foreach (string value in DescendantText(child)) yield return value;
            }
        }

        private static void RaceControlLeftAuxiliaryPlacement()
        {
            int towerLeft = Math.Max(8, (int)Math.Round(3440 * 0.004));
            int auxiliaryLeft = towerLeft + LeftTowerLayoutMetrics.Width + LeftTowerLayoutMetrics.SessionGap;
            int towerTop = Math.Max(8, (int)Math.Round(1440 * 0.008));
            int raceTop = towerTop + AuxiliaryOverlayLayoutMetrics.RaceControlTopOffset;

            AssertEqual(AuxiliaryOverlayLayoutMetrics.SessionHeight + AuxiliaryOverlayLayoutMetrics.LapTimingHeight + (LeftTowerLayoutMetrics.SessionGap * 2),
                AuxiliaryOverlayLayoutMetrics.RaceControlTopOffset);
            AssertTrue(auxiliaryLeft + AuxiliaryOverlayLayoutMetrics.RaceControlExpandedWidth < 3440 / 2);
            AssertEqual(towerTop + AuxiliaryOverlayLayoutMetrics.SessionHeight + AuxiliaryOverlayLayoutMetrics.LapTimingHeight + (LeftTowerLayoutMetrics.SessionGap * 2), raceTop);
        }

        private static void MultiplayerMenuShowsWaitingOverlay()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSession(SessionState.Qualify)
                .SetSessionTiming(15, 0, 210.17f)
                .SetParticipantVehicle(1, "Camaro SafetyCar", "SafetyCar");
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(Parse(fixture), 0, FixedTime());

            AssertEqual(MultiplayerOverlayMode.Waiting, decision.Mode);
            AssertNotNull(decision.Waiting);
            AssertEqual("멀티플레이어 세션 대기", decision.Waiting?.Title);
            AssertEqual("예선", decision.Waiting?.SessionLabel);
            AssertEqual("리그 3 / 원본 4", decision.Waiting?.ParticipantCountText);
            AssertEqual("3:31", decision.Waiting?.RemainingValue);
        }

        private static void MultiplayerQualifyEndShowsWaitingOverlay()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetGameState(GameState.InGamePlaying)
                .SetSession(SessionState.Qualify)
                .SetGlobalRaceState(RaceState.NotStarted)
                .SetSessionTiming(15, 0, -1);
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(Parse(fixture), 0, FixedTime());

            AssertEqual(MultiplayerOverlayMode.Waiting, decision.Mode);
            AssertEqual("MULTIPLAYER_SESSION_TRANSITION", decision.Reason);
            AssertEqual("세션 종료 대기", decision.Waiting?.RemainingValue);
        }

        private static void WaitingOverlayExcludesSingleAndReplay()
        {
            var controller = new MultiplayerWaitingOverlayController();
            TelemetrySnapshot single = Parse(new RawFixtureBuilder(1)
                .SetViewedIndex(0)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSessionTiming(15, 0, 210));
            TelemetrySnapshot replay = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameReplay)
                .SetSessionTiming(15, 0, 210));

            AssertEqual(MultiplayerOverlayMode.Hidden, controller.Observe(single, 0, FixedTime()).Mode);
            AssertEqual(MultiplayerOverlayMode.Hidden, controller.Observe(replay, 1, FixedTime()).Mode);
        }

        private static void WaitingOverlayReturnsToGameplay()
        {
            var controller = new MultiplayerWaitingOverlayController();
            DateTimeOffset t = FixedTime();
            TelemetrySnapshot waiting = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSessionTiming(15, 0, 210), t);
            TelemetrySnapshot playing = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGamePlaying)
                .SetSessionTiming(15, 0, 209), t.AddSeconds(1));

            AssertEqual(MultiplayerOverlayMode.Waiting, controller.Observe(waiting, 0, t).Mode);
            MultiplayerOverlayDecision resumed = controller.Observe(playing, 1, t.AddSeconds(1));
            AssertEqual(MultiplayerOverlayMode.Gameplay, resumed.Mode);
            AssertTrue(resumed.EffectiveRemainingSeconds.HasValue);
            AssertEqual(209f, resumed.EffectiveRemainingSeconds.GetValueOrDefault());
        }

        private static void RemainingTimerFallbackBounded()
        {
            var controller = new MultiplayerWaitingOverlayController();
            DateTimeOffset t = FixedTime();
            TelemetrySnapshot valid = Parse(new RawFixtureBuilder(4).SetSessionTiming(15, 0, 210), t);
            TelemetrySnapshot transient = Parse(new RawFixtureBuilder(4).SetSessionTiming(15, 0, -1), t.AddSeconds(1));

            controller.Observe(valid, 4, t);
            MultiplayerOverlayDecision held = controller.Observe(transient, 4, t.AddSeconds(1));
            AssertTrue(held.EffectiveRemainingSeconds.HasValue);
            AssertEqual(210f, held.EffectiveRemainingSeconds.GetValueOrDefault());
            AssertEqual("GAMEPLAY_TIMER_TRANSIENT", held.Reason);
            ParticipantSnapshot local = ResolveLocal(transient);
            OverlayViewModel timing = OverlayViewModel.Build(
                transient,
                local,
                Classify(transient),
                30,
                20,
                false,
                "TEST",
                eventTimeRemainingOverride: held.EffectiveRemainingSeconds,
                eventTimeRemainingTextOverride: held.RemainingDisplayTextOverride);
            AssertEqual("3:30", OverlayShellViewModel.Build(transient, timing, null, false).Session.PrimaryValue);

            MultiplayerOverlayDecision expired = controller.Observe(transient, 4, t.AddSeconds(3.1));
            AssertNull(expired.EffectiveRemainingSeconds);
            AssertEqual("종료 처리 중", expired.RemainingDisplayTextOverride);
            controller.Observe(valid, 4, t.AddSeconds(4));
            MultiplayerOverlayDecision nextGeneration = controller.Observe(transient, 5, t.AddSeconds(5));
            AssertNull(nextGeneration.EffectiveRemainingSeconds);
        }

        private static void WaitingTimerDoesNotFabricateCountdown()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetSession(SessionState.Qualify)
                .SetGlobalRaceState(RaceState.NotStarted)
                .SetSessionTiming(15, 0, -1);
            var controller = new MultiplayerWaitingOverlayController();

            MultiplayerOverlayDecision unknown = controller.Observe(Parse(fixture), 0, FixedTime());
            AssertEqual("상태", unknown.Waiting?.RemainingLabel);
            AssertEqual("세션 종료 대기", unknown.Waiting?.RemainingValue);
            AssertNull(unknown.EffectiveRemainingSeconds);
        }

        private static void SessionCardUsesObservedTerminalStatus()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetSessionTiming(15, 0, -1)
                .SetGlobalRaceState(RaceState.Finished);
            TelemetrySnapshot snapshot = Parse(fixture);
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(snapshot, 0, FixedTime());
            ParticipantSnapshot local = ResolveLocal(snapshot);
            LeagueClassification league = Classify(snapshot);
            OverlayViewModel timing = OverlayViewModel.Build(
                snapshot,
                local,
                league,
                30,
                20,
                false,
                "TEST",
                eventTimeRemainingOverride: decision.EffectiveRemainingSeconds,
                eventTimeRemainingTextOverride: decision.RemainingDisplayTextOverride);
            SessionInfoViewModel session = OverlayShellViewModel.Build(snapshot, timing, null, false).Session;

            AssertEqual(MultiplayerOverlayMode.Gameplay, decision.Mode);
            AssertEqual("세션 종료", decision.RemainingDisplayTextOverride);
            AssertEqual("세션 종료", session.PrimaryValue);
        }

        private static void PublicLaunchShowsStatus()
        {
            ClientStartupPolicy policy = ClientStartupPolicy.FromArguments(Array.Empty<string>());
            AssertTrue(policy.ShowStatusWindow);
            AssertTrue(policy.ShowStatusWindowActivated);
            AssertFalse(policy.IsBackgroundStartup);
            AssertFalse(policy.Diagnostic);
        }

        private static void BackgroundLaunchIsExplicit()
        {
            ClientStartupPolicy policy = ClientStartupPolicy.FromArguments(new[] { "--background" });
            AssertFalse(policy.ShowStatusWindow);
            AssertFalse(policy.ShowStatusWindowActivated);
            AssertTrue(policy.IsBackgroundStartup);
        }

        private static void FreshUserHasNoPairingIdentity()
        {
            WithTemporaryDirectory(directory => AssertEqual(string.Empty, PairingTokenStore.Load(directory)));
        }

        private static void PairingCredentialIsProtected()
        {
            WithTemporaryDirectory(directory =>
            {
                string token = "release020_pairing_test_0123456789abcdef";
                PairingTokenStore.Save(directory, token);
                string path = PairingTokenStore.ResolvePath(directory);
                AssertTrue(File.Exists(path));
                byte[] stored = File.ReadAllBytes(path);
                AssertFalse(Encoding.UTF8.GetString(stored).Contains(token, StringComparison.Ordinal));
                AssertEqual(token, PairingTokenStore.Load(directory));
            });
        }

        private static void UnpairClearsCredential()
        {
            WithTemporaryDirectory(directory =>
            {
                PairingTokenStore.Save(directory, "release020_unpair_test_0123456789abcdef");
                PairingTokenStore.Clear(directory);
                AssertEqual(string.Empty, PairingTokenStore.Load(directory));
            });
        }

        private static void FreshInstallEnrollsBeforeUpload()
        {
            WithTemporaryDirectory(directory =>
            {
                const string installationId = "client-anonymous-fixture-0001";
                const string token = "anonymous_fixture_token_00000001";
                string configPath = Path.Combine(directory, ActivityConnectionOptions.DefaultFileName);
                ActivityConnectionOptions options = ActivityConnectionOptions.Load(configPath);
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                var handler = new EnrollmentFixtureHandler(installationId, token);
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, installationId, "0.2.1", http);
                using var queueDirectory = new TemporaryClientDirectory();
                var queue = new ActivityUploadQueue(queueDirectory.Root);
                ActivityUploadItem item = queue.Enqueue(
                    "witness-enroll-fixture",
                    Cafe24ActivityUploadTransport.SessionWitnessEndpoint,
                    "witness:enrollment-fixture-0001",
                    "{\"schema\":\"ams2-session-witness-v1\"}").Item;

                ActivityUploadTransportResult result = transport.SendAsync(item, CancellationToken.None).GetAwaiter().GetResult();

                AssertEqual(201, result.StatusCode);
                AssertEqual(1, handler.EnrollmentCalls);
                AssertEqual(1, handler.UploadCalls);
                AssertEqual("Bearer " + token, handler.UploadAuthorization);
                AssertEqual("Bearer " + token, handler.UploadCompatibilityAuthorization);
                AssertEqual(token, PairingTokenStore.Load(directory));
                AssertFalse(Encoding.UTF8.GetString(File.ReadAllBytes(PairingTokenStore.ResolvePath(directory)))
                    .Contains(token, StringComparison.Ordinal));
            });
        }

        private static void TwoAnonymousInstallsRemainIndependent()
        {
            string firstToken = EnrollOnly("client-anonymous-alpha-0001", "anonymous_alpha_token_0000000001");
            string secondToken = EnrollOnly("client-anonymous-beta-00002", "anonymous_beta_token_00000000002");
            AssertFalse(string.Equals(firstToken, secondToken, StringComparison.Ordinal));
        }

        private static void AnonymousEnrollmentStatusIsAccurate()
        {
            var status = new ClientStatusViewModel("0.2.1");
            status.SetAccount(false, true);
            AssertTrue(status.AccountText.Contains("자동 등록 대기", StringComparison.Ordinal));
            AssertFalse(status.AccountText.Contains("비활성", StringComparison.Ordinal));
            status.SetAccount(true, true);
            AssertTrue(status.AccountText.Contains("기록 전송 활성", StringComparison.Ordinal));
        }

        private static void TelemetryGzipHttpContractIsExact()
        {
            WithTemporaryDirectory(directory =>
            {
                const string installationId = "client-telemetry-http-fixture-0001";
                const string token = "telemetry_fixture_token_00000001";
                string telemetryRoot = Path.Combine(directory, "future-telemetry");
                string metadataPath = CreatePendingTelemetryChunk(telemetryRoot);
                var queue = new TelemetryChunkUploadQueue(telemetryRoot);
                TelemetryChunkUploadItem item = queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                ActivityConnectionOptions options = ActivityConnectionOptions.Load(
                    Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                var handler = new EnrollmentFixtureHandler(installationId, token);
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(
                    options,
                    installationId,
                    "0.2.2",
                    http);

                TelemetryChunkUploadTransportResult result = transport
                    .SendTelemetryChunkAsync(item, CancellationToken.None)
                    .GetAwaiter().GetResult();

                AssertTrue(result.Success);
                AssertEqual(201, result.HttpStatus);
                AssertEqual(1, handler.EnrollmentCalls);
                AssertEqual(1, handler.TelemetryCalls);
                AssertEqual("Bearer " + token, handler.TelemetryAuthorization);
                AssertEqual("Bearer " + token, handler.TelemetryCompatibilityAuthorization);
                AssertEqual("gzip", handler.TelemetryContentEncoding);
                AssertEqual("application/json", handler.TelemetryContentType);
                AssertEqual("telemetry:" + item.Metadata.ChunkId, handler.TelemetryIdempotencyKey);
                AssertEqual(item.Metadata.PayloadSha256, handler.TelemetryPayloadSha256);
                AssertEqual(item.Metadata.CompressedSha256, handler.TelemetryCompressedSha256);
                AssertTrue(handler.TelemetryBody.Length > 2);
                AssertEqual((byte)0x1f, handler.TelemetryBody[0]);
                AssertEqual((byte)0x8b, handler.TelemetryBody[1]);
                byte[] decoded;
                using (var source = new MemoryStream(handler.TelemetryBody, false))
                {
                    decoded = TelemetryChunkSerializer.Gunzip(source, 4 * 1024 * 1024);
                }
                TelemetryChunkEnvelope envelope = TelemetryChunkSerializer.Deserialize(decoded);
                AssertEqual(item.Metadata.ChunkId, envelope.ChunkId);
                AssertTrue(File.Exists(metadataPath));
            });
        }

        private static void ActivityRuntimeUploadsPendingTelemetry()
        {
            WithTemporaryDirectory(directory =>
            {
                string telemetryRoot = Path.Combine(directory, "future-telemetry");
                string metadataPath = CreatePendingTelemetryChunk(telemetryRoot);
                var transport = new DualUploadFixtureTransport();
                var logger = new FileLogger(Path.Combine(directory, "logs"));
                using (var runtime = new ActivityCaptureRuntime(
                    directory,
                    "client-runtime-upload-fixture-0001",
                    "0.2.2",
                    logger,
                    transport))
                {
                    bool sent = SpinWait.SpinUntil(
                        () => ReadTelemetryStatus(metadataPath) == TelemetryUploadStatus.SENT,
                        TimeSpan.FromSeconds(5));
                    AssertTrue(sent);
                }
                AssertEqual(1, transport.TelemetryCalls);
                AssertTrue(File.ReadAllText(logger.FilePath).Contains(
                    "FUTURE_TELEMETRY_UPLOAD_BATCH attempted=1 sent=1",
                    StringComparison.Ordinal));
            });
        }

        private static void CompactTelemetryGzipHttpContractIsExact()
        {
            WithTemporaryDirectory(directory =>
            {
                const string installationId = "client-compact-http-fixture-0001";
                const string token = "compact_fixture_token_0000000001";
                string telemetryRoot = Path.Combine(directory, "future-telemetry");
                CreatePendingCompactTelemetryChunk(telemetryRoot);
                var queue = new TelemetryChunkUploadQueue(telemetryRoot);
                TelemetryChunkUploadItem item = queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                ActivityConnectionOptions options = ActivityConnectionOptions.Load(
                    Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                var handler = new EnrollmentFixtureHandler(installationId, token);
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, installationId, "0.2.3-beta.1", http);

                TelemetryChunkUploadTransportResult result = transport
                    .SendTelemetryChunkAsync(item, CancellationToken.None)
                    .GetAwaiter().GetResult();

                AssertTrue(result.Success);
                AssertEqual(Cafe24ActivityUploadTransport.CompactTelemetryContentType, handler.TelemetryContentType);
                AssertEqual(item.Metadata.ChunkId, handler.TelemetryChunkId);
                AssertEqual(item.Metadata.SessionId, handler.TelemetrySessionId);
                AssertEqual(item.Metadata.AttemptId, handler.TelemetryAttemptId);
                AssertEqual(item.Metadata.Visibility.ToString(), handler.TelemetryVisibility);
                AssertEqual("0.2.3-beta.1", handler.TelemetryClientVersion);
                using var source = new MemoryStream(handler.TelemetryBody, false);
                CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(
                    TelemetryChunkSerializer.Gunzip(source, 4 * 1024 * 1024));
                AssertEqual(CompactTelemetrySchemaId.RaceEventV1, decoded.Block.SchemaId);
            });
        }

        private static void CreatePendingCompactTelemetryChunk(string telemetryRoot)
        {
            string directory = Path.Combine(telemetryRoot, "sessions", "fixture", "chunks", "compact", "story");
            Directory.CreateDirectory(directory);
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(CompactTelemetrySchemaId.RaceEventV1);
            var values = new double?[schema.Fields.Count];
            values[schema.Fields.First(value => value.Name == "eventTypeRef").Ordinal] = 0;
            var strings = new[]
            {
                new CompactStringDictionaryEntry(CompactStringDictionaryId.EventType, 0, "SESSION_START")
            };
            byte[] payload = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(
                11,
                22,
                33,
                new CompactTelemetryBlock(
                    CompactTelemetrySchemaId.RaceEventV1,
                    0,
                    0,
                    new[] { new CompactTelemetrySample(0, values) }),
                null,
                strings));
            byte[] compressed = TelemetryChunkSerializer.Gzip(payload);
            string chunkPath = Path.Combine(directory, "00000033-0010.a2ct.gz");
            File.WriteAllBytes(chunkPath, compressed);
            DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
            var metadata = new TelemetryPendingUploadMetadata
            {
                Schema = "ams2-compact-upload-metadata-v1",
                Endpoint = Cafe24ActivityUploadTransport.TelemetryChunksEndpoint,
                Protocol = "AMS2_COMPACT_TELEMETRY_V1",
                CompactSchemaId = (ushort)CompactTelemetrySchemaId.RaceEventV1,
                SessionLocalId = 11,
                AttemptLocalId = 22,
                ChunkId = "a2ct-client-fixture-00000033",
                StreamType = TelemetryStreamType.RACE_STORY,
                Visibility = TelemetryVisibility.PUBLIC_REPLAY,
                SessionId = "capture-client-fixture",
                SessionFingerprint = "session-fingerprint-client-fixture",
                WitnessId = "witness-client-fixture",
                AttemptId = "attempt-client-fixture",
                AttemptNumber = 1,
                ChunkIndex = 33,
                StartElapsedMs = 0,
                EndElapsedMs = 0,
                FirstCapturedAtUtc = capturedAt,
                LastCapturedAtUtc = capturedAt,
                RelativeChunkPath = Path.GetRelativePath(telemetryRoot, chunkPath),
                ContentType = Cafe24ActivityUploadTransport.CompactTelemetryContentType,
                ContentEncoding = "gzip",
                PayloadSha256 = TelemetryChunkSerializer.Sha256(payload),
                CompressedSha256 = TelemetryChunkSerializer.Sha256(compressed),
                UncompressedBytes = payload.Length,
                CompressedBytes = compressed.Length,
                Status = TelemetryUploadStatus.PENDING,
                CreatedAtUtc = capturedAt,
                UpdatedAtUtc = capturedAt
            };
            File.WriteAllBytes(
                Path.Combine(directory, "00000033-0010.upload.json"),
                TelemetryChunkSerializer.SerializeMetadata(metadata));
        }

        private static string CreatePendingTelemetryChunk(string telemetryRoot)
        {
            TelemetryArchiveIdentity identity = TelemetryArchiveIdentityFactory.StartSession(
                "client-test-telemetry-session-fingerprint",
                "client-test-telemetry-witness");
            var archive = new LocalDurableTelemetryArchive(telemetryRoot, identity);
            try
            {
                AssertTrue(archive.TryCaptureSessionMetadata(new SessionMetadataSample
                {
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    SessionElapsedMs = 0,
                    GameBuild = 3398,
                    SharedMemoryVersion = 14,
                    ClientVersion = "0.2.2",
                    ParserVersion = "AMS2_SHM_V14",
                    Track = "Monza",
                    Layout = "Monza_2020",
                    TrackLengthMeters = 5793,
                    SessionType = "RACE",
                    ClockSource = "MONOTONIC_CAPTURE_CLOCK",
                    ObservedParticipants = 2,
                    CaptureStarted = true,
                    CaptureCompleteness = "COMPLETE"
                }));
                archive.FlushAsync().GetAwaiter().GetResult();
            }
            finally
            {
                archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            return Directory.EnumerateFiles(telemetryRoot, "*.upload.json", SearchOption.AllDirectories).Single();
        }

        private static TelemetryUploadStatus? ReadTelemetryStatus(string metadataPath)
        {
            try
            {
                return TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(metadataPath)).Status;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static string EnrollOnly(string installationId, string token)
        {
            string captured = string.Empty;
            WithTemporaryDirectory(directory =>
            {
                ActivityConnectionOptions options = ActivityConnectionOptions.Load(
                    Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                var handler = new EnrollmentFixtureHandler(installationId, token);
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, installationId, "0.2.1", http);
                Cafe24AnonymousEnrollmentResponse response = transport.EnsureAnonymousEnrollmentAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                AssertEqual(installationId, response.InstallationId);
                AssertTrue(response.Scopes.Contains("witnesses:write", StringComparer.Ordinal));
                AssertEqual(1, handler.EnrollmentCalls);
                captured = PairingTokenStore.Load(directory);
            });
            return captured;
        }

        private static void WithTemporaryDirectory(Action<string> action)
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-release020-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                action(path);
            }
            finally
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
        }

        private sealed class TemporaryClientDirectory : IDisposable
        {
            public TemporaryClientDirectory()
            {
                Root = Path.Combine(Path.GetTempPath(), "ams2-release021-queue-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
            }

            public string Root { get; }

            public void Dispose()
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
        }

        private sealed class EnrollmentFixtureHandler : HttpMessageHandler
        {
            private readonly string _installationId;
            private readonly string _token;

            public EnrollmentFixtureHandler(string installationId, string token)
            {
                _installationId = installationId;
                _token = token;
            }

            public int EnrollmentCalls { get; private set; }
            public int UploadCalls { get; private set; }
            public int TelemetryCalls { get; private set; }
            public string UploadAuthorization { get; private set; } = string.Empty;
            public string UploadCompatibilityAuthorization { get; private set; } = string.Empty;
            public string TelemetryAuthorization { get; private set; } = string.Empty;
            public string TelemetryCompatibilityAuthorization { get; private set; } = string.Empty;
            public string TelemetryContentEncoding { get; private set; } = string.Empty;
            public string TelemetryContentType { get; private set; } = string.Empty;
            public string TelemetryIdempotencyKey { get; private set; } = string.Empty;
            public string TelemetryPayloadSha256 { get; private set; } = string.Empty;
            public string TelemetryCompressedSha256 { get; private set; } = string.Empty;
            public string TelemetryChunkId { get; private set; } = string.Empty;
            public string TelemetrySessionId { get; private set; } = string.Empty;
            public string TelemetryAttemptId { get; private set; } = string.Empty;
            public string TelemetryVisibility { get; private set; } = string.Empty;
            public string TelemetryClientVersion { get; private set; } = string.Empty;
            public byte[] TelemetryBody { get; private set; } = Array.Empty<byte>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string query = Uri.UnescapeDataString(request.RequestUri?.Query ?? string.Empty);
                if (query.Contains(Cafe24ActivityUploadTransport.EnrollmentEndpoint, StringComparison.Ordinal))
                {
                    EnrollmentCalls++;
                    string json = "{\"installationToken\":\"" + _token
                        + "\",\"installationId\":\"" + _installationId
                        + "\",\"scopes\":[\"presence:write\",\"activities:write\",\"witnesses:write\",\"telemetry:write\"],\"duplicate\":false}";
                    return Task.FromResult(Response(HttpStatusCode.Created, json));
                }

                if (query.Contains(Cafe24ActivityUploadTransport.TelemetryChunksEndpoint, StringComparison.Ordinal))
                {
                    TelemetryCalls++;
                    TelemetryAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
                    TelemetryCompatibilityAuthorization = request.Headers.TryGetValues("X-AMS2-Authorization", out var authValues)
                        ? authValues.SingleOrDefault() ?? string.Empty
                        : string.Empty;
                    TelemetryContentEncoding = request.Content?.Headers.ContentEncoding.SingleOrDefault() ?? string.Empty;
                    TelemetryContentType = request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
                    TelemetryIdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var idempotencyValues)
                        ? idempotencyValues.SingleOrDefault() ?? string.Empty
                        : string.Empty;
                    TelemetryPayloadSha256 = request.Headers.TryGetValues("X-AMS2-Payload-SHA256", out var payloadValues)
                        ? payloadValues.SingleOrDefault() ?? string.Empty
                        : string.Empty;
                    TelemetryCompressedSha256 = request.Headers.TryGetValues("X-AMS2-Compressed-SHA256", out var compressedValues)
                        ? compressedValues.SingleOrDefault() ?? string.Empty
                        : string.Empty;
                    TelemetryChunkId = Header(request, "X-AMS2-Chunk-Id");
                    TelemetrySessionId = Header(request, "X-AMS2-Session-Id");
                    TelemetryAttemptId = Header(request, "X-AMS2-Attempt-Id");
                    TelemetryVisibility = Header(request, "X-AMS2-Visibility");
                    TelemetryClientVersion = Header(request, "X-AMS2-Client-Version");
                    TelemetryBody = request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult()
                        ?? Array.Empty<byte>();
                    string chunkId = TelemetryIdempotencyKey.StartsWith("telemetry:", StringComparison.Ordinal)
                        ? TelemetryIdempotencyKey.Substring("telemetry:".Length)
                        : string.Empty;
                    string json = "{\"status\":\"stored\",\"duplicate\":false,\"chunkId\":\""
                        + chunkId + "\",\"contentSha256\":\"" + TelemetryPayloadSha256 + "\"}";
                    return Task.FromResult(Response(HttpStatusCode.Created, json));
                }

                if (query.Contains(Cafe24ActivityUploadTransport.SessionWitnessEndpoint, StringComparison.Ordinal))
                {
                    UploadCalls++;
                    UploadAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
                    UploadCompatibilityAuthorization = request.Headers.TryGetValues("X-AMS2-Authorization", out var values)
                        ? values.SingleOrDefault() ?? string.Empty
                        : string.Empty;
                    return Task.FromResult(Response(HttpStatusCode.Created, "{\"status\":\"stored\",\"duplicate\":false}"));
                }

                return Task.FromResult(Response(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}"));
            }

            private static HttpResponseMessage Response(HttpStatusCode status, string json)
                => new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

            private static string Header(HttpRequestMessage request, string name)
                => request.Headers.TryGetValues(name, out IEnumerable<string>? values)
                    ? values.SingleOrDefault() ?? string.Empty
                    : string.Empty;
        }

        private sealed class DualUploadFixtureTransport : IActivityUploadTransport, ITelemetryChunkUploadTransport
        {
            public int TelemetryCalls { get; private set; }

            public Task<ActivityUploadTransportResult> SendAsync(
                ActivityUploadItem item,
                CancellationToken cancellationToken)
                => Task.FromResult(ActivityUploadTransportResult.Http(201, false, "STORED"));

            public Task<TelemetryChunkUploadTransportResult> SendTelemetryChunkAsync(
                TelemetryChunkUploadItem item,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TelemetryCalls++;
                return Task.FromResult(TelemetryChunkUploadTransportResult.Stored(201, false));
            }
        }

        private static TelemetrySnapshot Parse(RawFixtureBuilder fixture)
            => Parse(fixture, DateTimeOffset.UtcNow);

        private static TelemetrySnapshot Parse(RawFixtureBuilder fixture, DateTimeOffset capturedAt)
        {
            TelemetryReadResult result = new SharedMemoryParser().Parse(fixture.Buffer, capturedAt);
            if (result.Status != TelemetryReadStatus.Success || result.Snapshot == null)
            {
                throw new InvalidOperationException("Fixture did not parse: " + result.Status + " " + result.Message);
            }
            return result.Snapshot;
        }

        private static DateTimeOffset FixedTime()
            => new DateTimeOffset(2026, 9, 1, 4, 0, 0, TimeSpan.Zero);

        private static RaceControlUpdate ObserveControl(
            RaceControlAnalyzer analyzer,
            RawFixtureBuilder fixture,
            DateTimeOffset time,
            int generation = 0)
        {
            TelemetrySnapshot snapshot = Parse(fixture, time);
            return analyzer.Observe(snapshot, Classify(snapshot), generation, time);
        }

        private static ParticipantSnapshot ResolveLocal(TelemetrySnapshot snapshot)
        {
            LocalParticipantResolution result = new LocalParticipantResolver().Resolve(snapshot);
            if (!result.IsValid || result.Participant == null)
            {
                throw new InvalidOperationException(result.Reason);
            }
            return result.Participant;
        }

        private static LeagueClassification Classify(TelemetrySnapshot snapshot)
            => new LeagueClassificationResolver().Resolve(snapshot, ResolveLocal(snapshot));

        private static void AssertTrue(bool value)
        {
            if (!value) throw new InvalidOperationException("Expected true.");
        }

        private static void AssertFalse(bool value)
        {
            if (value) throw new InvalidOperationException("Expected false.");
        }

        private static void AssertNull(object? value)
        {
            if (value != null) throw new InvalidOperationException("Expected null, got " + value + ".");
        }

        private static void AssertNotNull(object? value)
        {
            if (value == null) throw new InvalidOperationException("Expected non-null.");
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
            }
        }
    }
}
