using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AMS2LeagueClient.Core.ActivityCapture.Upload;
using AMS2LeagueClient.Core.Events;
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
    internal static partial class Program
    {
        private static string? _layoutCaptureDirectory;

        [STAThread]
        private static int Main(string[] args)
        {
            var application = new AMS2LeagueClient.App(startRuntime: false);
            application.InitializeComponent();
            int captureArgument = Array.IndexOf(args, "--capture-layout");
            if (captureArgument >= 0 && captureArgument + 1 < args.Length)
                _layoutCaptureDirectory = Path.GetFullPath(args[captureArgument + 1]);
            if (args.Contains("--motion-probe", StringComparer.Ordinal))
            {
                OverlayMotionProbe.Run();
                application.Shutdown();
                return 0;
            }
            if (args.Contains("--resource-probe", StringComparer.Ordinal))
            {
                HudResourceProbe();
                application.Shutdown();
                return 0;
            }
            if (args.Contains("--hud-preview", StringComparer.Ordinal))
            {
                DrivingAbsSteeringAndRpm();
                FastDrivingReadDoesNotFeedRecording();
                KoreanLabelsAndPenaltyColumn();
                application.Shutdown();
                return 0;
            }
            int liveUpdateArgument = Array.IndexOf(args, "--verify-live-update");
            if (liveUpdateArgument >= 0 && liveUpdateArgument + 1 < args.Length)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                var client = new GitHubReleaseClient(http);
                ReleaseUpdate release = client.FindUpdateAsync("0.0.0", CancellationToken.None).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("No public release found.");
                string path = client.DownloadAsync(release, Path.GetFullPath(args[liveUpdateArgument + 1]), null, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("LIVE_UPDATE_VERIFIED version=" + release.Version + " bytes=" + new FileInfo(path).Length + " sha256=" + release.Sha256);
                application.Shutdown();
                return 0;
            }
            int logsArgument = Array.IndexOf(args, "--verify-session-logs");
            if (logsArgument >= 0 && logsArgument + 1 < args.Length)
            {
                AutomaticModeTests.VerifyRealLogs(args[logsArgument + 1]);
                application.Shutdown();
                return 0;
            }
            var tests = new (string Name, Action Test)[]
            {
                ("Main overlay gallery previews and direct selection persist", MainOverlayGallery),
                ("Telemetry layouts separate gauges and keep wheel angle compact", TelemetryPanelLayouts),
                ("Driving graph scrolls existing points left between samples", DrivingGraphScrollsLeft),
                ("Driving telemetry validates sources and bounds its history", DrivingTelemetrySourcesAndHistory),
                ("Separate pedal panels migrate and retain independent layout", SeparatePedalLayoutMigration),
                ("Driving ABS steering and RPM use observed values", DrivingAbsSteeringAndRpm)
                ,("Fast driving reads are isolated from recording", FastDrivingReadDoesNotFeedRecording),
                ("Ahead and behind battles use the matching split and queue", BattlesAheadAndBehind),
                ("Driving HUD renders independently and persists appearance", DrivingHudRenderingAndSettings),
                ("Update helper confirms restart and reports early exit", UpdateHelperConfirmsRestart),
                ("Automatic online log session boundaries", AutomaticModeTests.LogBoundaries),
                ("Automatic mode rejects missing stale and replaced logs", AutomaticModeTests.LogFilesAndHistory),
                ("Single and unknown queues cannot starve multiplayer uploads", AutomaticModeTests.UploadFiltering),
                ("Result and replay race mode fields agree", AutomaticModeTests.ModeFields),
                ("Korean labels and dedicated penalty column", KoreanLabelsAndPenaltyColumn),
                ("Tower shrinks and restores with participant count", TowerShrinksAndRestoresWithParticipants),
                ("Empty panels remain editable with preview", EmptyPanelsRemainEditableWithPreview),
                ("Offline layout preview lifecycle", OfflineLayoutPreviewLifecycle),
                ("Offline layout controls", OfflineLayoutControls),
                ("VR settings defaults bounds colours and transforms", VrSettingsAndTransforms),
                ("VR persistent D3D11 texture GPU readback and lifetime", VrPersistentTexturePixels),
                ("VR connection retry backpressure quit and monitor off", VrConnectionLifecycle),
                ("VR output rendering focus isolation and saved settings", VrOutputRenderingAndPersistence),
                ("VR settings controls and packaged SDK", VrSettingsControlsAndSdk),
                ("Saved tower expands once and retains independent layout", LegacyTowerWidthMigration),
                ("Release versions and metadata reject unsafe updates", ReleaseVersionAndMetadata),
                ("Update downloads verify exact bytes and recover from failures", UpdateDownloadValidation),
                ("Updater startup and Windows quoting preserve arguments", UpdateStartupAndArguments),
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
                ("Start finish wrap never creates lap gap", StartFinishWrapNeverCreatesLapGap),
                ("Actual lap gaps require stable cumulative progress", ActualLapGapsRequireStableCumulativeProgress),
                ("Participant refresh resets lap confirmation", ParticipantRefreshResetsLapConfirmation),
                ("Relative time gap uses matching game source and survives lap gap", RelativeTimeGapUsesMatchingGameSource),
                ("Prestart relative uses world positions until lap progress is ready", PrestartRelativeUsesWorldPositions),
                ("Prestart relative rejects missing and ambiguous positions", PrestartRelativeRejectsUnknownPositions),
                ("Relative time and distance remain visible at resized bounds", RelativeTimeAndDistanceRemainVisible),
                ("RaceControl clears AMS2 top-center alert", RaceControlLeftAuxiliaryPlacement),
                ("Compact UI metrics meet target", CompactUiMetricsMeetTarget),
                ("Timing tower row capacity follows resized aspect ratio", TimingTowerRowCapacityFollowsResize),
                ("Timing tower last row stays inside bounds", TimingTowerLastRowStaysInsideBounds),
                ("Expanded timing tower preserves leader and player selection", ExpandedTimingTowerPreservesSelection),
                ("Timing refresh updates rows without collection churn", TimingRefreshUpdatesRowsInPlace),
                ("Compact anchors hold at target resolutions", CompactAnchorsHoldAtTargetResolutions),
                ("Independent layout profile scales and clamps", IndependentLayoutProfileScalesAndClamps),
                ("Timing rows expose class and best lap", TimingRowsExposeClassAndCurrentTime),
                ("Class badge palette is explicit and stable", ClassBadgePaletteIsExplicitAndStable),
                ("Class and timing typography fits tower", ClassAndTimingTypographyFitsTower),
                ("Only inactive participant states are dimmed", OnlyInactiveParticipantStatesAreDimmed),
                ("Status changes never dim active rows", StatusChangesNeverDimActiveRows),
                ("Practice active tower uses best lap, personal panel stays live", PracticeActiveUsesCurrentTiming),
                ("Practice completed uses best lap", PracticeCompletedUsesBestLap),
                ("Qualifying active tower uses best lap, personal panel stays live", QualifyingActiveUsesCurrentTiming),
                ("Qualifying completed uses best lap", QualifyingCompletedUsesBestLap),
                ("Race timing stops per participant", RaceTimingStopsPerParticipant),
                ("Terminal states never keep timing", TerminalStatesNeverKeepTiming),
                ("Position reorder animation survives timing refresh", PositionAnimationSurvivesTimingRefresh),
                ("Waiting overlay content fits design bounds", WaitingOverlayContentFitsDesignBounds),
                ("Waiting overlay fits legacy saved bounds", WaitingOverlayFitsLegacySavedBounds),
                ("Timing tower removes redundant headers", TimingTowerRemovesRedundantHeaders),
                ("Overlay edit mode restores click-through", OverlayEditModeRestoresClickThrough),
                ("Multiplayer menu shows waiting overlay", MultiplayerMenuShowsWaitingOverlay),
                ("Missing qualifying timer cannot override gameplay state", QualifyingWithoutTimerRemainsGameplay),
                ("Waiting overlay includes single but excludes replay", WaitingOverlayIncludesSingleExcludesReplay),
                ("Automatic mode display and session labels follow observed sources", WaitingModeAndSessionLabels),
                ("Waiting overlay returns to gameplay", WaitingOverlayReturnsToGameplay),
                ("All driving modes remain visible without timer", AllDrivingModesRemainVisibleWithoutTimer),
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
                ,("JSON gzip capabilities are optional and route specific", JsonGzipCapabilities)
                ,("JSON gzip preserves durable payloads identities and response handling", JsonGzipUploadContract)
                ,("JSON gzip negotiation is shared transient and server bound", JsonGzipNegotiationLifecycle)
                ,("Telemetry gzip HTTP contract is exact", TelemetryGzipHttpContractIsExact)
                ,("Compact telemetry gzip HTTP contract is exact", CompactTelemetryGzipHttpContractIsExact)
                ,("Long-track upload retries unknown server schema and preserves other failures", LongTrackUploadWaitsForServer)
                ,("403 JSON HTML diagnostics quarantine without credentials or replay", ForbiddenUploadDiagnostics)
                ,("Race batch uploads after whole-field finish and preserves late joins", RaceBatchCompletionAndLateJoin)
                ,("Transition tracker reports position direction and fastest lap", TransitionTrackerReportsPositionDirection)
                ,("Position change flashes row and rolls number", PositionChangeFlashesRowAndRollsNumber)
                ,("Fastest lap status sweeps purple without dimming", FastestLapStatusSweepsPurple)
                ,("Tower rows build in when shown", TowerRowsBuildInWhenShown)
                ,("Component toggle persists without layout edit", ComponentToggleWithoutEditPersists)
                ,("Status window toggles are always enabled", StatusWindowTogglesAlwaysEnabled)
                ,("Relative participant change animates", RelativeParticipantChangeAnimates)
                ,("Session lap counter rolls", SessionLapCounterRolls)
                ,("Event card exit keeps surface for animation", EventCardExitKeepsSurfaceForAnimation)
                ,("Fastest lap and leader change flash once per event", EventHighlightsFlashOnce)
                ,("Lap timing best lap pops", LapTimingBestLapPops)
                ,("Resize preview immediately matches saved tower", ResizePreviewMatchesSavedTower)
                ,("Auxiliary panels fill independently resized bounds", AuxiliaryPanelsFillResizedBounds)
                ,("Ongoing flags do not replay entrance", OngoingFlagsDoNotReplayEntrance)
                ,("Timing tick only notifies current time binding", TimingTickOnlyNotifiesTime)
                ,("Broadcast motion requests high refresh", BroadcastMotionRequestsHighRefresh)
                ,("Race control reflows without clipping or glyph distortion", RaceControlReflowsWithoutClipping)
                ,("Participant lap clocks start independently at observed lines", ParticipantLapClocksStartIndependently)
                ,("Participant lap clocks reject stale identity and terminal states", ParticipantLapClocksRejectInvalidContinuity)
                ,("Tower best lap ignores sector loss and observed clocks", TowerTimingFallsBackForMissingSectors)
                ,("Tower best lap uses only the participant best source", OpponentTimingUsesCurrentLapSectors)
                ,("Opening lap shows out-lap label per driver only in the tower", OpeningLapLabelInTower)
                ,("Race control shows current green flag without history", RaceControlShowsCurrentGreenWithoutHistory)
                ,("Invalid current lap freezes personal panel but preserves tower bests", InvalidLapDisplayFreezesPerParticipant)
                ,("Invalid lap event interrupts ordinary events once per lap", InvalidLapEventIsVisibleOncePerLap)
                ,("Opening invalid flags stay out of UI without changing raw data", OpeningInvalidFlagsStayHidden)
                ,("Relative lap gaps appear only in Race", RelativeLapGapsOnlyInRace)
                ,("Pit and track relatives use separate physical populations", PitAndTrackRelativesStaySeparate)
                ,("Pit relative rejects unknown geometry without track fallback", PitRelativeRejectsUnknownGeometry)
                ,("Pit-to-track distance source changes do not fake trends", PitRelativeTransitionResetsTrend)
                ,("Practice qualifying pit exit starts a new out lap, then timed running", PracticeLapPhaseFollowsPitExit)
                ,("Lap phase resets on session identity and counter rollback", LapPhaseRejectsStaleState)
                ,("PIT includes all five pit modes and preserves penalty precedence", AllPitModesShowPitBadge)
                ,("Native lap start clears out lap before geometric and completed counters", NativeLapStartClearsOutLapImmediately)
                ,("Pit-exit state cannot re-arm a started timed lap", PitExitDoesNotRearmTimedLap)
                ,("Real first timed lap starts without any lap counter change", FirstTimedLapUsesNativeTimingAvailability)
                ,("Out lap survives real pause and menu generation changes", OutLapSurvivesPauseAndMenu)
                ,("Late attach distinguishes untimed out lap from first timed lap", LateAttachRecognizesUntimedOutLap)
            };
            int filterArgument = Array.IndexOf(args, "--filter");
            if (filterArgument >= 0 && filterArgument + 1 < args.Length)
            {
                tests = tests.Where(test => test.Name.Contains(args[filterArgument + 1], StringComparison.OrdinalIgnoreCase)).ToArray();
                if (tests.Length == 0) throw new ArgumentException("No tests matched --filter.");
            }
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
            AssertTrue(expired.History[0].Message.Contains("드라이브스루", StringComparison.Ordinal));
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
                AheadDistanceMeters = 50,
                BehindParticipantIndex = 5,
                BehindDistanceMeters = 50
            };
            tracker.Apply(view, 1);
            AssertEqual(string.Empty, view.AheadDistanceTrendArrow);
            AssertEqual(string.Empty, view.BehindDistanceTrendArrow);

            view.AheadDistanceMeters = 60;
            view.BehindDistanceMeters = 40;
            tracker.Apply(view, 1);
            AssertEqual("▲", view.AheadDistanceTrendArrow);
            AssertEqual("#FF7777", view.AheadDistanceColor);
            AssertEqual("▼", view.BehindDistanceTrendArrow);
            AssertEqual("#FF7777", view.BehindDistanceColor);

            view.AheadDistanceMeters = 50;
            view.BehindDistanceMeters = 50;
            tracker.Apply(view, 1);
            AssertEqual("▼", view.AheadDistanceTrendArrow);
            AssertEqual("#57D5FF", view.AheadDistanceColor);
            AssertEqual("▲", view.BehindDistanceTrendArrow);
            AssertEqual("#57D5FF", view.BehindDistanceColor);

            view.AheadDistanceMeters = 51;
            view.BehindDistanceMeters = 49;
            tracker.Apply(view, 1);
            AssertEqual("▼", view.AheadDistanceTrendArrow);
            AssertEqual("▲", view.BehindDistanceTrendArrow);

            view.AheadParticipantIndex = 6;
            tracker.Apply(view, 1);
            AssertEqual(string.Empty, view.AheadDistanceTrendArrow);
            tracker.Apply(view, 2);
            AssertEqual(string.Empty, view.BehindDistanceTrendArrow);
        }

        private static void StartFinishWrapNeverCreatesLapGap()
        {
            const float trackLength = 1000;
            ParticipantSnapshot local = ProgressParticipant(0, "LOCAL", 2, 990);
            ParticipantSnapshot aheadAcrossLine = ProgressParticipant(1, "AHEAD", 3, 10);
            TrackProgressDistance progress = new TrackProgressDistanceResolver().Resolve(trackLength, local, aheadAcrossLine);
            AssertEqual(0, progress.LapGap);
            AssertEqual("20m", progress.Text);

            var tracker = new RelativeDistanceTrendTracker();
            for (int index = 0; index < 3; index++)
            {
                var view = new OverlayViewModel
                {
                    AheadParticipantIndex = 1,
                    AheadParticipantKey = "1|AHEAD|CAR|GT3",
                    AheadDistanceMeters = 20,
                    AheadLapGapCandidate = progress.LapGap,
                    AheadGap = "+0.250"
                };
                tracker.Apply(view, 7);
                AssertEqual("+0.250", view.AheadGap);
                AssertEqual(string.Empty, view.AheadLapGap);
            }
        }

        private static void ActualLapGapsRequireStableCumulativeProgress()
        {
            const float trackLength = 1000;
            ParticipantSnapshot local = ProgressParticipant(0, "LOCAL", 1, 100);
            TrackProgressDistance oneLap = new TrackProgressDistanceResolver().Resolve(
                trackLength,
                local,
                ProgressParticipant(1, "AHEAD", 2, 100));
            TrackProgressDistance twoLaps = new TrackProgressDistanceResolver().Resolve(
                trackLength,
                local,
                ProgressParticipant(1, "AHEAD", 3, 100));
            AssertEqual(1, oneLap.LapGap);
            AssertEqual("랩 1", oneLap.Text);
            AssertEqual(2, twoLaps.LapGap);
            AssertEqual("랩 2", twoLaps.Text);

            var tracker = new RelativeDistanceTrendTracker();
            var first = LapCandidateView("1|AHEAD|CAR|GT3", 1);
            tracker.Apply(first, 8);
            AssertEqual("+0.500", first.AheadGap);
            AssertEqual(string.Empty, first.AheadLapGap);
            var confirmed = LapCandidateView("1|AHEAD|CAR|GT3", 1);
            tracker.Apply(confirmed, 8);
            AssertEqual("랩 1", confirmed.AheadLapGap);
            AssertEqual("+0.500", confirmed.AheadGap);

            var firstTwo = LapCandidateView("1|AHEAD|CAR|GT3", 2);
            tracker.Apply(firstTwo, 8);
            AssertEqual("랩 1", firstTwo.AheadLapGap);
            var confirmedTwo = LapCandidateView("1|AHEAD|CAR|GT3", 2);
            tracker.Apply(confirmedTwo, 8);
            AssertEqual("랩 2", confirmedTwo.AheadLapGap);
            AssertEqual("+0.500", confirmedTwo.AheadGap);
        }

        private static void ParticipantRefreshResetsLapConfirmation()
        {
            var tracker = new RelativeDistanceTrendTracker();
            tracker.Apply(LapCandidateView("1|OLD|CAR|GT3", 1), 9);
            var oldConfirmed = LapCandidateView("1|OLD|CAR|GT3", 1);
            tracker.Apply(oldConfirmed, 9);
            AssertEqual("랩 1", oldConfirmed.AheadLapGap);

            var refreshed = LapCandidateView("1|NEW|CAR|GT3", 1);
            tracker.Apply(refreshed, 9);
            AssertEqual("+0.500", refreshed.AheadGap);
            AssertEqual(string.Empty, refreshed.AheadLapGap);

            var sessionTracker = new RelativeDistanceTrendTracker();
            sessionTracker.Apply(LapCandidateView("1|SAME|CAR|GT3", 1), 9);
            sessionTracker.Apply(LapCandidateView("1|SAME|CAR|GT3", 1), 9);
            var nextSession = LapCandidateView("1|SAME|CAR|GT3", 1);
            sessionTracker.Apply(nextSession, 10);
            AssertEqual("+0.500", nextSession.AheadGap);
            AssertEqual(string.Empty, nextSession.AheadLapGap);
        }

        private static void RelativeTimeGapUsesMatchingGameSource()
        {
            var fixture = new RawFixtureBuilder(4).SetViewedIndex(1).SetTrackTelemetry(1000, 300)
                .SetParticipantLapDistance(0, 550).SetParticipantLapDistance(1, 500)
                .SetParticipantLapDistance(2, 460).SetParticipantLapDistance(3, 100)
                .SetSplitAhead(0.842f).SetSplitBehind(1.127f);
            OverlayViewModel view = BuildTiming(fixture);
            AssertEqual("+0.842s", view.AheadGap);
            AssertEqual("+1.127s", view.BehindGap);
            AssertEqual("50m", view.AheadDistance);
            AssertEqual("40m", view.BehindDistance);
            AssertEqual("GAME_SPLIT", view.AheadSource);

            view.AheadLapGapCandidate = 1;
            view.BehindLapGapCandidate = 2;
            var tracker = new RelativeDistanceTrendTracker();
            tracker.Apply(view, 1);
            tracker.Apply(view, 1);
            AssertEqual("랩 1", view.AheadLapGap);
            AssertEqual("랩 2", view.BehindLapGap);
            AssertEqual("+0.842s", view.AheadGap);
            AssertEqual("+1.127s", view.BehindGap);

            foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                view = BuildTiming(fixture.SetSplitAhead(invalid).SetSplitBehind(invalid));
                AssertEqual("—", view.AheadGap);
                AssertEqual("—", view.BehindGap);
                AssertEqual("50m", view.AheadDistance);
            }
            view = BuildTiming(fixture.SetSplitAhead(0).SetSplitBehind(0));
            AssertEqual("+0.000s", view.AheadGap);
            AssertEqual("+0.000s", view.BehindGap);
            // A physically nearer car must never inherit another car's ranking split.
            view = BuildTiming(fixture.SetSplitAhead(0.842f).SetSplitBehind(1.127f)
                .SetParticipantLapDistance(3, 510));
            AssertEqual(3, view.AheadParticipantIndex);
            AssertEqual("10m", view.AheadDistance);
            AssertEqual("—", view.AheadGap);
            AssertEqual("UNKNOWN", view.AheadSource);
            AssertEqual("+1.127s", view.BehindGap);
        }

        private static RawFixtureBuilder PitRelativeFixture(PitMode localMode = PitMode.InPit, PitMode frontMode = PitMode.InGarage, uint completed = 0)
        {
            var fixture = new RawFixtureBuilder(5).SetViewedIndex(1).SetTrackTelemetry(4000, 300)
                .SetParticipant(0, true, "PIT FRONT", 1, completed, completed + 1, RaceState.Racing, frontMode)
                .SetParticipant(1, true, "ME", 2, completed, completed + 1, RaceState.Racing, localMode)
                .SetParticipant(2, true, "PIT REAR", 3, completed, completed + 1, RaceState.Racing, PitMode.InPit)
                .SetParticipant(3, true, "TRACK FRONT", 4, completed, completed + 1, RaceState.Racing, PitMode.None)
                .SetParticipant(4, true, "TRACK REAR", 5, completed, completed + 1, RaceState.Racing, PitMode.None)
                .SetParticipantLapDistance(0, 0).SetParticipantLapDistance(1, 3800).SetParticipantLapDistance(2, 0)
                .SetParticipantLapDistance(3, 3942).SetParticipantLapDistance(4, 3766)
                .SetParticipantMotion(0, 100, 10, 90, 0, (float)(Math.PI / 2), 0, 0) // angled garage car remains a candidate
                .SetParticipantMotion(1, 100, 10, 100, 0, 0, 0, 0)
                .SetParticipantMotion(2, 100, 10, 120, 0, 0, 0, 0)
                .SetParticipantMotion(3, 100, 10, 98, 0, 0, 0, 10) // nearer in 3D, but on the other route
                .SetParticipantMotion(4, 100, 10, 102, 0, 0, 0, 10);
            return fixture;
        }

        private static void PitAndTrackRelativesStaySeparate()
        {
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Race })
            foreach (PitMode ownPit in new[] { PitMode.DrivingIntoPits, PitMode.InPit, PitMode.DrivingOutOfPits, PitMode.InGarage, PitMode.DrivingOutOfGarage })
            foreach (PitMode otherPit in new[] { PitMode.DrivingIntoPits, PitMode.InPit, PitMode.DrivingOutOfPits, PitMode.InGarage, PitMode.DrivingOutOfGarage })
            foreach (uint completed in new uint[] { 0, 3 })
            {
                var fixture = PitRelativeFixture(ownPit, otherPit, completed).SetSession(session);
                byte[] original = fixture.Buffer.ToArray();
                OverlayViewModel view = BuildTiming(fixture);
                AssertEqual(0, view.AheadParticipantIndex);
                AssertEqual(2, view.BehindParticipantIndex);
                AssertEqual("~10m", view.AheadDistance);
                AssertEqual("~20m", view.BehindDistance);
                AssertFalse(view.AheadLapGapCandidate.HasValue);
                AssertFalse(view.BehindLapGapCandidate.HasValue);
                AssertTrue(fixture.Buffer.SequenceEqual(original));
                view = BuildTiming(PitRelativeFixture(PitMode.None, otherPit, completed).SetSession(session));
                AssertEqual(3, view.AheadParticipantIndex);
                AssertEqual(4, view.BehindParticipantIndex);
                AssertEqual("142m", view.AheadDistance);
                AssertEqual("34m", view.BehindDistance);
            }
            var pit = PitRelativeFixture();
            var unranked = BuildTiming(pit.SetParticipant(0, true, "UNRANKED PIT", 0, 0, 1, RaceState.NotStarted, PitMode.InGarage));
            AssertEqual(0, unranked.AheadParticipantIndex);
            AssertEqual("P—", unranked.AheadPosition);
            AssertEqual("UNRANKED PIT", unranked.AheadName);
            AssertEqual("~10m", unranked.AheadDistance);
            pit = PitRelativeFixture();
            var engine = new RaceEventEngine();
            foreach (int seconds in new[] { 0, 10, 20 })
            {
                TelemetrySnapshot snapshot = Parse(pit, FixedTime().AddSeconds(seconds));
                AssertFalse(engine.Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt).DetectedEvents.Any(item => item.Type == OverlayEventType.Battle || item.Type == OverlayEventType.BattleBehind));
            }
            var panel = new RelativeDriversView();
            panel.SetViewModel(BuildTiming(pit));
            panel.Measure(new Size(panel.Width, panel.Height));
            panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
            CaptureLayout(panel, "pit-relative-only");
        }

        private static void PitRelativeRejectsUnknownGeometry()
        {
            foreach ((float x, float y, float z) in new[] { (0f, 0f, 0f), (float.NaN, 10f, 90f), (100f, 10f, float.PositiveInfinity), (100f, 10f, 100f), (100f, 20f, 90f), (140f, 10f, 90f), (100f, 10f, -100f) })
            {
                var view = BuildTiming(PitRelativeFixture().SetParticipantMotion(0, x, y, z, 0, 0, 0, 0));
                AssertEqual(-1, view.AheadParticipantIndex);
                AssertEqual("—", view.AheadDistance); // never substitute TRACK FRONT
                AssertEqual(2, view.BehindParticipantIndex);
            }
            AssertEqual("~10m", BuildTiming(PitRelativeFixture().SetTrackTelemetry(0, 300)).AheadDistance);
            AssertEqual(-1, BuildTiming(PitRelativeFixture(frontMode: (PitMode)999)).AheadParticipantIndex);
            AssertFalse(BuildTiming(PitRelativeFixture(localMode: (PitMode)999)).IsBottomGapPanelVisible);
            AssertEqual(-1, BuildTiming(PitRelativeFixture().SetParticipantVehicle(0, "SafetyCar", "SafetyCar")).AheadParticipantIndex);
            // Requesting a pit stop has not entered the pit area yet.
            AssertEqual(3, BuildTiming(PitRelativeFixture(PitMode.None).SetParticipantControl(1, PitSchedule.PlayerRequested)).AheadParticipantIndex);
        }

        private static void PitRelativeTransitionResetsTrend()
        {
            var tracker = new RelativeDistanceTrendTracker();
            OverlayViewModel Sample(bool pit, float frontZ)
            {
                var fixture = PitRelativeFixture(pit ? PitMode.InPit : PitMode.None, pit ? PitMode.InPit : PitMode.None)
                    .SetParticipantMotion(0, 100, 10, frontZ, 0, 0, 0, 0).SetParticipantLapDistance(0, 3900);
                var view = BuildTiming(fixture);
                tracker.Apply(view, 1);
                return view;
            }
            AssertEqual(0, Sample(true, 90).AheadParticipantIndex);
            AssertEqual("▲", Sample(true, 80).AheadDistanceTrendArrow);
            var track = Sample(false, 80);
            AssertEqual(0, track.AheadParticipantIndex); // same car, different measurement source
            AssertEqual("100m", track.AheadDistance);
            AssertEqual(string.Empty, track.AheadDistanceTrendArrow);
            AssertEqual("~20m", Sample(true, 80).AheadDistance);
            AssertEqual(string.Empty, Sample(true, 80).AheadDistanceTrendArrow);
        }

        private static void RelativeLapGapsOnlyInRace()
        {
            var fixture = new RawFixtureBuilder(4).SetViewedIndex(1).SetTrackTelemetry(1000, 300)
                .SetParticipant(0, true, "AHEAD", 1, 3, 4, RaceState.Racing, PitMode.None)
                .SetParticipant(2, true, "BEHIND", 3, 0, 1, RaceState.Racing, PitMode.None)
                .SetParticipantLapDistance(0, 550).SetParticipantLapDistance(1, 500)
                .SetParticipantLapDistance(2, 460).SetParticipantLapDistance(3, 100)
                .SetSplitAhead(0.842f).SetSplitBehind(1.127f);
            var tracker = new RelativeDistanceTrendTracker();
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Test, SessionState.TimeAttack })
            {
                OverlayViewModel view = BuildTiming(fixture.SetSession(SessionState.Race));
                tracker.Apply(view, 1);
                tracker.Apply(view, 1);
                AssertEqual("랩 1", view.AheadLapGap);
                AssertEqual("랩 2", view.BehindLapGap);
                view = BuildTiming(fixture.SetSession(session));
                for (int sample = 0; sample < 3; sample++)
                {
                    tracker.Apply(view, 1); // same generation: previous Race labels must clear immediately
                    AssertFalse(view.AheadLapGapCandidate.HasValue);
                    AssertFalse(view.BehindLapGapCandidate.HasValue);
                    AssertEqual(string.Empty, view.AheadLapGap);
                    AssertEqual(string.Empty, view.BehindLapGap);
                    AssertEqual("+0.842s", view.AheadGap);
                    AssertEqual("+1.127s", view.BehindGap);
                    AssertEqual("50m", view.AheadDistance);
                    AssertEqual("40m", view.BehindDistance);
                }
                if (session == SessionState.Practice)
                {
                    var panel = new RelativeDriversView();
                    panel.SetViewModel(view);
                    panel.Measure(new Size(panel.Width, panel.Height));
                    panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
                    CaptureLayout(panel, "practice-relative-no-lap");
                }
            }
        }

        private static void PrestartRelativeUsesWorldPositions()
        {
            var fixture = new RawFixtureBuilder(4).SetViewedIndex(1).SetTrackTelemetry(1000, 300)
                .SetSplitAhead(0.842f).SetSplitBehind(1.127f);
            for (int index = 0; index < 4; index++)
                fixture.SetParticipant(index, true, "GRID_" + index, (uint)index + 1, 0, 1, RaceState.NotStarted, PitMode.None)
                    .SetParticipantLapDistance(index, 0);
            // yaw 0 faces -Z. Physical neighbours deliberately differ from ranking neighbours.
            fixture.SetParticipantMotion(1, 100, 10, 100, 0, 0, 0, 0)
                .SetParticipantMotion(0, 100, 10, 115, 0, 0, 0, 0)
                .SetParticipantMotion(2, 100, 10, 95, 0, 0, 0, 0)
                .SetParticipantMotion(3, 100, 10, 90, 0, 0, 0, 0)
                .SetParticipantVehicle(2, "Mercedes AMG SafetyCar", "SafetyCar");
            var tracker = new RelativeDistanceTrendTracker();
            OverlayViewModel view = BuildTiming(fixture);
            AssertEqual(3, view.AheadParticipantIndex);
            AssertEqual(0, view.BehindParticipantIndex);
            AssertEqual("~10m", view.AheadDistance);
            AssertEqual("~15m", view.BehindDistance);
            AssertEqual("—", view.AheadGap); // never copy a ranking neighbour's time
            AssertEqual("—", view.BehindGap);
            AssertTrue(view.IsBottomGapPanelVisible);
            AssertFalse(view.AheadLapGapCandidate.HasValue);
            tracker.Apply(view, 1);
            tracker.Apply(view, 1);
            AssertEqual(string.Empty, view.AheadLapGap);

            fixture.SetParticipantMotion(3, 100, 10, 80, 0, 0, 0, 10)
                .SetParticipantMotion(0, 100, 10, 110, 0, 0, 0, 10);
            view = BuildTiming(fixture); // all lap distances still zero: display must already move
            tracker.Apply(view, 1);
            AssertEqual("~20m", view.AheadDistance);
            AssertEqual("~10m", view.BehindDistance);
            AssertEqual("#FF7777", view.AheadDistanceColor);
            AssertEqual("#FF7777", view.BehindDistanceColor);

            fixture.SetParticipant(3, true, "GRID_3", 1, 0, 1, RaceState.Racing, PitMode.None).SetParticipantLapDistance(3, 25)
                .SetParticipant(0, true, "GRID_0", 3, 0, 1, RaceState.Racing, PitMode.None).SetParticipantLapDistance(0, 0)
                .SetParticipantLapDistance(1, 5);
            view = BuildTiming(fixture); // player/front crossed; rear has not. Rank reorder must not corrupt selection.
            AssertEqual(3, view.AheadParticipantIndex);
            AssertEqual(0, view.BehindParticipantIndex);
            AssertEqual("~20m", view.AheadDistance);
            AssertEqual("~10m", view.BehindDistance);
            AssertEqual("+0.842s", view.AheadGap);
            AssertEqual("+1.127s", view.BehindGap);
            AssertFalse(view.BehindLapGapCandidate.HasValue);

            view = BuildTiming(fixture.SetParticipantLapDistance(0, 995));
            AssertEqual("20m", view.AheadDistance); // all progress initialized: use native track distance again
            AssertEqual("10m", view.BehindDistance);
            AssertEqual(0, view.AheadLapGapCandidate);
            AssertEqual(0, view.BehindLapGapCandidate);
        }

        private static void PrestartRelativeRejectsUnknownPositions()
        {
            var fixture = new RawFixtureBuilder(3).SetViewedIndex(1).SetTrackTelemetry(1000, 300)
                .SetSplitAhead(-1).SetSplitBehind(-1);
            for (int index = 0; index < 3; index++)
                fixture.SetParticipant(index, true, "GRID_" + index, (uint)index + 1, 0, 1, RaceState.Racing, PitMode.None)
                    .SetParticipantLapDistance(index, 0);
            OverlayViewModel view = BuildTiming(fixture); // no world data: no rank fallback, duplicate P1 or fabricated 0m
            AssertEqual(-1, view.AheadParticipantIndex);
            AssertEqual(-1, view.BehindParticipantIndex);
            AssertEqual("—", view.AheadDistance);
            AssertEqual("—", view.BehindDistance);
            AssertFalse(view.IsBottomGapPanelVisible);

            fixture.SetParticipantMotion(1, 100, 10, 100, 0, 0, 0, 0);
            foreach ((float x, float y, float z, float yaw) in new[]
            {
                (0f, 0f, 0f, 0f), (float.NaN, 10f, 90f, 0f), (100f, 10f, float.PositiveInfinity, 0f),
                (100f, 10f, 90f, float.NaN), (100f, 10f, 100f, 0f), (102f, 10f, 100f, 0f), // overlapping/alongside
                (120f, 10f, 95f, 0f), (100f, 20f, 90f, 0f), (100f, 10f, -100f, 0f), // lateral/overpass/distant
                (100f, 10f, 90f, (float)Math.PI) // opposite direction on another track section
            })
            {
                view = BuildTiming(fixture.SetParticipantMotion(0, x, y, z, 0, yaw, 0, 0));
                AssertEqual(-1, view.AheadParticipantIndex);
                AssertEqual(-1, view.BehindParticipantIndex);
            }
            // A different heading must rotate ahead/behind, not assume world Z is the track direction.
            float turn = (float)(Math.PI / 2);
            fixture.SetParticipantMotion(1, 100, 10, 100, 0, turn, 0, 0)
                .SetParticipantMotion(0, 90, 10, 100, 0, turn, 0, 0)
                .SetParticipantMotion(2, 115, 10, 100, 0, turn, 0, 0);
            view = BuildTiming(fixture.SetTrackTelemetry(0, 300));
            AssertEqual("~10m", view.AheadDistance); // no track length needed for nearby world distance
            AssertEqual("~15m", view.BehindDistance);
            AssertEqual("—", view.AheadGap); // absence of time remains explicit
            AssertFalse(view.AheadLapGapCandidate.HasValue);
            foreach ((bool active, RaceState state, PitMode pit) in new[]
            {
                (false, RaceState.Racing, PitMode.None), (true, RaceState.Retired, PitMode.None),
                (true, RaceState.Racing, PitMode.InGarage)
            })
            {
                view = BuildTiming(fixture.SetParticipant(0, active, "GRID_0", 1, 0, 1, state, pit).SetParticipantLapDistance(0, 0));
                AssertEqual(-1, view.AheadParticipantIndex);
                AssertEqual(2, view.BehindParticipantIndex);
            }
        }

        private static void RelativeTimeAndDistanceRemainVisible()
        {
            foreach (bool lapped in new[] { false, true })
            foreach (bool hasTime in new[] { false, true })
            foreach ((int width, int height) in new[] { (520, 104), (416, 166), (780, 83) })
            {
                var view = new RelativeDriversView();
                OverlayViewModel timing = DemoSnapshotFactory.CreateViewModel(false);
                timing.AheadParticipantKey = timing.BehindParticipantKey = string.Empty; // static layout, no entrance motion
                timing.AheadGap = hasTime ? (lapped ? "+123.456s" : "+0.842s") : "—";
                timing.BehindGap = hasTime ? (lapped ? "+987.654s" : "+1.127s") : "—";
                timing.AheadLapGap = lapped ? "랩 1" : string.Empty;
                timing.BehindLapGap = lapped ? "랩 2" : string.Empty;
                timing.AheadDistance = "1234m";
                view.SetViewModel(timing);
                double scale = Math.Min(width / 520.0, height / 104.0);
                view.Width = width / scale;
                view.Height = height / scale;
                var host = new Viewbox { Stretch = Stretch.Uniform, Child = view };
                var size = new Size(width, height);
                host.Measure(size);
                host.Arrange(new Rect(size));
                PumpDispatcher();
                host.UpdateLayout();
                foreach (string side in new[] { "Ahead", "Behind" })
                {
                    Grid row = Named<Grid>(view, side + "Row");
                    TextBlock gap = Named<TextBlock>(view, side + "TimeGapText");
                    TextBlock lap = Named<TextBlock>(view, side + "LapGapText");
                    TextBlock name = Named<TextBlock>(view, side + "NameText");
                    Viewbox gapPanel = Named<Viewbox>(view, side + "GapPanel");
                    StackPanel distance = Named<StackPanel>(view, side + "DistancePanel");
                    AssertEqual(side == "Ahead" ? timing.AheadGap : timing.BehindGap, gap.Text);
                    AssertEqual(side == "Ahead" ? timing.AheadLapGap : timing.BehindLapGap, lap.Text);
                    AssertEqual(lapped ? Visibility.Visible : Visibility.Collapsed, lap.Visibility);
                    AssertEqual(hasTime || !lapped ? Visibility.Visible : Visibility.Collapsed, gap.Visibility);
                    AssertEqual(3, Grid.GetColumn(gapPanel)); // original right-hand gap slot, not below the name
                    AssertTrue(Descendants<TextBlock>(gapPanel).Contains(lap));
                    AssertTrue(Descendants<TextBlock>(gapPanel).Contains(gap));
                    Rect gapBounds = gapPanel.TransformToAncestor(row).TransformBounds(new Rect(gapPanel.RenderSize));
                    Rect nameBounds = name.TransformToAncestor(row).TransformBounds(new Rect(name.RenderSize));
                    Rect distanceBounds = distance.TransformToAncestor(row).TransformBounds(new Rect(distance.RenderSize));
                    AssertTrue(nameBounds.Right <= gapBounds.Left);
                    AssertTrue(gapBounds.Right <= distanceBounds.Left);
                    AssertTrue(distanceBounds.Right <= row.ActualWidth + 1);
                    foreach (FrameworkElement element in new FrameworkElement[] { gap, distance, lap })
                    {
                        if (element.Visibility != Visibility.Visible) continue;
                        Rect bounds = element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));
                        AssertTrue(bounds.Left >= 0 && bounds.Top >= 0);
                        AssertTrue(bounds.Right <= width + 1 && bounds.Bottom <= height + 1);
                    }
                    if (lapped && hasTime)
                    {
                        Rect lapBounds = lap.TransformToAncestor(row).TransformBounds(new Rect(lap.RenderSize));
                        Rect timeBounds = gap.TransformToAncestor(row).TransformBounds(new Rect(gap.RenderSize));
                        AssertTrue(lapBounds.Bottom <= timeBounds.Top + 1);
                    }
                }
                CaptureLayout(host, "relative-gap-" + width + "x" + height + "-lap" + lapped + "-time" + hasTime);
            }
        }

        private static void CompactUiMetricsMeetTarget()
        {
            AssertEqual(1.00, OverlayUiMetrics.TargetScale);
            AssertTrue(OverlayUiMetrics.FontDriverName >= 24);
            AssertTrue(OverlayUiMetrics.RowPitch >= 37);
            AssertTrue(OverlayUiMetrics.TowerHeight + OverlayUiMetrics.ComponentGap + OverlayUiMetrics.RelativeHeight <= 722);
            AssertEqual(15, LeftTowerLayoutMetrics.RankingRows);
            AssertTrue(LeftTowerLayoutMetrics.RequiredHeight <= LeftTowerLayoutMetrics.DesiredHeight);
        }

        private static void TimingTowerRowCapacityFollowsResize()
        {
            foreach (int rows in new[] { 2, 15, 20, 64 })
            {
                int height = LeftTowerLayoutMetrics.RequiredHeightForRows(rows, false);
                AssertEqual(rows, LeftTowerLayoutMetrics.CalculateRankingRows(OverlayUiMetrics.TowerWidth, height, false));
            }

            AssertEqual(19, LeftTowerLayoutMetrics.CalculateRankingRows(
                OverlayUiMetrics.TowerWidth,
                LeftTowerLayoutMetrics.RequiredHeightForRows(20, false) - 1,
                false));
            AssertEqual(15, LeftTowerLayoutMetrics.CalculateRankingRows(OverlayUiMetrics.TowerWidth * 2, OverlayUiMetrics.TowerHeight * 2, false));
            AssertEqual(15, LeftTowerLayoutMetrics.CalculateRankingRows(
                OverlayUiMetrics.TowerWidth,
                LeftTowerLayoutMetrics.RequiredHeightForRows(15, true),
                true));
        }

        private static void TimingTowerLastRowStaysInsideBounds()
        {
            foreach (int capacity in new[] { 15, 20 })
            {
                var view = new OverlayHudView();
                RankingRowViewModel[] rows = Enumerable.Range(1, capacity)
                    .Select(index => Row(index, "P" + index, "DRIVER " + index, "0:20.000"))
                    .ToArray();
                OverlayViewModel timing = TimingRows(rows);
                timing.RankingRowCapacity = capacity;
                view.SetViewModel(timing);
                int requiredHeight = LeftTowerLayoutMetrics.RequiredHeightForRows(capacity, false);
                var size = new Size(OverlayUiMetrics.TowerWidth, requiredHeight);
                view.Measure(size);
                view.Arrange(new Rect(size));
                view.UpdateLayout();

                ItemsControl items = FindDescendant<ItemsControl>(view) ?? throw new InvalidOperationException("Ranking items missing.");
                ContentPresenter last = Container(items, capacity - 1);
                double bottom = last.TranslatePoint(new Point(0, last.ActualHeight), view).Y;
                AssertEqual(capacity, items.Items.Count);
                AssertEqual((double)requiredHeight, view.Height);
                AssertTrue(bottom <= view.ActualHeight + 0.5);
            }
        }

        private static void ExpandedTimingTowerPreservesSelection()
        {
            TelemetrySnapshot snapshot = DemoSnapshotFactory.CreateSnapshot();
            ParticipantSnapshot local = ResolveLocal(snapshot);
            LeagueClassification league = Classify(snapshot);
            OverlayViewModel compact = OverlayViewModel.Build(
                snapshot, local, league, 30, 20, false, "TEST", rankingRowCapacity: 10);
            AssertEqual(10, compact.RankingRows.Count);
            AssertTrue(compact.RankingRows.Take(9).Select(row => row.Position)
                .SequenceEqual(Enumerable.Range(1, 9).Select(position => "P" + position)));
            AssertTrue(compact.RankingRows[9].IsPlayer);
            AssertEqual("P16", compact.RankingRows[9].Position);

            OverlayViewModel expanded = OverlayViewModel.Build(
                snapshot, local, league, 30, 20, false, "TEST", rankingRowCapacity: 20);
            AssertEqual(20, expanded.RankingRows.Count);
            AssertTrue(expanded.RankingRows.Select(row => row.Position)
                .SequenceEqual(Enumerable.Range(1, 20).Select(position => "P" + position)));
            AssertEqual(20, expanded.RankingRows.Select(row => row.ParticipantIndex).Distinct().Count());
            AssertTrue(expanded.IsPlayerVisibleInRanking);
        }

        private static void TimingRefreshUpdatesRowsInPlace()
        {
            var view = new OverlayHudView();
            view.SetViewModel(TimingRows(Row(1, "P1", "ALPHA", "0:20.000"), Row(2, "P2", "BRAVO", "0:21.000")));
            LayoutTower(view);
            ItemsControl items = FindDescendant<ItemsControl>(view) ?? throw new InvalidOperationException("Ranking items missing.");
            object firstItem = items.Items[0];
            ContentPresenter firstPresenter = Container(items, 0);
            var entry = new TranslateTransform();
            firstPresenter.RenderTransform = entry;
            entry.BeginAnimation(TranslateTransform.XProperty, null);
            entry.X = 0;
            int collectionChanges = 0;
            ((INotifyCollectionChanged)items.ItemsSource).CollectionChanged += (sender, args) => collectionChanges++;

            for (int frame = 1; frame <= 120; frame++)
            {
                view.SetViewModel(TimingRows(
                    Row(1, "P1", "ALPHA", "0:20." + frame.ToString("000")),
                    Row(2, "P2", "BRAVO", "0:21." + frame.ToString("000"))));
            }

            AssertEqual(0, collectionChanges);
            AssertTrue(ReferenceEquals(firstItem, items.Items[0]));
            AssertTrue(ReferenceEquals(firstPresenter, Container(items, 0)));
            AssertFalse(entry.HasAnimatedProperties);
            AssertEqual("0:20.120", Descendants<TextBlock>(firstPresenter).Single(text => text.Text == "0:20.120").Text);
            AssertEqual(1.0, firstPresenter.Opacity);
            AssertFalse(DependencyPropertyHelper.GetValueSource(firstPresenter, UIElement.OpacityProperty).IsAnimated);
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
            AssertEqual("1:40.973", player.CurrentTime);
            AssertTrue(OverlayUiMetrics.FontDriverName > OverlayUiMetrics.FontTitle);
            AssertFalse(player.IsDimmed);
            AssertEqual(ParticipantRowDisplayState.Active, player.DisplayState);
        }

        private static void ClassBadgePaletteIsExplicitAndStable()
        {
            AssertEqual("GT3", ClassBadgePalette.Resolve("GT3").Family);
            AssertEqual("GT3", ClassBadgePalette.Resolve("GT3_Gen2").Family);
            AssertEqual("GT4", ClassBadgePalette.Resolve("GT4").Family);
            AssertEqual("GTE", ClassBadgePalette.Resolve("GTE").Family);
            AssertEqual("P1/DPI", ClassBadgePalette.Resolve("DPI").Family);
            AssertEqual("P2", ClassBadgePalette.Resolve("LMP2").Family);
            AssertEqual("P3", ClassBadgePalette.Resolve("LMP3").Family);
            AssertEqual("FORMULA", ClassBadgePalette.Resolve("F-Hitech_Gen2_LD").Family);
            AssertEqual("FALLBACK", ClassBadgePalette.Resolve("unmapped-class").Family);
            AssertEqual(ClassBadgePalette.FallbackBackground, ClassBadgePalette.Resolve("unmapped-class").Background);
        }

        private static void ClassAndTimingTypographyFitsTower()
        {
            AssertTrue(OverlayUiMetrics.FontClass >= 12);
            AssertTrue(OverlayUiMetrics.FontTiming >= 18);
            AssertTrue(OverlayUiMetrics.RowPitch >= 38);
            AssertTrue(OverlayUiMetrics.TowerHeight + OverlayUiMetrics.ComponentGap + OverlayUiMetrics.RelativeHeight <= 722);

            var view = new OverlayHudView();
            view.SetViewModel(DemoSnapshotFactory.CreateShell(false).Timing);
            var size = new Size(OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            TextBlock classText = Descendants<TextBlock>(view).First(item => item.Text == "GT3");
            TextBlock timeText = Descendants<TextBlock>(view).First(item => item.Text == "1:40.973");
            AssertEqual(OverlayUiMetrics.FontClass, classText.FontSize);
            AssertEqual(OverlayUiMetrics.FontTiming, timeText.FontSize);
            AssertTrue(classText.ActualHeight <= 36);
            AssertTrue(timeText.DesiredSize.Width <= 104);
            AssertTrue(timeText.ActualHeight <= 36);
        }

        private static void OnlyInactiveParticipantStatesAreDimmed()
        {
            ParticipantSnapshot active = ParticipantForStyle(true, RaceState.Racing, PitMode.None);
            ParticipantSnapshot pit = ParticipantForStyle(true, RaceState.Racing, PitMode.InPit);
            ParticipantSnapshot finished = ParticipantForStyle(true, RaceState.Finished, PitMode.None);
            AssertFalse(ParticipantRowStateResolver.ShouldDim(ParticipantRowStateResolver.Resolve(active)));
            AssertFalse(ParticipantRowStateResolver.ShouldDim(ParticipantRowStateResolver.Resolve(pit)));
            AssertFalse(ParticipantRowStateResolver.ShouldDim(ParticipantRowStateResolver.Resolve(finished)));

            foreach (RaceState state in new[] { RaceState.Retired, RaceState.Dnf, RaceState.Disqualified })
            {
                ParticipantRowDisplayState display = ParticipantRowStateResolver.Resolve(ParticipantForStyle(true, state, PitMode.None));
                AssertEqual(ParticipantRowDisplayState.TerminalInactive, display);
                AssertTrue(ParticipantRowStateResolver.ShouldDim(display));
            }

            ParticipantRowDisplayState disconnected = ParticipantRowStateResolver.Resolve(ParticipantForStyle(false, RaceState.Racing, PitMode.None));
            AssertEqual(ParticipantRowDisplayState.Disconnected, disconnected);
            AssertTrue(ParticipantRowStateResolver.ShouldDim(disconnected));
        }

        private static void StatusChangesNeverDimActiveRows()
        {
            var view = new OverlayHudView();
            view.SetViewModel(TimingRows(Row(1, "P1", "ACTIVE", "0:20.000")));
            view.Measure(new Size(OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.Arrange(new Rect(0, 0, OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.UpdateLayout();

            RankingRowViewModel changed = Row(1, "P1", "ACTIVE", "0:20.050");
            changed.Status = "PIT";
            view.SetViewModel(TimingRows(changed));
            ItemsControl items = FindDescendant<ItemsControl>(view) ?? throw new InvalidOperationException("Ranking items missing.");
            ContentPresenter presenter = items.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter
                ?? throw new InvalidOperationException("Ranking row container missing.");
            ValueSource opacitySource = DependencyPropertyHelper.GetValueSource(presenter, UIElement.OpacityProperty);
            AssertEqual(1.0, presenter.Opacity);
            AssertFalse(opacitySource.IsAnimated);
        }

        private static void PracticeActiveUsesCurrentTiming()
        {
            OverlayViewModel timing = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Practice)
                .SetParticipant(3, true, "LEE", 4, 2, 3, RaceState.Racing, PitMode.None)
                .SetCurrentTiming(42.5f, 42.5f, -1, -1));
            AssertEqual("2:20.881", PlayerRow(timing).CurrentTime);
            AssertEqual("0:42.500", timing.CurrentLapText);
        }

        private static void PracticeCompletedUsesBestLap()
        {
            OverlayViewModel completed = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Practice)
                .SetParticipant(3, true, "LEE", 4, 2, 3, RaceState.Finished, PitMode.None)
                .SetParticipantLapTimes(3, 91.234f, 93.1f)
                .SetCurrentTiming(34.7f, 34.7f, -1, -1));
            AssertEqual("1:31.234", PlayerRow(completed).CurrentTime);

            OverlayViewModel noBest = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Practice)
                .SetParticipant(3, true, "LEE", 4, 2, 3, RaceState.Finished, PitMode.None)
                .SetParticipantLapTimes(3, -1, -1)
                .SetCurrentTiming(34.7f, 34.7f, -1, -1));
            AssertEqual("--", PlayerRow(noBest).CurrentTime);
        }

        private static void QualifyingActiveUsesCurrentTiming()
        {
            OverlayViewModel timing = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Qualify)
                .SetParticipant(3, true, "LEE", 4, 2, 3, RaceState.Racing, PitMode.None)
                .SetCurrentTiming(51.125f, 51.125f, -1, -1));
            AssertEqual("2:20.881", PlayerRow(timing).CurrentTime);
            AssertEqual("0:51.125", timing.CurrentLapText);
        }

        private static void QualifyingCompletedUsesBestLap()
        {
            OverlayViewModel timing = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Qualify)
                .SetParticipant(3, true, "LEE", 4, 2, 3, RaceState.Finished, PitMode.None)
                .SetParticipantLapTimes(3, 88.765f, 89.2f)
                .SetCurrentTiming(37.4f, 37.4f, -1, -1));
            AssertEqual("1:28.765", PlayerRow(timing).CurrentTime);
        }

        private static void RaceTimingStopsPerParticipant()
        {
            OverlayViewModel leaderFinished = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Race)
                .SetParticipant(0, true, "LEADER", 1, 4, 5, RaceState.Finished, PitMode.None)
                .SetParticipant(3, true, "LEE", 4, 3, 4, RaceState.Racing, PitMode.None)
                .SetCurrentTiming(34.125f, 34.125f, -1, -1));
            AssertEqual("FIN", leaderFinished.RankingRows.Single(row => row.ParticipantIndex == 0).Status);
            AssertEqual("2:17.881", leaderFinished.RankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("2:20.881", PlayerRow(leaderFinished).CurrentTime);
            AssertEqual("0:34.125", leaderFinished.CurrentLapText);

            OverlayViewModel trailingFinished = BuildTiming(new RawFixtureBuilder(4)
                .SetSession(SessionState.Race)
                .SetParticipant(0, true, "LEADER", 1, 4, 5, RaceState.Finished, PitMode.None)
                .SetParticipant(3, true, "LEE", 4, 4, 5, RaceState.Finished, PitMode.None)
                .SetCurrentTiming(39.875f, 39.875f, -1, -1));
            AssertEqual("FIN", PlayerRow(trailingFinished).Status);
            AssertEqual("2:20.881", PlayerRow(trailingFinished).CurrentTime);
        }

        private static void TerminalStatesNeverKeepTiming()
        {
            foreach ((RaceState state, string expected) in new[]
            {
                (RaceState.Disqualified, "DSQ"),
                (RaceState.Retired, "RET"),
                (RaceState.Dnf, "DNF")
            })
            {
                OverlayViewModel timing = BuildTiming(new RawFixtureBuilder(4)
                    .SetSession(SessionState.Race)
                    .SetParticipant(3, true, "LEE", 4, 2, 3, state, PitMode.None)
                    .SetCurrentTiming(38.25f, 38.25f, -1, -1));
                AssertEqual(expected, PlayerRow(timing).Status);
                AssertEqual("2:20.881", PlayerRow(timing).CurrentTime);
                AssertTrue(PlayerRow(timing).IsDimmed);
                AssertEqual(ParticipantRowDisplayState.TerminalInactive, PlayerRow(timing).DisplayState);
            }
        }

        private static void PositionAnimationSurvivesTimingRefresh()
        {
            var view = new OverlayHudView();
            view.SetViewModel(TimingRows(
                Row(1, "P1", "ALPHA", "0:20.000"),
                Row(2, "P2", "BRAVO", "0:21.000")));
            view.Measure(new Size(OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.Arrange(new Rect(0, 0, OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.UpdateLayout();

            view.SetViewModel(TimingRows(
                Row(2, "P1", "BRAVO", "0:22.000"),
                Row(1, "P2", "ALPHA", "0:23.000")));
            ItemsControl items = FindDescendant<ItemsControl>(view) ?? throw new InvalidOperationException("Ranking items missing.");
            ContentPresenter first = items.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter
                ?? throw new InvalidOperationException("Ranking row container missing.");
            AssertTrue(first.RenderTransform is TranslateTransform reordered && reordered.HasAnimatedProperties);

            view.SetViewModel(TimingRows(
                Row(2, "P1", "BRAVO", "0:22.050"),
                Row(1, "P2", "ALPHA", "0:23.050")));
            first = items.ItemContainerGenerator.ContainerFromIndex(0) as ContentPresenter
                ?? throw new InvalidOperationException("Refreshed ranking row container missing.");
            AssertTrue(first.RenderTransform is TranslateTransform refreshed && refreshed.HasAnimatedProperties);
        }

        private static void WaitingOverlayContentFitsDesignBounds()
        {
            var view = new MultiplayerWaitingOverlayView
            {
                DataContext = new MultiplayerWaitingOverlayViewModel
                {
                    Title = "멀티플레이어 세션 대기",
                    SessionLabel = "예선 결과 확정 및 다음 세션 준비",
                    ParticipantCountText = "리그 120 / 원본 128",
                    RemainingLabel = "남은 시간",
                    RemainingValue = "세션 종료 대기"
                }
            };
            var size = new Size(OverlayUiMetrics.WaitingWidth, OverlayUiMetrics.WaitingHeight);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            foreach (TextBlock text in Descendants<TextBlock>(view))
            {
                Rect bounds = text.TransformToAncestor(view).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                AssertTrue(bounds.Left >= -0.5 && bounds.Top >= -0.5);
                AssertTrue(bounds.Right <= OverlayUiMetrics.WaitingWidth + 0.5);
                AssertTrue(bounds.Bottom <= OverlayUiMetrics.WaitingHeight + 0.5);
            }
        }

        private static void WaitingOverlayFitsLegacySavedBounds()
        {
            const int viewportWidth = 3440;
            const int viewportHeight = 1440;
            var profile = new OverlayLayoutProfile();
            profile.Capture(
                OverlayComponentKeys.Waiting,
                new OverlayBounds(520, 114, 326, 125),
                viewportWidth,
                viewportHeight);

            OverlayBounds saved = profile.Resolve(
                OverlayComponentKeys.Waiting,
                new OverlayBounds(0, 0, OverlayUiMetrics.WaitingWidth, OverlayUiMetrics.WaitingHeight),
                viewportWidth,
                viewportHeight);
            AssertEqual(326, saved.Width);
            AssertEqual(125, saved.Height);

            var content = new MultiplayerWaitingOverlayView
            {
                Width = OverlayUiMetrics.WaitingWidth,
                Height = OverlayUiMetrics.WaitingHeight,
                DataContext = new MultiplayerWaitingOverlayViewModel
                {
                    Title = "멀티플레이어 세션 대기",
                    SessionLabel = "예선 결과 확정 및 다음 세션 준비",
                    ParticipantCountText = "리그 120 / 원본 128",
                    RemainingLabel = "남은 시간",
                    RemainingValue = "세션 종료 대기"
                }
            };
            var surface = new Viewbox { Stretch = Stretch.Uniform, Child = content };
            var size = new Size(saved.Width, saved.Height);
            surface.Measure(size);
            surface.Arrange(new Rect(size));
            surface.UpdateLayout();

            AssertFalse(content.ClipToBounds);
            AssertTrue(content.ActualWidth <= OverlayUiMetrics.WaitingWidth + 0.5);
            AssertTrue(content.ActualHeight <= OverlayUiMetrics.WaitingHeight + 0.5);
            AssertTrue(surface.ActualWidth <= saved.Width + 0.5);
            AssertTrue(surface.ActualHeight <= saved.Height + 0.5);
        }

        private static OverlayViewModel BuildTiming(RawFixtureBuilder fixture)
        {
            TelemetrySnapshot snapshot = Parse(fixture);
            ParticipantSnapshot local = ResolveLocal(snapshot);
            return OverlayViewModel.Build(snapshot, local, Classify(snapshot), 30, 20, false, "TEST");
        }

        private static RankingRowViewModel PlayerRow(OverlayViewModel timing)
            => timing.RankingRows.Single(row => row.IsPlayer);

        private static RankingRowViewModel Row(int participantIndex, string position, string name, string currentTime)
            => new RankingRowViewModel { ParticipantIndex = participantIndex, Position = position, Name = name, CurrentTime = currentTime };

        private static ParticipantSnapshot ParticipantForStyle(bool active, RaceState raceState, PitMode pitMode)
            => new ParticipantSnapshot(
                0,
                active,
                "STYLE",
                1,
                2,
                3,
                1,
                (uint)raceState,
                (uint)pitMode,
                90,
                91,
                "Fixture",
                "GT3");

        private static ParticipantSnapshot ProgressParticipant(int index, string name, uint lapsCompleted, float lapDistance)
            => new ParticipantSnapshot(
                index,
                true,
                name,
                (uint)index + 1,
                lapsCompleted,
                lapsCompleted + 1,
                1,
                (uint)RaceState.Racing,
                (uint)PitMode.None,
                90,
                91,
                "Car",
                "GT3",
                lapDistance);

        private static OverlayViewModel LapCandidateView(string participantKey, int laps)
            => new OverlayViewModel
            {
                AheadParticipantIndex = 1,
                AheadParticipantKey = participantKey,
                AheadDistanceMeters = 50,
                AheadLapGapCandidate = laps,
                AheadGap = "+0.500"
            };

        private static OverlayViewModel TimingRows(params RankingRowViewModel[] rows)
            => new OverlayViewModel { RankingRows = rows, RankingRangeText = rows.Length == 0 ? "순위" : rows[0].Position + " — " + rows[^1].Position };

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
            AssertTrue(text.Contains("1:40.973", StringComparer.Ordinal));
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

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
            => Descendants<T>(root).FirstOrDefault();

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T typed) yield return typed;
                foreach (T descendant in Descendants<T>(child)) yield return descendant;
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
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(Parse(fixture), 0, FixedTime(), SessionPlayMode.Multiplayer);

            AssertEqual(MultiplayerOverlayMode.Waiting, decision.Mode);
            AssertNotNull(decision.Waiting);
            AssertEqual("멀티플레이어 세션 대기", decision.Waiting?.Title);
            AssertEqual("예선", decision.Waiting?.SessionLabel);
            AssertEqual("리그 3 / 원본 4", decision.Waiting?.ParticipantCountText);
            AssertEqual("3:31", decision.Waiting?.RemainingValue);
        }

        private static void QualifyingWithoutTimerRemainsGameplay()
        {
            var fixture = new RawFixtureBuilder(4)
                .SetGameState(GameState.InGamePlaying)
                .SetSession(SessionState.Qualify)
                .SetGlobalRaceState(RaceState.NotStarted)
                .SetSessionTiming(15, 0, -1);
            MultiplayerOverlayDecision decision = new MultiplayerWaitingOverlayController().Observe(Parse(fixture), 0, FixedTime());

            AssertEqual(MultiplayerOverlayMode.Gameplay, decision.Mode);
            AssertEqual("GAMEPLAY", decision.Reason);
            AssertNull(decision.Waiting);
            AssertEqual("—", decision.RemainingDisplayTextOverride);
        }

        private static void WaitingOverlayIncludesSingleExcludesReplay()
        {
            var controller = new MultiplayerWaitingOverlayController();
            TelemetrySnapshot single = Parse(new RawFixtureBuilder(1)
                .SetViewedIndex(0)
                .SetGameState(GameState.InGameMenuTimeTicking)
                .SetSessionTiming(15, 0, 210));
            TelemetrySnapshot replay = Parse(new RawFixtureBuilder(4)
                .SetGameState(GameState.InGameReplay)
                .SetSessionTiming(15, 0, 210));

            AssertEqual(MultiplayerOverlayMode.Waiting, controller.Observe(single, 0, FixedTime()).Mode);
            AssertEqual("세션 대기 · 모드 미확인", controller.Observe(single, 0, FixedTime()).Waiting?.Title);
            AssertEqual(MultiplayerOverlayMode.Hidden, controller.Observe(replay, 1, FixedTime()).Mode);
        }

        private static void WaitingModeAndSessionLabels()
        {
            var controller = new MultiplayerWaitingOverlayController();
            foreach (int count in new[] { 1, 30 }) // AI count cannot imply multiplayer
            foreach ((SessionState session, string label) in new[]
            {
                (SessionState.Practice, "자유 연습 주행"), (SessionState.Qualify, "예선"),
                (SessionState.Race, "레이스"), (SessionState.Test, "테스트 주행"),
                (SessionState.Invalid, "세션 전환 중"), ((SessionState)999, "세션 미확인")
            })
            foreach ((SessionPlayMode mode, string title) in new[]
            {
                (SessionPlayMode.Unknown, "세션 대기 · 모드 미확인"),
                (SessionPlayMode.SinglePlayer, "싱글플레이어 세션 대기"),
                (SessionPlayMode.Multiplayer, "멀티플레이어 세션 대기")
            })
            {
                var fixture = new RawFixtureBuilder(count).SetViewedIndex(0)
                    .SetGameState(GameState.InGameMenuTimeTicking).SetSession(session);
                var decision = controller.Observe(Parse(fixture), 1, FixedTime(), mode);
                AssertEqual(MultiplayerOverlayMode.Waiting, decision.Mode);
                AssertEqual(title, decision.Waiting?.Title);
                AssertEqual(label, decision.Waiting?.SessionLabel);
                fixture.SetSessionActivityMetadata(0, true);
                AssertEqual(title, controller.Observe(Parse(fixture), 1, FixedTime(), mode).Waiting?.Title);
                if (count == 30 && session == SessionState.Practice)
                {
                    var panel = new MultiplayerWaitingOverlayView { DataContext = decision.Waiting };
                    var size = new Size(OverlayUiMetrics.WaitingWidth, OverlayUiMetrics.WaitingHeight);
                    panel.Measure(size);
                    panel.Arrange(new Rect(size));
                    panel.UpdateLayout();
                    foreach (TextBlock text in Descendants<TextBlock>(panel))
                    {
                        Rect bounds = text.TransformToAncestor(panel).TransformBounds(new Rect(text.RenderSize));
                        AssertTrue(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= size.Width && bounds.Bottom <= size.Height);
                    }
                    CaptureLayout(panel, "waiting-practice-" + mode);
                }
            }
            var status = new ClientStatusViewModel();
            var window = new ClientStatusWindow(status);
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(860, 780));
            root.Arrange(new Rect(0, 0, 860, 780));
            root.UpdateLayout();
            AssertFalse(Descendants<ComboBox>(root).Any());
            Named<Expander>(root, "ConnectionDetails").IsExpanded = true;
            root.UpdateLayout();
            var modeLabel = Named<TextBlock>(root, "SessionPlayModeLabel");
            AssertEqual(SessionPlayMode.Unknown, status.SessionPlayMode);
            foreach (SessionPlayMode mode in new[] { SessionPlayMode.SinglePlayer, SessionPlayMode.Multiplayer, SessionPlayMode.Unknown })
            {
                status.SessionPlayMode = mode;
                PumpDispatcher();
                AssertEqual(status.SessionPlayModeText, modeLabel.Text);
            }
            status.SessionPlayMode = (SessionPlayMode)999;
            PumpDispatcher();
            AssertEqual(SessionPlayMode.Unknown, status.SessionPlayMode);
            Named<Expander>(root, "ConnectionDetails").IsExpanded = true;
            root.UpdateLayout(); PumpDispatcher();
            AssertTrue(modeLabel.Visibility == Visibility.Visible && modeLabel.ActualHeight > 0);
            CaptureLayout(root, "status-automatic-mode");
            window.Close();
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
            AssertEqual("—", expired.RemainingDisplayTextOverride);
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
            AssertEqual(MultiplayerOverlayMode.Gameplay, unknown.Mode);
            AssertNull(unknown.Waiting);
            AssertEqual("—", unknown.RemainingDisplayTextOverride);
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

        private static void ForbiddenUploadDiagnostics()
        {
            const string token = "abcdef0123456789abcdef0123456789";
            foreach (var example in new[]
            {
                (Body: "{\"error\":\"SCOPE_FORBIDDEN\",\"requestId\":\"0123456789abcdef\",\"duplicate\":true}", Type: "application/json", Code: "SCOPE_FORBIDDEN"),
                (Body: "<html>Forbidden " + token + "</html>", Type: "text/html", Code: "HTTP_403_HTML"),
                (Body: "<html>" + new string('x', 5000) + token, Type: "text/html", Code: "HTTP_403_HTML"),
                (Body: "unreadable", Type: "text/plain", Code: "HTTP_403_OTHER"),
                (Body: "{\"error\":\"" + token + "\",\"requestId\":\"" + token + "\",\"message\":\"Authorization: Bearer " + token + "\"}", Type: "application/json", Code: "HTTP_403_JSON")
            }) WithTemporaryDirectory(directory =>
            {
                ActivityConnectionOptions options = ActivityConnectionOptions.Load(Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                options.ApiBaseUrl = "https://fixture.invalid/ams2";
                options.BearerToken = token;
                var diagnostics = new List<string>();
                var handler = new EnrollmentFixtureHandler("fixture-existing-installation", token)
                {
                    UploadReply = () => new HttpResponseMessage(HttpStatusCode.Forbidden)
                    { Content = example.Body == "unreadable" ? (HttpContent)new UnreadableErrorContent() : new StringContent(example.Body, Encoding.UTF8, example.Type) }
                };
                using var http = new HttpClient(handler);
                using var transport = new Cafe24ActivityUploadTransport(options, "fixture-existing-installation", "0.3.1-test", http)
                { FailureDiagnostic = diagnostics.Add };
                var queue = new ActivityUploadQueue(Path.Combine(directory, "witness-queue"));
                ActivityUploadItem original = queue.Enqueue("witness-forbidden", Cafe24ActivityUploadTransport.SessionWitnessEndpoint,
                    "witness:forbidden-fixture", "{\"schema\":\"ams2-session-witness-v1\"}").Item;
                var worker = new ActivityUploadWorker(queue, transport);
                AssertEqual(1, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Quarantined);
                AssertEqual(0, worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Attempted);
                ActivityUploadItem stored = queue.Scan().Single();
                AssertEqual(ActivityUploadStatus.QUARANTINED, stored.State.Status);
                AssertEqual(1, stored.State.AttemptCount);
                AssertEqual(example.Code, stored.State.LastResult);
                AssertEqual(original.Metadata.IdempotencyKey, stored.Metadata.IdempotencyKey);
                AssertEqual(original.Metadata.BodySha256, stored.Metadata.BodySha256);
                AssertTrue(original.PayloadUtf8.Span.SequenceEqual(stored.PayloadUtf8.Span));
                AssertEqual(original.Metadata.IdempotencyKey, handler.UploadIdempotencyKey);
                string telemetryRoot = Path.Combine(directory, "telemetry");
                CreatePendingCompactTelemetryChunk(telemetryRoot);
                var telemetryQueue = new TelemetryChunkUploadQueue(telemetryRoot);
                var telemetryWorker = new TelemetryChunkUploadWorker(telemetryQueue, transport);
                telemetryWorker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual(0, telemetryWorker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult().Attempted);
                AssertEqual(1, handler.UploadCalls);
                AssertEqual(1, handler.TelemetryCalls);
                AssertEqual(0, handler.EnrollmentCalls);
                AssertEqual(token, options.BearerToken);
                AssertEqual(2, diagnostics.Count);
                AssertTrue(diagnostics.All(value => value.Contains("code=" + example.Code) && value.Length < 600 && !value.Contains(token)));
                if (example.Code == "SCOPE_FORBIDDEN") AssertTrue(diagnostics.All(value => value.Contains("requestId=0123456789abcdef")));
                if (example.Body.Length > 4096) AssertTrue(diagnostics.All(value => value.Contains("EXCEEDS_4096_BYTES")));

                // A transient failure is still retryable and must not become SENT even
                // if an inconsistent error body includes duplicate=true.
                handler.UploadReply = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"error\":\"TEMPORARY\",\"duplicate\":true}", Encoding.UTF8, "application/json") };
                ActivityUploadTransportResult retry = transport.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual(503, retry.StatusCode);
                AssertFalse(retry.Duplicate);
                handler.UploadReply = null;
                ActivityUploadTransportResult success = transport.SendAsync(original, CancellationToken.None).GetAwaiter().GetResult();
                AssertEqual(201, success.StatusCode);
                AssertEqual(original.Metadata.IdempotencyKey, handler.UploadIdempotencyKey);
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
                AssertEqual("UNKNOWN", handler.TelemetryRaceMode);
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
                using var transport = new Cafe24ActivityUploadTransport(options, installationId, "0.2.3-beta.3", http);

                TelemetryChunkUploadTransportResult result = transport
                    .SendTelemetryChunkAsync(item, CancellationToken.None)
                    .GetAwaiter().GetResult();

                AssertTrue(result.Success);
                AssertEqual(Cafe24ActivityUploadTransport.CompactTelemetryContentType, handler.TelemetryContentType);
                AssertEqual("UNKNOWN", handler.TelemetryRaceMode);
                AssertEqual(item.Metadata.ChunkId, handler.TelemetryChunkId);
                AssertEqual(item.Metadata.SessionId, handler.TelemetrySessionId);
                AssertEqual(item.Metadata.AttemptId, handler.TelemetryAttemptId);
                AssertEqual(item.Metadata.Visibility.ToString(), handler.TelemetryVisibility);
                AssertEqual("0.2.3-beta.3", handler.TelemetryClientVersion);
                using var source = new MemoryStream(handler.TelemetryBody, false);
                CompactTelemetryEnvelope decoded = CompactTelemetryCodec.Decode(
                    TelemetryChunkSerializer.Gunzip(source, 4 * 1024 * 1024));
                AssertEqual(CompactTelemetrySchemaId.RaceEventV1, decoded.Block.SchemaId);
            });
        }

        private static void LongTrackUploadWaitsForServer()
        {
            foreach (var scenario in new[] {
                (Schema: CompactTelemetrySchemaId.RaceEventV2, Status: 400, Error: "COMPACT_SCHEMA_UNKNOWN", Retry: true),
                (Schema: CompactTelemetrySchemaId.RaceEventV1, Status: 400, Error: "COMPACT_SCHEMA_UNKNOWN", Retry: false),
                (Schema: CompactTelemetrySchemaId.RaceEventV2, Status: 400, Error: "COMPACT_VALUE_RANGE_INVALID", Retry: false),
                (Schema: CompactTelemetrySchemaId.RaceEventV2, Status: 403, Error: "COMPACT_SCHEMA_UNKNOWN", Retry: false),
                (Schema: CompactTelemetrySchemaId.RaceEventV2, Status: 422, Error: "COMPACT_SCHEMA_UNKNOWN", Retry: false) })
            {
                WithTemporaryDirectory(directory => {
                    string telemetryRoot = Path.Combine(directory, "future-telemetry");
                    CreatePendingCompactTelemetryChunk(telemetryRoot, scenario.Schema);
                    var queue = new TelemetryChunkUploadQueue(telemetryRoot);
                    var item = queue.GetDueBatch(1, DateTimeOffset.UtcNow).Single();
                    byte[] original = File.ReadAllBytes(item.ChunkPath);
                    var options = ActivityConnectionOptions.Load(Path.Combine(directory, ActivityConnectionOptions.DefaultFileName));
                    options.ApiBaseUrl = "https://fixture.invalid/ams2";
                    var handler = new EnrollmentFixtureHandler("long-track-http-fixture-0001", "compact_fixture_token_0000000001") {
                        UploadReply = () => new HttpResponseMessage((HttpStatusCode)scenario.Status) {
                            Content = new StringContent("{\"error\":\"" + scenario.Error + "\"}", Encoding.UTF8, "application/json") } };
                    using var http = new HttpClient(handler);
                    using var transport = new Cafe24ActivityUploadTransport(options, "long-track-http-fixture-0001", "0.7.0", http);
                    var worker = new TelemetryChunkUploadWorker(queue, transport);
                    var first = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                    AssertEqual(scenario.Retry ? 1 : 0, first.Retryable);
                    AssertEqual(scenario.Retry ? 0 : 1, first.Quarantined);
                    AssertTrue(original.SequenceEqual(File.ReadAllBytes(item.ChunkPath)));
                    if (!scenario.Retry) return;
                    var pending = TelemetryChunkSerializer.DeserializeMetadata(File.ReadAllBytes(item.MetadataPath));
                    AssertEqual(TelemetryUploadStatus.FAILED_RETRYABLE, pending.Status);
                    AssertTrue(pending.NextAttemptAtUtc > DateTimeOffset.UtcNow);
                    pending.NextAttemptAtUtc = DateTimeOffset.UnixEpoch;
                    File.WriteAllBytes(item.MetadataPath, TelemetryChunkSerializer.SerializeMetadata(pending));
                    handler.UploadReply = null;
                    var second = worker.ProcessDueAsync(CancellationToken.None).GetAwaiter().GetResult();
                    AssertEqual(1, second.Sent);
                    AssertEqual(2, handler.TelemetryCalls);
                    AssertTrue(original.SequenceEqual(File.ReadAllBytes(item.ChunkPath)));
                });
            }
        }

        private static void CreatePendingCompactTelemetryChunk(string telemetryRoot, CompactTelemetrySchemaId schemaId = CompactTelemetrySchemaId.RaceEventV1)
        {
            string directory = Path.Combine(telemetryRoot, "sessions", "fixture", "chunks", "compact", "story");
            Directory.CreateDirectory(directory);
            CompactTelemetrySchema schema = CompactTelemetrySchemaRegistry.Get(schemaId);
            var values = new double?[schema.Fields.Count];
            values[schema.Fields.First(value => value.Name == "eventTypeRef").Ordinal] = 0;
            if ((ushort)schemaId >= 0x0100) values[schema.Fields.First(value => value.Name == "lapDistanceMeters").Ordinal] = 20_815.41;
            var strings = new[]
            {
                new CompactStringDictionaryEntry(CompactStringDictionaryId.EventType, 0, "SESSION_START")
            };
            byte[] payload = CompactTelemetryCodec.Encode(new CompactTelemetryEnvelope(
                11,
                22,
                33,
                new CompactTelemetryBlock(
                    schemaId,
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
                CompactSchemaId = (ushort)schemaId,
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

        private static string CreatePendingTelemetryChunk(string telemetryRoot, DateTimeOffset? capturedAt = null)
        {
            TelemetryArchiveIdentity identity = TelemetryArchiveIdentityFactory.StartSession(
                "client-test-telemetry-session-fingerprint",
                "client-test-telemetry-witness");
            var archive = new LocalDurableTelemetryArchive(telemetryRoot, identity);
            try
            {
                AssertTrue(archive.TryCaptureSessionMetadata(new SessionMetadataSample
                {
                    CapturedAtUtc = capturedAt ?? DateTimeOffset.UtcNow,
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

        private sealed class UnreadableErrorContent : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => Task.FromException(new IOException("fixture error body disconnected"));
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
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
            public Func<HttpResponseMessage>? UploadReply { get; set; }
            public string UploadIdempotencyKey { get; private set; } = string.Empty;
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
            public string TelemetryRaceMode { get; private set; } = string.Empty;
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
                    TelemetryRaceMode = Header(request, "X-AMS2-Race-Mode");
                    TelemetryBody = request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult()
                        ?? Array.Empty<byte>();
                    string chunkId = TelemetryIdempotencyKey.StartsWith("telemetry:", StringComparison.Ordinal)
                        ? TelemetryIdempotencyKey.Substring("telemetry:".Length)
                        : string.Empty;
                    string json = "{\"status\":\"stored\",\"duplicate\":false,\"chunkId\":\""
                        + chunkId + "\",\"contentSha256\":\"" + TelemetryPayloadSha256 + "\"}";
                    if (UploadReply != null) return Task.FromResult(UploadReply());
                    return Task.FromResult(Response(HttpStatusCode.Created, json));
                }

                if (query.Contains(Cafe24ActivityUploadTransport.SessionWitnessEndpoint, StringComparison.Ordinal))
                {
                    UploadCalls++;
                    UploadIdempotencyKey = Header(request, "Idempotency-Key");
                    UploadAuthorization = request.Headers.Authorization?.ToString() ?? string.Empty;
                    UploadCompatibilityAuthorization = request.Headers.TryGetValues("X-AMS2-Authorization", out var values)
                        ? values.SingleOrDefault() ?? string.Empty
                        : string.Empty;
                    return Task.FromResult(UploadReply?.Invoke() ?? Response(HttpStatusCode.Created, "{\"status\":\"stored\",\"duplicate\":false}"));
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

        private static void TransitionTrackerReportsPositionDirection()
        {
            var tracker = new TimingTowerTransitionTracker();
            tracker.Observe(new[] { Row(1, "P1", "ALPHA", "0:20.000"), Row(2, "P2", "BRAVO", "0:21.000") });

            RankingRowViewModel bravo = Row(2, "P1", "BRAVO", "0:22.000");
            bravo.Status = "BEST";
            IReadOnlyList<TimingTowerTransition> transitions = tracker.Observe(new[] { bravo, Row(1, "P2", "ALPHA", "0:23.000") });
            TimingTowerTransition gained = transitions.Single(item => item.ParticipantIndex == 2);
            TimingTowerTransition lost = transitions.Single(item => item.ParticipantIndex == 1);
            AssertTrue(gained.IsReorder);
            AssertTrue(gained.PositionGained);
            AssertFalse(gained.PositionLost);
            AssertEqual(2, gained.PreviousPosition);
            AssertEqual(1, gained.Position);
            AssertTrue(gained.StatusChanged);
            AssertTrue(gained.BecameFastestLap);
            AssertTrue(lost.PositionLost);
            AssertFalse(lost.PositionGained);
            AssertFalse(lost.StatusChanged);
            AssertFalse(lost.BecameFastestLap);

            // A participant entering the visible window is new: no reorder, no gain/loss flash.
            transitions = tracker.Observe(new[] { bravo, Row(1, "P2", "ALPHA", "0:23.000"), Row(7, "P3", "CHARLIE", "0:24.000") });
            TimingTowerTransition entered = transitions.Single(item => item.ParticipantIndex == 7);
            AssertTrue(entered.IsNew);
            AssertFalse(entered.IsReorder);
            AssertFalse(entered.PositionGained);
            AssertFalse(entered.StatusChanged);
            AssertEqual(12, TimingTowerTransitionTracker.ParsePosition("P12"));
            AssertEqual(0, TimingTowerTransitionTracker.ParsePosition("P—"));
        }

        private static void PositionChangeFlashesRowAndRollsNumber()
        {
            var view = new OverlayHudView();
            view.SetViewModel(TimingRows(Row(1, "P1", "ALPHA", "0:20.000"), Row(2, "P2", "BRAVO", "0:21.000")));
            LayoutTower(view);

            view.SetViewModel(TimingRows(Row(2, "P1", "BRAVO", "0:22.000"), Row(1, "P2", "ALPHA", "0:23.000")));
            ItemsControl items = FindDescendant<ItemsControl>(view) ?? throw new InvalidOperationException("Ranking items missing.");
            ContentPresenter gained = Container(items, 0);
            ContentPresenter lost = Container(items, 1);
            Border gainedFlash = Named<Border>(gained, "FlashLayer");
            Border lostFlash = Named<Border>(lost, "FlashLayer");
            AssertColor(OverlayHudView.PositionGainFlashColor, gainedFlash.Background);
            AssertColor(OverlayHudView.PositionLossFlashColor, lostFlash.Background);
            // Animated values only update on the next time-manager tick, so the
            // tests assert clock attachment: the flash layer (and only the flash
            // layer) carries an opacity animation whose base value is fully hidden.
            AssertTrue(gainedFlash.HasAnimatedProperties);
            AssertTrue(lostFlash.HasAnimatedProperties);
            AssertEqual(0.0, gainedFlash.GetAnimationBaseValue(UIElement.OpacityProperty));
            TextBlock number = Named<TextBlock>(gained, "PositionText");
            AssertTrue(number.RenderTransform is TranslateTransform roll && roll.HasAnimatedProperties);
            AssertTrue(gained.RenderTransform is TranslateTransform slide && slide.HasAnimatedProperties);

            // The rows themselves never dim, whatever accent is playing.
            AssertEqual(1.0, gained.Opacity);
            AssertEqual(1.0, lost.Opacity);
            AssertFalse(gained.HasAnimatedProperties);
            AssertFalse(lost.HasAnimatedProperties);
            AssertFalse(DependencyPropertyHelper.GetValueSource(gained, UIElement.OpacityProperty).IsAnimated);
            AssertFalse(DependencyPropertyHelper.GetValueSource(lost, UIElement.OpacityProperty).IsAnimated);
        }

        private static void FastestLapStatusSweepsPurple()
        {
            var view = new OverlayHudView();
            view.SetViewModel(TimingRows(Row(1, "P1", "ALPHA", "0:20.000")));
            LayoutTower(view);

            RankingRowViewModel best = Row(1, "P1", "ALPHA", "0:20.050");
            best.Status = "BEST";
            view.SetViewModel(TimingRows(best));
            ItemsControl items = FindDescendant<ItemsControl>(view) ?? throw new InvalidOperationException("Ranking items missing.");
            ContentPresenter presenter = Container(items, 0);
            Border flash = Named<Border>(presenter, "FlashLayer");
            AssertColor(OverlayHudView.FastestLapFlashColor, flash.Background);
            AssertTrue(flash.RenderTransform is ScaleTransform sweep && sweep.HasAnimatedProperties);
            AssertTrue(flash.HasAnimatedProperties);
            AssertEqual(0.0, flash.GetAnimationBaseValue(UIElement.OpacityProperty));
            TextBlock status = Named<TextBlock>(presenter, "StatusText");
            AssertTrue(status.RenderTransform is ScaleTransform pop && pop.HasAnimatedProperties);
            AssertEqual(1.0, presenter.Opacity);
            AssertFalse(presenter.HasAnimatedProperties);
            AssertFalse(DependencyPropertyHelper.GetValueSource(presenter, UIElement.OpacityProperty).IsAnimated);
        }

        private static void TowerRowsBuildInWhenShown()
        {
            string root = Path.Combine(Path.GetTempPath(), "ams2-tower-entry-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = new OverlayWindow(false, Path.Combine(root, "overlay-layout.json"));
            try
            {
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                window.ShowDemoAt(-5000, -5000, 96);
                PumpDispatcher();
                ItemsControl items = FindDescendant<ItemsControl>(window) ?? throw new InvalidOperationException("Ranking items missing.");
                AssertTrue(items.Items.Count >= 2);
                ContentPresenter first = Container(items, 0);
                ContentPresenter last = Container(items, items.Items.Count - 1);
                AssertTrue(first.RenderTransform is TranslateTransform firstSlide && firstSlide.HasAnimatedProperties);
                AssertTrue(last.RenderTransform is TranslateTransform lastSlide && lastSlide.HasAnimatedProperties);
                AssertEqual(1.0, first.Opacity);
                AssertFalse(DependencyPropertyHelper.GetValueSource(first, UIElement.OpacityProperty).IsAnimated);
            }
            finally
            {
                window.Close();
                Directory.Delete(root, true);
            }
        }

        private static void ComponentToggleWithoutEditPersists()
        {
            string root = Path.Combine(Path.GetTempPath(), "ams2-toggle-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string layoutPath = Path.Combine(root, "overlay-layout.json");
            var window = new OverlayWindow(false, layoutPath);
            try
            {
                AssertFalse(window.IsLayoutEditing);
                window.SetComponentEnabled(OverlayComponentKeys.LapTiming, false);
                AssertFalse(window.IsComponentEnabled(OverlayComponentKeys.LapTiming));
                AssertTrue(File.Exists(layoutPath));
                AssertTrue(File.ReadAllText(layoutPath).Contains("\"lapTiming\": false", StringComparison.Ordinal));

                var reloaded = new OverlayWindow(false, layoutPath);
                try
                {
                    AssertFalse(reloaded.IsComponentEnabled(OverlayComponentKeys.LapTiming));
                    AssertTrue(reloaded.IsComponentEnabled(OverlayComponentKeys.TimingTower));
                    reloaded.SetComponentEnabled(OverlayComponentKeys.LapTiming, true);
                    AssertTrue(File.ReadAllText(layoutPath).Contains("\"lapTiming\": true", StringComparison.Ordinal));
                }
                finally
                {
                    reloaded.Close();
                }
            }
            finally
            {
                window.Close();
                Directory.Delete(root, true);
            }
        }

        private static void StatusWindowTogglesAlwaysEnabled()
        {
            var window = new ClientStatusWindow(new ClientStatusViewModel());
            try
            {
                var toggles = new List<LayoutComponentToggleEventArgs>();
                window.LayoutComponentToggled += (sender, args) => toggles.Add(args);
                AssertTrue(window.AreComponentTogglesEnabled);
                window.SetLayoutEditState(false, "레이아웃이 잠겼습니다.");
                AssertTrue(window.AreComponentTogglesEnabled);

                // Programmatic synchronisation never echoes back as a user toggle.
                window.SetLayoutComponentStates(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    [OverlayComponentKeys.LapTiming] = false
                });
                AssertEqual(0, toggles.Count);
                AssertFalse(window.GetLayoutComponentStates()[OverlayComponentKeys.LapTiming]);

                CheckBox lapTiming = LogicalDescendants<CheckBox>(window)
                    .Single(item => string.Equals(item.Tag as string, OverlayComponentKeys.LapTiming, StringComparison.OrdinalIgnoreCase));
                lapTiming.IsChecked = true;
                AssertEqual(1, toggles.Count);
                AssertEqual(OverlayComponentKeys.LapTiming, toggles[0].Component);
                AssertTrue(toggles[0].Enabled);

                window.SetAllComponents(false);
                AssertTrue(toggles.Count >= 1 + OverlayComponentKeys.All.Length);
                AssertTrue(OverlayComponentKeys.All.All(key => !window.GetLayoutComponentStates()[key]));
            }
            finally
            {
                window.Close();
            }
        }

        private static void RelativeParticipantChangeAnimates()
        {
            var view = new RelativeDriversView();
            OverlayViewModel first = DemoSnapshotFactory.CreateViewModel(false);
            view.SetViewModel(first);
            var size = new Size(OverlayUiMetrics.RelativeWidth, OverlayUiMetrics.RelativeHeight);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            OverlayViewModel changed = DemoSnapshotFactory.CreateViewModel(false);
            changed.AheadParticipantKey = first.AheadParticipantKey + "|swap";
            changed.AheadDistanceColor = "#57D5FF";
            view.SetViewModel(changed);
            Grid aheadRow = Named<Grid>(view, "AheadRow");
            StackPanel aheadDistance = Named<StackPanel>(view, "AheadDistancePanel");
            AssertTrue(aheadRow.RenderTransform is TranslateTransform slide && slide.HasAnimatedProperties);
            AssertTrue(aheadDistance.RenderTransform is ScaleTransform pop && pop.HasAnimatedProperties);
            AssertTrue(ReferenceEquals(view.DataContext, changed));
        }

        private static void SessionLapCounterRolls()
        {
            var view = new SessionInfoView();
            view.SetViewModel(new SessionInfoViewModel { LapValue = "3", PositionValue = "P5 / 20", PrimaryValue = "12:00" });
            var size = new Size(OverlayUiMetrics.SessionWidth, OverlayUiMetrics.SessionHeight);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            view.SetViewModel(new SessionInfoViewModel { LapValue = "4", PositionValue = "P4 / 20", PrimaryValue = "11:59" });
            TextBlock lap = Named<TextBlock>(view, "LapValueText");
            TextBlock position = Named<TextBlock>(view, "PositionValueText");
            TextBlock primary = Named<TextBlock>(view, "PrimaryValueText");
            AssertTrue(lap.RenderTransform is TranslateTransform lapRoll && lapRoll.HasAnimatedProperties);
            AssertTrue(position.RenderTransform is TranslateTransform positionRoll && positionRoll.HasAnimatedProperties);
            AssertFalse(DependencyPropertyHelper.GetValueSource(primary, UIElement.OpacityProperty).IsAnimated);
        }

        private static void EventCardExitKeepsSurfaceForAnimation()
        {
            var view = new EventCardView();
            EventCardViewModel shown = EventCardViewModel.FromEvent(DemoSnapshotFactory.CreateEvent(OverlayEventType.PositionGained), true);
            AssertEqual(TimeSpan.Zero, view.SetViewModel(shown, true));
            AssertEqual(EventCardView.ExitDuration, view.SetViewModel(new EventCardViewModel(), true));
            AssertTrue(ReferenceEquals(view.DataContext, shown));
            AssertEqual(TimeSpan.Zero, view.SetViewModel(new EventCardViewModel(), true));

            string root = Path.Combine(Path.GetTempPath(), "ams2-event-exit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = new OverlayWindow(false, Path.Combine(root, "overlay-layout.json"));
            try
            {
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false, OverlayEventType.PositionGained), false);
                window.ShowDemoAt(-5000, -5000, 96);
                AssertTrue(window.IsEventCardSurfaceVisible);

                window.SetViewModel(DemoSnapshotFactory.CreateShell(false), true);
                window.ShowDemoAt(-5000, -5000, 96);
                AssertTrue(window.IsEventCardSurfaceVisible);

                Thread.Sleep(EventCardView.ExitDuration + TimeSpan.FromMilliseconds(120));
                window.ShowDemoAt(-5000, -5000, 96);
                AssertFalse(window.IsEventCardSurfaceVisible);
            }
            finally
            {
                window.Close();
                Directory.Delete(root, true);
            }
        }

        private static void EventHighlightsFlashOnce()
        {
            foreach (OverlayEventType type in Enum.GetValues<OverlayEventType>())
                AssertEqual(type == OverlayEventType.RaceFastestLap || type == OverlayEventType.LeaderChange,
                    EventCardViewModel.FromEvent(DemoSnapshotFactory.CreateEvent(type), false).FlashOnEntry);

            foreach (OverlayEventType type in new[] { OverlayEventType.RaceFastestLap, OverlayEventType.LeaderChange })
            {
                var view = new EventCardView { Width = 520, Height = 84 };
                var item = new OverlayEvent(type, OverlayEventPriority.High, DateTimeOffset.UtcNow,
                    TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12),
                    type == OverlayEventType.RaceFastestLap ? "레이스 최고 랩" : "선두 변경",
                    "김드라이버", type == OverlayEventType.RaceFastestLap ? "0:55.749" : "P2 → P1", "FIXTURE");
                EventCardViewModel model = EventCardViewModel.FromEvent(item, true);
                view.SetViewModel(model, true);
                view.Measure(new Size(view.Width, view.Height));
                view.Arrange(new Rect(0, 0, view.Width, view.Height));
                PumpDispatcher(); // resolve the newly assigned DataContext brush binding
                Border sweep = Named<Border>(view, "EventSweep");
                AssertTrue(sweep.HasAnimatedProperties);
                AssertTrue(sweep.RenderTransform is ScaleTransform scale && scale.HasAnimatedProperties);
                AssertEqual(0.0, sweep.GetAnimationBaseValue(UIElement.OpacityProperty));
                AssertColor(model.Accent, sweep.Background);
                if (_layoutCaptureDirectory != null)
                {
                    // A connected, off-screen surface ticks WPF animation clocks without taking game focus.
                    var host = new Window { Content = view, SizeToContent = SizeToContent.WidthAndHeight,
                        Left = -5000, Top = -5000, ShowActivated = false, ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent };
                    try
                    {
                        host.Show();
                        PumpDispatcher();
                        view.SetViewModel(new EventCardViewModel(), false);
                        view.SetViewModel(model, true);
                        CaptureLayout(view, "event-flash-" + type, 500);
                        Console.WriteLine("PROOF " + type + " sweepOpacity=" + sweep.Opacity.ToString("0.000"));
                        AssertTrue(sweep.Opacity > 0 && sweep.Opacity <= 0.42);
                    }
                    finally { host.Content = null; host.Close(); }
                }

                // Detach the finished effect, then rebuild the same event VM as each UI tick does.
                sweep.BeginAnimation(UIElement.OpacityProperty, null);
                ((ScaleTransform)sweep.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, null);
                for (int tick = 0; tick < 120; tick++)
                    view.SetViewModel(EventCardViewModel.FromEvent(item, true), true);
                AssertFalse(sweep.HasAnimatedProperties);
                AssertFalse(((ScaleTransform)sweep.RenderTransform).HasAnimatedProperties);

                view.SetViewModel(EventCardViewModel.FromEvent(DemoSnapshotFactory.CreateEvent(type), false), true);
                AssertTrue(sweep.HasAnimatedProperties); // a genuinely new event flashes again
                view.SetViewModel(EventCardViewModel.FromEvent(DemoSnapshotFactory.CreateEvent(OverlayEventType.Battle), false), true);
                AssertFalse(sweep.HasAnimatedProperties); // preemption cannot inherit the previous flash
                AssertEqual(0.0, sweep.Opacity);

                model = EventCardViewModel.FromEvent(DemoSnapshotFactory.CreateEvent(type), false);
                view.SetViewModel(model, true);
                AssertEqual(EventCardView.ExitDuration, view.SetViewModel(new EventCardViewModel(), true));
                AssertFalse(sweep.HasAnimatedProperties);
                view.SetViewModel(model, false);
                AssertFalse(sweep.HasAnimatedProperties); // preview/static mode is motion-free
            }
        }

        private static void LapTimingBestLapPops()
        {
            var view = new LapTimingView();
            view.SetViewModel(DemoSnapshotFactory.CreateViewModel(false));
            var size = new Size(OverlayUiMetrics.LapTimingWidth, OverlayUiMetrics.LapTimingHeight);
            view.Measure(size);
            view.Arrange(new Rect(size));
            view.UpdateLayout();

            OverlayViewModel next = DemoSnapshotFactory.CreateViewModel(false);
            // The demo player is in sector 2: S1/S2 already show times, S3 is still "—".
            next.LastLapText = "1:40.111";
            next.BestLapText = "1:40.111";
            next.Sector3Text = "0:31.004";
            view.SetViewModel(next);
            AssertTrue(Named<TextBlock>(view, "LastLapValue").RenderTransform is ScaleTransform last && last.HasAnimatedProperties);
            AssertTrue(Named<TextBlock>(view, "BestLapValue").RenderTransform is ScaleTransform best && best.HasAnimatedProperties);
            AssertTrue(Named<TextBlock>(view, "Sector3Value").RenderTransform is ScaleTransform sector && sector.HasAnimatedProperties);
            AssertFalse(Named<TextBlock>(view, "Sector2Value").RenderTransform is ScaleTransform idle && idle.HasAnimatedProperties);
        }

        private static void ResizePreviewMatchesSavedTower()
        {
            string layout = Path.Combine(Path.GetTempPath(), "ams2-resize-" + Guid.NewGuid().ToString("N") + ".json");
            var window = new OverlayWindow(false, layout);
            try
            {
                var model = DemoSnapshotFactory.CreateShell(false);
                window.SetViewModel(model, false);
                window.ShowDemoAt(-5000, -5000, 96);
                PumpDispatcher();
                AssertTrue(window.BeginLayoutEdit());
                ItemsControl items = FindDescendant<ItemsControl>(window)!;
                foreach (int capacity in new[] { 10, 20, 8, 20 })
                {
                    // Keep native windows within small CI desktops; preserve the exact aspect ratio.
                    window.Width = OverlayUiMetrics.TowerWidth / 2.0;
                    window.Height = LeftTowerLayoutMetrics.RequiredHeightForRows(capacity, false) / 2.0;
                    PumpDispatcher();
                    // No SetViewModel/telemetry tick between these resizes.
                    AssertEqual(capacity, items.Items.Count);
                    AssertEqual(capacity, model.Timing.RankingRowCapacity);
                    AssertTrue(model.Timing.RankingRows.Any(row => row.IsPlayer));
                    if (capacity < 16) AssertEqual("P16", model.Timing.RankingRows.Last().Position);
                    ContentPresenter last = Container(items, capacity - 1);
                    AssertTrue(last.TranslatePoint(new Point(0, last.ActualHeight), window).Y <= window.ActualHeight + 0.5);
                }
                CaptureLayout((FrameworkElement)window.Content, "tower-preview");
                window.EndLayoutEdit(true);
                PumpDispatcher();
                AssertEqual(20, items.Items.Count);
                CaptureLayout((FrameworkElement)window.Content, "tower-applied");
                var reloaded = new OverlayWindow(false, layout);
                try
                {
                    reloaded.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                    reloaded.ShowDemoAt(-5000, -5000, 96);
                    PumpDispatcher();
                    AssertEqual(20, FindDescendant<ItemsControl>(reloaded)!.Items.Count);
                    AssertTrue(Math.Abs(window.ActualHeight - reloaded.ActualHeight) < 1);
                }
                finally { reloaded.Close(); }
            }
            finally
            {
                window.EndLayoutEdit(false);
                window.Close();
                if (File.Exists(layout)) File.Delete(layout);
            }
        }

        private static void AuxiliaryPanelsFillResizedBounds()
        {
            var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
            var window = new OverlayWindow(false, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            try
            {
                var shell = DemoSnapshotFactory.CreateShell(false, OverlayEventType.PositionGained);
                shell.RaceControl = new RaceControlViewModel
                {
                    IsVisible = true, IsExpanded = true, Title = "레이스 컨트롤",
                    Message = "랩타임 삭제", DriverLine = "P16 플레이어", StateLabel = "! 황색기"
                };
                window.SetViewModel(shell, false);
                Window[] panels = Application.Current.Windows.Cast<Window>()
                    .Where(item => item != window && !existing.Contains(item)).ToArray();
                AssertEqual(11, panels.Length);
                foreach (Window panel in panels)
                {
                    var root = (Grid)panel.Content;
                    var box = root.Children.OfType<Viewbox>().SingleOrDefault();
                    var content = box == null ? root.Children.OfType<RaceControlView>().Single() : (FrameworkElement)box.Child;
                    if (content is MultiplayerWaitingOverlayView)
                        content.DataContext = new MultiplayerWaitingOverlayViewModel
                        {
                            Title = "멀티플레이어 세션 대기", SessionLabel = "예선 결과 확정 및 다음 세션 준비",
                            ParticipantCountText = "리그 48 / 원본 49", RemainingLabel = "남은 시간", RemainingValue = "세션 종료 대기"
                        };
                    if (box != null) AssertEqual(Stretch.Uniform, box.Stretch);
                    double designWidth = box == null ? OverlayUiMetrics.RaceControlExpandedWidth : content.Width;
                    double designHeight = box == null ? OverlayUiMetrics.RaceControlExpandedHeight : content.Height;
                    foreach ((double x, double y) in new[] { (1.5, 0.8), (0.8, 1.6), (1.0, 1.0), (2.0, 2.0) })
                    {
                        var size = new Size(designWidth * x, designHeight * y);
                        root.Measure(size);
                        root.Arrange(new Rect(size));
                        root.UpdateLayout();
                        Rect bounds = content.TransformToAncestor(root).TransformBounds(new Rect(content.RenderSize));
                        AssertTrue(Math.Abs(bounds.Width - size.Width) < 1);
                        AssertTrue(Math.Abs(bounds.Height - size.Height) < 1);
                        AssertTrue(Math.Abs(bounds.X) < 1 && Math.Abs(bounds.Y) < 1);
                        int measuredTexts = 0;
                        foreach (TextBlock text in Descendants<TextBlock>(content).Where(item => item.Visibility == Visibility.Visible && item.ActualWidth > 0 && item.ActualHeight > 0))
                        {
                            measuredTexts++;
                            GeneralTransform transform = text.TransformToAncestor(root);
                            Point zero = transform.Transform(new Point());
                            double scaleX = (transform.Transform(new Point(1, 0)) - zero).Length;
                            double scaleY = (transform.Transform(new Point(0, 1)) - zero).Length;
                            AssertTrue(Math.Abs(scaleX - scaleY) < 0.0001);
                            if (box != null && !(content is DrivingNumberView) && text.Name != "AheadTimeGapText" && text.Name != "BehindTimeGapText"
                                && text.Name != "AheadLapGapText" && text.Name != "BehindLapGapText")
                                AssertTrue(Math.Abs(scaleX - Math.Min(x, y)) < 0.0001);
                        }
                        AssertTrue(measuredTexts > 0 || content is DrivingDashboardView || content is PedalTelemetryView
                            || Descendants<LegacyPedalTelemetryView>(content).Any() || Descendants<PedalTelemetryView>(content).Any());
                        CaptureLayout(root, panel.Title + "-" + x + "x" + y);
                    }
                }
            }
            finally { window.Close(); }
        }

        private static void OngoingFlagsDoNotReplayEntrance()
        {
            RaceControlViewModel State(int version) => RaceControlViewModel.FromUpdate(new RaceControlUpdate(
                Array.Empty<RaceControlEvent>(), null, Array.Empty<RaceControlEvent>(),
                new Dictionary<int, ParticipantBroadcastState>(), BroadcastOverlayState.Yellow, version, false));
            AssertEqual(State(1).EventId, State(120).EventId);
            var view = new RaceControlView { Width = 416, Height = 152 };
            RaceControlViewModel Yellow(string id, bool expanded = true) => new RaceControlViewModel
            {
                IsVisible = true, IsExpanded = expanded, EventId = id,
                Title = "레이스 컨트롤", Message = "! 황색기", StateLabel = "! 황색기", Accent = "#FFD166"
            };
            view.SetViewModel(Yellow("first"), false);
            view.Measure(new Size(view.Width, view.Height));
            view.Arrange(new Rect(0, 0, view.Width, view.Height));
            Border panel = Named<Border>(view, "Panel");
            for (int tick = 1; tick <= 120; tick++)
            {
                view.SetViewModel(Yellow("new-id-" + tick, tick % 2 == 0), true);
                AssertFalse(DependencyPropertyHelper.GetValueSource(panel, UIElement.OpacityProperty).IsAnimated);
                AssertFalse(((TranslateTransform)panel.RenderTransform).HasAnimatedProperties);
            }
            AssertEqual((double)OverlayUiMetrics.RaceControlExpandedHeight, view.Height);
            view.SetViewModel(Yellow("compact", false), true);
            // The host, not SetViewModel, owns the user's independent dimensions.
            view.Width = OverlayUiMetrics.RaceControlCompactWidth;
            view.Height = OverlayUiMetrics.RaceControlCompactHeight;
            AssertEqual((double)OverlayUiMetrics.RaceControlCompactHeight, view.Height);
            view.Measure(new Size(view.Width, view.Height));
            view.Arrange(new Rect(0, 0, view.Width, view.Height));
            CaptureLayout(view, "yellow-compact");
            var changed = Yellow("new-state", false);
            changed.StateLabel = "전 코스 황색기";
            view.SetViewModel(changed, true);
            AssertTrue(((ScaleTransform)Named<TextBlock>(view, "StateLabelText").RenderTransform).HasAnimatedProperties);
            AssertFalse(DependencyPropertyHelper.GetValueSource(panel, UIElement.OpacityProperty).IsAnimated);
            changed = Yellow("penalty");
            changed.Message = "랩타임 삭제";
            view.SetViewModel(changed, true);
            AssertTrue(((TranslateTransform)panel.RenderTransform).HasAnimatedProperties);
            AssertEqual(RaceControlView.ExitDuration, view.SetViewModel(new RaceControlViewModel(), true));
            view.SetViewModel(Yellow("returned"), true);
            AssertTrue(((TranslateTransform)panel.RenderTransform).HasAnimatedProperties);
        }

        private static void TimingTickOnlyNotifiesTime()
        {
            var row = Row(1, "P1", "ALPHA", "0:20.000");
            var notifications = new List<string?>();
            row.PropertyChanged += (sender, args) => notifications.Add(args.PropertyName);
            for (int tick = 1; tick <= 120; tick++)
                row.UpdateFrom(Row(1, "P1", "ALPHA", "0:20." + tick.ToString("000")));
            AssertEqual(120, notifications.Count);
            AssertTrue(notifications.All(name => name == nameof(RankingRowViewModel.CurrentTime)));
            row.UpdateFrom(Row(1, "P1", "ALPHA", "0:20.120"));
            AssertEqual(120, notifications.Count);
            row.UpdateFrom(Row(1, "P2", "ALPHA", "0:20.121"));
            AssertEqual(string.Empty, notifications.Last());
        }

        private static void BroadcastMotionRequestsHighRefresh()
        {
            AssertEqual((int?)144, Timeline.GetDesiredFrameRate(new DoubleAnimation()));
            AssertEqual((int?)144, Timeline.GetDesiredFrameRate(new DoubleAnimationUsingKeyFrames()));
        }

        private static TelemetrySnapshot LapClockFrame(double seconds, float a, float b,
            uint lapA = 0, uint lapB = 0, RaceState stateA = RaceState.Racing,
            GameState game = GameState.InGamePlaying, string nameA = "ALPHA", bool activeA = true)
        {
            return Parse(new RawFixtureBuilder(4).SetGameState(game).SetSequence(200 + (uint)(seconds * 1000) * 2).SetTrackTelemetry(1000, -1)
                .SetParticipant(0, activeA, nameA, 1, lapA, lapA + 1, stateA, PitMode.None)
                .SetParticipant(1, true, "BRAVO", 2, lapB, lapB + 1, RaceState.Racing, PitMode.None)
                .SetParticipantLapDistance(0, a).SetParticipantLapDistance(1, b), FixedTime().AddSeconds(seconds));
        }

        private static void ParticipantLapClocksStartIndependently()
        {
            var clock = new ParticipantLapClock();
            AssertEqual(0, clock.Observe(LapClockFrame(0, 990, 980)).Count);
            clock.Observe(LapClockFrame(0.1, 2, 985)); // counter can lag the distance reset
            clock.Observe(LapClockFrame(0.2, 8, 991, 1));
            clock.Observe(LapClockFrame(0.3, 14, 998, 1));
            clock.Observe(LapClockFrame(0.4, 20, 2, 1, 1));
            var times = clock.Observe(LapClockFrame(0.5, 25, 8, 1, 1));
            AssertTrue(Math.Abs(times[0] - 0.4f) < 0.001);
            AssertTrue(Math.Abs(times[1] - 0.1f) < 0.001);
            // Leader finishing never stops the other driver's lap clock.
            times = clock.Observe(LapClockFrame(0.6, 30, 12, 1, 1, RaceState.Finished));
            AssertFalse(times.ContainsKey(0));
            AssertTrue(Math.Abs(times[1] - 0.2f) < 0.001);
            // Rank is not an identity or timing input.
            var reordered = new RawFixtureBuilder(4).SetTrackTelemetry(1000, -1)
                .SetParticipant(1, true, "BRAVO", 1, 1, 2, RaceState.Racing, PitMode.None)
                .SetParticipantLapDistance(1, 18);
            times = clock.Observe(Parse(reordered, FixedTime().AddSeconds(0.7)));
            AssertTrue(Math.Abs(times[1] - 0.3f) < 0.001);
        }

        private static void ParticipantLapClocksRejectInvalidContinuity()
        {
            ParticipantLapClock Started()
            {
                var clock = new ParticipantLapClock();
                clock.Observe(LapClockFrame(0, 990, 980));
                clock.Observe(LapClockFrame(0.1, 2, 985, 1));
                clock.Observe(LapClockFrame(0.2, 8, 991, 1));
                return clock;
            }
            foreach (RaceState state in new[] { RaceState.Finished, RaceState.Retired, RaceState.Dnf, RaceState.Disqualified })
                AssertFalse(Started().Observe(LapClockFrame(0.3, 15, 995, 1, stateA: state)).ContainsKey(0));
            AssertFalse(Started().Observe(LapClockFrame(0.3, 15, 995, 1, nameA: "REPLACEMENT")).ContainsKey(0));
            AssertFalse(Started().Observe(LapClockFrame(0.3, 15, 995, 1, activeA: false)).ContainsKey(0));
            AssertFalse(Started().Observe(LapClockFrame(2, 15, 995, 1)).ContainsKey(0));
            AssertFalse(Started().Observe(LapClockFrame(0.3, 999, 995, 1)).ContainsKey(0)); // reverse/teleport
            AssertFalse(Started().Observe(LapClockFrame(0.3, 15, 995, 0)).ContainsKey(0)); // counter reset
            AssertFalse(Started().Observe(LapClockFrame(0.3, float.NaN, 995, 1)).ContainsKey(0));
            var stale = Started();
            var staleFixture = new RawFixtureBuilder(4).SetTrackTelemetry(1000, -1).SetSequence(600)
                .SetParticipant(0, true, "ALPHA", 1, 1, 2, RaceState.Racing, PitMode.None).SetParticipantLapDistance(0, 8);
            AssertTrue(Math.Abs(stale.Observe(Parse(staleFixture, FixedTime().AddSeconds(5)))[0] - 0.1f) < 0.001);
            var paused = Started();
            var before = paused.Observe(LapClockFrame(0.3, 8, 991, 1, game: GameState.InGamePaused));
            var after = paused.Observe(LapClockFrame(10, 8, 991, 1, game: GameState.InGamePaused));
            AssertEqual(before[0], after[0]);
            paused.Observe(LapClockFrame(10.1, 8, 991, 1));
            after = paused.Observe(LapClockFrame(10.2, 14, 997, 1));
            AssertTrue(Math.Abs(after[0] - before[0] - 0.1f) < 0.001);
            AssertEqual(0, paused.Observe(null).Count);
            AssertEqual(0, paused.Observe(LapClockFrame(11, 100, 200, 1)).Count); // mid-lap attach cannot infer a start

            var expired = new ParticipantLapClock();
            TelemetrySnapshot Timed(double seconds, float distance, float remaining, RaceState state = RaceState.Racing)
                => Parse(new RawFixtureBuilder(4).SetSequence(200 + (uint)(seconds * 1000) * 2)
                    .SetTrackTelemetry(1000, remaining).SetSessionTiming(1, 0, remaining)
                    .SetParticipant(0, true, "ALPHA", 1, 1, 2, state, PitMode.None)
                    .SetParticipantLapDistance(0, distance), FixedTime().AddSeconds(seconds));
            expired.Observe(Timed(0, 990, 0.2f));
            expired.Observe(Timed(0.1, 2, 0.1f));
            expired.Observe(Timed(0.2, 8, 0));
            AssertTrue(Math.Abs(expired.Observe(Timed(0.3, 14, 0))[0] - 0.2f) < 0.001);
            AssertFalse(expired.Observe(Timed(0.4, 20, 0, RaceState.Finished)).ContainsKey(0));
        }

        private static void TowerTimingFallsBackForMissingSectors()
        {
            var fixture = new RawFixtureBuilder(4).SetSession(SessionState.Race);
            for (int driver = 0; driver < 4; driver++)
                fixture.SetCurrentTiming(12, 12, -1, -1, participantIndex: driver)
                    .SetParticipantCurrentSector(driver, 1).SetParticipantLapTimes(driver, -1, -1); // required active S2 is absent
            fixture.SetParticipantLapTimes(0, 70.125f, 70.125f).SetParticipantLapTimes(1, 73.5f, 73.5f);
            TelemetrySnapshot snapshot = Parse(fixture);
            OverlayViewModel Build(IReadOnlyDictionary<int, float>? clocks = null) => OverlayViewModel.Build(
                snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE", participantLapTimes: clocks);
            var timing = Build();
            AssertEqual("1:10.125", timing.RankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("BEST", timing.RankingRows.Single(row => row.ParticipantIndex == 0).Status);
            AssertEqual("1:13.500", timing.RankingRows.Single(row => row.ParticipantIndex == 1).CurrentTime);
            AssertEqual(string.Empty, timing.RankingRows.Single(row => row.ParticipantIndex == 1).Status);
            AssertEqual("레이스 중", timing.RankingRows.Single(row => row.ParticipantIndex == 2).CurrentTime);
            AssertEqual("레이스 중", PlayerRow(timing).CurrentTime);
            AssertEqual("0:12.000", timing.CurrentLapText);
            timing = Build(new Dictionary<int, float> { [0] = 6.4f, [1] = 4.1f });
            AssertEqual("1:10.125", timing.RankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("1:13.500", timing.RankingRows.Single(row => row.ParticipantIndex == 1).CurrentTime);
        }

        private static void OpponentTimingUsesCurrentLapSectors()
        {
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Race, SessionState.Test, SessionState.TimeAttack })
            {
                var fixture = new RawFixtureBuilder(4).SetSession(session)
                    .SetParticipantLapTimes(0, 75.125f, 78).SetParticipantLapTimes(1, 80.5f, 82)
                    .SetParticipantLapTimes(2, 90.25f, 95).SetParticipantLapTimes(3, 70.75f, 74);
                string Time(int index) => BuildTiming(fixture).RankingRows.Single(row => row.ParticipantIndex == index).CurrentTime;
                foreach (float current in new[] { 0f, 10f, 999f, -1f, float.NaN })
                {
                    for (int index = 0; index < 4; index++) fixture.SetCurrentTiming(current, current, 35, 40, participantIndex: index);
                    AssertEqual("1:15.125", Time(0));
                    AssertEqual("1:20.500", Time(1));
                    AssertEqual("1:30.250", Time(2));
                    AssertEqual("1:10.750", Time(3));
                }
                foreach (float invalid in new[] { 0f, -1f, -123f, float.NaN, float.PositiveInfinity })
                {
                    fixture.SetParticipantLapTimes(0, invalid, 78).SetParticipantLapTimes(3, invalid, 74);
                    string missing = session == SessionState.Race ? "레이스 중"
                        : session == SessionState.TimeAttack ? "--" : "랩 타임 주행 중";
                    AssertEqual(missing, Time(0));
                    AssertEqual(missing, Time(3)); // never borrow root BestLapTime or a last lap
                }
                fixture.SetParticipantLapTimes(0, 69.125f, 69.125f);
                AssertEqual("1:09.125", Time(0));
                var view = BuildTiming(fixture);
                AssertEqual("BEST", view.RankingRows.Single(row => row.ParticipantIndex == 0).Status);
                var tower = new OverlayHudView();
                tower.SetViewModel(view);
                LayoutTower(tower);
                if (session == SessionState.Practice) CaptureLayout(tower, "participant-best-lap-tower");
            }
        }

        private static void InvalidLapDisplayFreezesPerParticipant()
        {
            var tracker = new InvalidLapDisplayTracker();
            RawFixtureBuilder Frame(float time, bool invalid, uint completed = 2, string name = "AI") => new RawFixtureBuilder(4)
                .SetParticipant(0, true, name, 1, completed, completed + 1, RaceState.Racing, PitMode.None)
                .SetParticipantCurrentSector(0, 0).SetCurrentTiming(time, time + 10, -1, -1, invalid, 0)
                .SetParticipant(3, true, "ME", 4, completed, completed + 1, RaceState.Racing, PitMode.None)
                .SetParticipantCurrentSector(3, 0).SetCurrentTiming(time, time, -1, -1, invalid)
                .SetParticipantLapTimes(0, 80.5f, 90).SetParticipantLapTimes(3, 70.125f, 80);
            OverlayViewModel Apply(RawFixtureBuilder fixture, int generation = 1)
            {
                TelemetrySnapshot snapshot = Parse(fixture);
                OverlayViewModel view = BuildTiming(fixture);
                tracker.Apply(view, snapshot, generation);
                AssertEqual(Parse(fixture).CurrentTime, snapshot.CurrentTime); // freeze never mutates source
                return view;
            }
            AssertEqual("1:10.125", PlayerRow(Apply(Frame(10, false))).CurrentTime);
            OverlayViewModel first = Apply(Frame(11, true));
            AssertEqual("1:10.125", PlayerRow(first).CurrentTime);
            OverlayViewModel later = Apply(Frame(44, true));
            AssertEqual("1:10.125", PlayerRow(later).CurrentTime);
            AssertEqual("0:11.000", later.CurrentLapText);
            AssertEqual("1:20.500", later.AllRankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("무효", PlayerRow(later).Status);
            AssertEqual("#E765F4", PlayerRow(later).TimeForeground);
            AssertEqual("#FF7777", PlayerRow(later).StatusColor);
            AssertFalse(PlayerRow(later).IsDimmed);
            AssertTrue(later.CurrentLabel.Contains("무효", StringComparison.Ordinal));
            var tower = new OverlayHudView();
            tower.SetViewModel(later);
            LayoutTower(tower);
            CaptureLayout(tower, "invalid-lap-tower");
            var panel = new LapTimingView();
            panel.SetViewModel(later);
            panel.Measure(new Size(panel.Width, panel.Height));
            panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
            CaptureLayout(panel, "invalid-lap-personal");

            AssertEqual("0:02.000", Apply(Frame(2, true, completed: 3)).CurrentLapText); // new lap never inherits frozen time
            AssertEqual("0:03.000", Apply(Frame(3, true, completed: 3), 2).CurrentLapText); // new session
            OverlayViewModel swapped = Apply(Frame(4, true, completed: 3, name: "NEW AI"), 2);
            AssertEqual("1:20.500", swapped.AllRankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("0:03.000", swapped.CurrentLapText);
            OverlayViewModel valid = Apply(Frame(5, false, completed: 3), 2);
            AssertEqual("0:05.000", valid.CurrentLapText);
            AssertTrue(PlayerRow(valid).Status != "무효");
            AssertEqual("아웃랩", PlayerRow(Apply(Frame(6, true, completed: 0).SetParticipantLapTimes(3, -1, -1))).CurrentTime);
            foreach ((RaceState state, string expected) in new[] { (RaceState.Finished, "FIN"), (RaceState.Dnf, "DNF"), (RaceState.Retired, "RET"), (RaceState.Disqualified, "DSQ") })
                AssertEqual(expected, PlayerRow(Apply(Frame(7, true).SetParticipant(3, true, "ME", 4, 1, 2, state, PitMode.None))).Status);
            OverlayViewModel missing = Apply(Frame(8, true).SetCurrentTiming(8, -1, -1, -1, true, 0));
            AssertEqual("1:20.500", missing.AllRankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            missing = Apply(Frame(8, true).SetParticipantLapTimes(0, -1, 90), 3);
            AssertEqual("레이스 중", missing.AllRankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("1:05.500", PlayerRow(Apply(Frame(9, true).SetParticipantLapTimes(3, 65.5f, 80), 3)).CurrentTime); // best update is never frozen
        }

        private static void InvalidLapEventIsVisibleOncePerLap()
        {
            var engine = new RaceEventEngine();
            DateTimeOffset start = FixedTime();
            RaceEventUpdate Observe(double seconds, bool invalid, uint completed = 1)
            {
                var fixture = new RawFixtureBuilder(4).SetParticipant(3, true, "ME", 4, completed, completed + 1, RaceState.Racing, PitMode.None)
                    .SetCurrentTiming(20, 20, -1, -1, invalid);
                TelemetrySnapshot snapshot = Parse(fixture, start.AddSeconds(seconds));
                return engine.Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt, BroadcastOverlayState.FullCourseYellow);
            }
            AssertEqual(0, Observe(0, false).DetectedEvents.Count);
            engine.Queue.Enqueue(new OverlayEvent(OverlayEventType.PodiumExit, OverlayEventPriority.Critical, start,
                TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(20), "순위 상승", "P4", "", "FIXTURE"), start);
            RaceEventUpdate invalid = Observe(0.1, true);
            AssertEqual(OverlayEventType.InvalidLap, invalid.CurrentEvent?.Type);
            AssertEqual(OverlayEventPriority.Critical, invalid.CurrentEvent?.Priority);
            AssertEqual(TimeSpan.FromSeconds(4), invalid.CurrentEvent?.DisplayDuration);
            AssertEqual("LOCAL_LAP_INVALIDATED", invalid.CurrentEvent?.SourceKind);
            AssertEqual("#FF7777", EventCardViewModel.FromEvent(invalid.CurrentEvent, false).Accent);
            AssertFalse(invalid.CurrentEvent!.Title.Contains("트랙", StringComparison.Ordinal));
            AssertFalse(Observe(1, true).DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
            Observe(2, false);
            AssertFalse(Observe(3, true).DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap)); // flag flicker, same lap
            AssertTrue(Observe(6, true, 2).DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
            engine.Reset();
            AssertTrue(Observe(7, true, 2).DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap)); // attached while already invalid
            foreach (OverlayEventType type in new[] { OverlayEventType.Finish, OverlayEventType.Retired, OverlayEventType.Disqualified })
            {
                var queue = new OverlayEventQueue();
                var terminal = new OverlayEvent(type, OverlayEventPriority.Critical, start, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), "종료", "", "", "FIXTURE");
                queue.Enqueue(invalid.CurrentEvent, start);
                queue.Enqueue(terminal, start);
                AssertEqual(type, queue.Current?.Type);
                queue.Enqueue(invalid.CurrentEvent, start);
                AssertEqual(type, queue.Current?.Type);
            }
        }

        private static void OpeningInvalidFlagsStayHidden()
        {
            foreach (SessionState session in new[] { SessionState.Race })
            foreach (bool rootOnly in new[] { false, true })
            {
                var engine = new RaceEventEngine();
                var tracker = new InvalidLapDisplayTracker();
                double seconds = 0;
                (OverlayViewModel View, RaceEventUpdate Events) Observe(uint completed, uint aiCompleted = 0,
                    RaceState state = RaceState.Racing, int generation = 1)
                {
                    var fixture = new RawFixtureBuilder(4).SetSession(session);
                    for (int driver = 0; driver < 4; driver++)
                    {
                        uint laps = driver == 3 ? completed : aiCompleted;
                        fixture.SetParticipant(driver, true, "DRIVER_" + driver, (uint)driver + 1, laps, laps + 1, state, PitMode.None)
                            .SetParticipantLapTimes(driver, -1, -1)
                            .SetCurrentTiming((float)seconds, (float)seconds, -1, -1, true, driver)
                            .SetParticipantCurrentSector(driver, 0);
                        if (rootOnly) fixture.Buffer[SharedMemoryLayout.LapsInvalidated + driver] = 0;
                    }
                    byte[] original = fixture.Buffer.ToArray();
                    TelemetrySnapshot snapshot = Parse(fixture, FixedTime().AddSeconds(seconds++));
                    OverlayViewModel view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE");
                    tracker.Apply(view, snapshot, generation);
                    RaceEventUpdate events = engine.Observe(snapshot, Classify(snapshot), generation, snapshot.CapturedAt);
                    AssertTrue(fixture.Buffer.SequenceEqual(original));
                    AssertTrue(snapshot.LapInvalidated);
                    AssertEqual(!rootOnly, snapshot.Participants[3].LapInvalidated);
                    return (view, events);
                }

                Observe(0, state: RaceState.NotStarted); // grid -> green retains AMS2's invalid flag
                foreach (int generation in new[] { 1, 1, 2 }) // duplicate and pause/resume must stay quiet too
                {
                    var opening = Observe(0, generation: generation);
                    AssertFalse(opening.Events.DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
                    AssertTrue(opening.View.AllRankingRows.All(row => row.Status != "무효" && row.CurrentTime == "아웃랩"));
                    AssertFalse(opening.View.CurrentLabel.Contains("무효", StringComparison.Ordinal));
                    AssertFalse(opening.View.CurrentLapStateText.Contains("무효", StringComparison.Ordinal));
                    AssertEqual(OverlayViewModel.FormatLapTime((float)seconds - 1), opening.View.CurrentLapText);
                    AssertEqual("#FFFFFF", opening.View.CurrentLapColor);
                }
                var nextLap = Observe(1, generation: 2);
                AssertEqual("무효", PlayerRow(nextLap.View).Status);
                AssertEqual(OverlayEventType.InvalidLap, nextLap.Events.CurrentEvent?.Type);
                AssertTrue(nextLap.View.AllRankingRows.Where(row => !row.IsPlayer).All(row => row.Status != "무효"));
                var aiNextLap = Observe(1, aiCompleted: 1, generation: 2);
                AssertEqual(!rootOnly, aiNextLap.View.AllRankingRows.First(row => !row.IsPlayer).Status == "무효");
                var restart = Observe(0, generation: 2); // lap counter rollback, not a new generation
                AssertFalse(restart.Events.DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
                AssertFalse(restart.Events.CurrentEvent?.Type == OverlayEventType.InvalidLap);
                AssertTrue(restart.View.AllRankingRows.All(row => row.Status != "무효"));
            }
            var timeAttack = new RawFixtureBuilder(4).SetSession(SessionState.TimeAttack)
                .SetParticipant(3, true, "ME", 4, 0, 1, RaceState.Racing, PitMode.None)
                .SetCurrentTiming(20, 20, -1, -1, true);
            OverlayViewModel view = BuildTiming(timeAttack);
            new InvalidLapDisplayTracker().Apply(view, Parse(timeAttack), 1);
            AssertEqual("무효", PlayerRow(view).Status); // Time Attack's first timed attempt is not an out lap
        }

        private static void OpeningLapLabelInTower()
        {
            var fixture = new RawFixtureBuilder(4).SetSession(SessionState.Race);
            for (int index = 0; index < 4; index++)
                fixture.SetParticipant(index, true, "DRIVER_" + index, (uint)index + 1, 0, 1, RaceState.Racing, PitMode.None)
                    .SetParticipantLapTimes(index, -1, -1)
                    .SetParticipantCurrentSector(index, 0).SetCurrentTiming(54.321f, 20 + index, -1, -1, participantIndex: index);
            TelemetrySnapshot initial = Parse(fixture);
            OverlayViewModel first = OverlayViewModel.Build(initial, ResolveLocal(initial), Classify(initial), 30, 20, false, "TEST",
                participantLapTimes: new Dictionary<int, float> { [0] = 12, [3] = 14 });
            AssertTrue(first.AllRankingRows.All(row => row.CurrentTime == "아웃랩"));
            AssertEqual("0:54.321", first.CurrentLapText); // personal panel is independent
            var tower = new OverlayHudView();
            tower.SetViewModel(first);
            LayoutTower(tower);
            CaptureLayout(tower, "opening-out-lap-tower");
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Test })
            {
                OverlayViewModel practice = BuildTiming(fixture.SetSession(session));
                AssertTrue(practice.AllRankingRows.All(row => row.CurrentTime == "랩 타임 주행 중")); // no pit-exit history; lap zero alone is not an out lap
            }
            fixture.SetSession(SessionState.Race).SetParticipant(0, true, "DRIVER_0", 1, 1, 2, RaceState.Racing, PitMode.None)
                .SetParticipantLapTimes(0, 60.125f, 61)
                .SetParticipantCurrentSector(0, 0).SetCurrentTiming(54.321f, 1.234f, -1, -1, participantIndex: 0);
            OverlayViewModel leaderLapTwo = BuildTiming(fixture);
            AssertEqual("레이스 중", leaderLapTwo.AllRankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertTrue(leaderLapTwo.AllRankingRows.All(row => row.Status != "BEST"));
            AssertTrue(leaderLapTwo.AllRankingRows.Where(row => row.ParticipantIndex != 0).All(row => row.CurrentTime == "아웃랩"));
            fixture.SetParticipant(3, true, "DRIVER_3", 4, 1, 2, RaceState.Racing, PitMode.None)
                .SetParticipantLapTimes(3, 65.5f, 66)
                .SetCurrentTiming(2.345f, 2.345f, -1, -1).SetParticipantCurrentSector(3, 0);
            AssertEqual("레이스 중", PlayerRow(BuildTiming(fixture)).CurrentTime);
            fixture.SetParticipant(0, true, "DRIVER_0", 1, 2, 3, RaceState.Racing, PitMode.None).SetParticipantLapTimes(0, 60.125f, 61);
            AssertEqual("1:00.125", BuildTiming(fixture).AllRankingRows.Single(row => row.ParticipantIndex == 0).CurrentTime);
            AssertEqual("레이스 중", PlayerRow(BuildTiming(fixture)).CurrentTime); // leader crossing cannot unlock trailing driver
            fixture.SetParticipant(3, true, "DRIVER_3", 4, 2, 3, RaceState.Racing, PitMode.None).SetParticipantLapTimes(3, 65.5f, 66);
            AssertEqual("1:05.500", PlayerRow(BuildTiming(fixture)).CurrentTime);
            foreach ((RaceState state, string text) in new[] { (RaceState.Finished, "FIN"), (RaceState.Retired, "RET"), (RaceState.Dnf, "DNF"), (RaceState.Disqualified, "DSQ") })
                AssertEqual(text, PlayerRow(BuildTiming(fixture.SetParticipant(3, true, "DRIVER_3", 4, 0, 1, state, PitMode.None))).Status);
            fixture.SetParticipant(3, true, "DRIVER_3", 4, 0, 2, RaceState.Racing, PitMode.None).SetCurrentTiming(1, 1, -1, -1, true);
            OverlayViewModel counterTransition = BuildTiming(fixture);
            new InvalidLapDisplayTracker().Apply(counterTransition, Parse(fixture), 1);
            AssertEqual("아웃랩", PlayerRow(counterTransition).CurrentTime); // raw currentLap can advance before completed counter
            AssertTrue(PlayerRow(counterTransition).Status != "무효");
        }

        private static void PracticeLapPhaseFollowsPitExit()
        {
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Test })
            foreach (uint initialLaps in new uint[] { 0, 5 })
            {
                var tracker = new InvalidLapDisplayTracker();
                var events = new RaceEventEngine();
                int tick = 0;
                (OverlayViewModel View, RaceEventUpdate Events) Frame(PitMode pit, uint laps, float distance, float best = -1, bool invalid = true)
                {
                    var fixture = new RawFixtureBuilder(4).SetSession(session).SetTrackTelemetry(3000, 600);
                    foreach (int index in new[] { 0, 3 })
                        fixture.SetParticipant(index, true, "DRIVER_" + index, (uint)index + 1, laps, laps + 1, RaceState.Racing, pit)
                            .SetParticipantLapDistance(index, distance).SetParticipantLapTimes(index, best, -1)
                            .SetCurrentTiming(10 + tick, 10 + tick, -1, -1, invalid, index);
                    byte[] original = fixture.Buffer.ToArray();
                    var snapshot = Parse(fixture, FixedTime().AddMilliseconds(tick++ * 100));
                    tracker.Observe(snapshot, 1);
                    var view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE",
                        outLapParticipants: tracker.OutLapParticipants);
                    tracker.Apply(view, snapshot, 1);
                    var update = events.Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt,
                        outLapParticipants: tracker.OutLapParticipants);
                    AssertTrue(original.SequenceEqual(fixture.Buffer));
                    return (view, update);
                }
                AssertEqual("--", PlayerRow(Frame(PitMode.InGarage, initialLaps, 100).View).CurrentTime);
                foreach ((PitMode pit, float distance) in new[] { (PitMode.DrivingOutOfGarage, 120f), (PitMode.DrivingOutOfPits, 200f),
                    (PitMode.None, 900f), (PitMode.None, 2990f) })
                {
                    var outLap = Frame(pit, initialLaps, distance);
                    foreach (var row in outLap.View.AllRankingRows.Where(row => row.ParticipantIndex == 0 || row.IsPlayer))
                    { AssertEqual("아웃랩", row.CurrentTime); AssertTrue(row.Status != "무효"); }
                    AssertFalse(outLap.View.CurrentLapStateText.Contains("무효", StringComparison.Ordinal));
                    AssertFalse(outLap.Events.DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
                }
                // Distance wraps before the counter update: the first timed lap must start now.
                var timed = Frame(PitMode.None, initialLaps, 10, invalid: false);
                AssertEqual("랩 타임 주행 중", PlayerRow(timed.View).CurrentTime);
                var tower = new OverlayHudView();
                tower.SetViewModel(timed.View); LayoutTower(tower);
                var label = Descendants<TextBlock>(tower).First(text => text.Text == "랩 타임\n주행 중");
                AssertEqual(OverlayUiMetrics.FontSmall, label.FontSize);
                Rect labelBounds = label.TransformToAncestor(tower).TransformBounds(new Rect(label.RenderSize));
                AssertTrue(labelBounds.Height <= OverlayUiMetrics.RowPitch - 2 + .5); // actual Viewbox-scaled glyphs, excluding row margins
                CaptureLayout(tower, "lap-phase-" + session + "-stint" + initialLaps);
                AssertTrue(Frame(PitMode.None, initialLaps + 1, 30).Events.DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
                var recorded = Frame(PitMode.None, initialLaps + 2, 40, 65.125f, false);
                AssertEqual("1:05.125", PlayerRow(recorded.View).CurrentTime);
                AssertEqual("1:05.125", PlayerRow(Frame(PitMode.InPit, initialLaps + 2, 600, 65.125f).View).CurrentTime);
                Frame(PitMode.DrivingOutOfPits, initialLaps + 2, 700, 65.125f);
                var newStint = Frame(PitMode.None, initialLaps + 2, 800, 65.125f);
                AssertEqual("1:05.125", PlayerRow(newStint.View).CurrentTime); // user's best-first choice
                AssertTrue(PlayerRow(newStint.View).Status != "무효");
                AssertFalse(newStint.Events.DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
                Frame(PitMode.None, initialLaps + 3, 25, 65.125f, false); // trusted completed-lap transition, no usable wrap
                AssertEqual(0, tracker.OutLapParticipants.Count);
            }
        }

        private static void NativeLapStartClearsOutLapImmediately()
        {
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Test })
            foreach (string signal in new[] { "currentLap", "sector", "completed", "wrap" })
            foreach (PitMode exitMode in new[] { PitMode.None, PitMode.DrivingOutOfPits })
            {
                var tracker = new InvalidLapDisplayTracker();
                int tick = 0;
                OverlayViewModel Frame(PitMode pit, uint currentLap, uint completed, int sector, float distance, float sector1)
                {
                    var fixture = new RawFixtureBuilder(4).SetSession(session).SetTrackTelemetry(3000, 600)
                        .SetParticipant(3, true, "ME", 4, completed, currentLap, RaceState.Racing, pit)
                        .SetParticipantLapTimes(3, -1, -1).SetParticipantLapDistance(3, distance)
                        .SetParticipantCurrentSector(3, sector).SetCurrentTiming(50, sector1, 20, 10);
                    var snapshot = Parse(fixture, FixedTime().AddMilliseconds(tick++ * 100));
                    tracker.Observe(snapshot, 1);
                    var view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE",
                        outLapParticipants: tracker.OutLapParticipants);
                    tracker.Apply(view, snapshot, 1);
                    return view;
                }
                Frame(PitMode.InGarage, 7, 6, 2, 1000, 20);
                Frame(PitMode.DrivingOutOfPits, 7, 6, 2, 1100, 20);
                AssertEqual("아웃랩", PlayerRow(Frame(exitMode, 7, 6, 2, signal == "wrap" ? 2990 : 1200, 20)).CurrentTime);
                uint current = signal == "currentLap" ? 8u : 7u;
                uint completed = signal == "completed" ? 7u : 6u;
                int sector = signal == "sector" ? 0 : 2;
                float distance = signal == "wrap" ? 5 : 1205;
                float sector1 = signal == "sector" ? .05f : 20;
                AssertEqual("랩 타임 주행 중", PlayerRow(Frame(exitMode, current, completed, sector, distance, sector1)).CurrentTime);
                AssertEqual("랩 타임 주행 중", PlayerRow(Frame(exitMode, current, completed, sector, distance + 5, sector1)).CurrentTime);
                AssertEqual("랩 타임 주행 중", PlayerRow(Frame(PitMode.None, current, completed, sector, distance + 10, sector1)).CurrentTime);
            }
        }

        private static void PitExitDoesNotRearmTimedLap()
        {
            var tracker = new InvalidLapDisplayTracker();
            int tick = 0;
            bool Frame(PitMode pit, uint current = 1, uint completed = 0, int sector = 2, float s1 = 20, float distance = 2000)
            {
                var snapshot = Parse(new RawFixtureBuilder(4).SetSession(SessionState.Qualify).SetTrackTelemetry(3000, 600)
                    .SetParticipant(3, true, "ME", 4, completed, current, RaceState.Racing, pit)
                    .SetParticipantCurrentSector(3, sector).SetCurrentTiming(60, s1, 20, 20)
                    .SetParticipantLapDistance(3, distance), FixedTime().AddMilliseconds(tick++ * 100));
                tracker.Observe(snapshot, 1);
                return tracker.OutLapParticipants.Contains(3);
            }
            Frame(PitMode.InGarage, current: 0);
            AssertTrue(Frame(PitMode.DrivingOutOfGarage)); // 0 -> 1 is initialization, not a timed lap
            AssertTrue(Frame(PitMode.DrivingOutOfPits));
            AssertTrue(Frame(PitMode.None, sector: 1));
            AssertTrue(Frame(PitMode.None, sector: 0, s1: 20)); // neither a final-sector wrap nor a reset sector timer
            AssertTrue(Frame(PitMode.None, sector: 2));
            AssertTrue(Frame(PitMode.None, sector: 0, s1: -1)); // missing timing is not evidence
            AssertTrue(Frame(PitMode.None, distance: 2990));
            AssertTrue(Frame(PitMode.None, distance: 500)); // implausible teleport cannot start timing
            AssertFalse(Frame(PitMode.None, current: 2));
            AssertFalse(Frame(PitMode.DrivingOutOfPits, current: 2)); // late pit status after line; no new pit visit
            AssertFalse(Frame(PitMode.None, current: 2));
            Frame(PitMode.InPit, current: 2);
            AssertTrue(Frame(PitMode.DrivingOutOfPits, current: 2)); // genuine new visit still starts a fresh out lap
        }

        private static void FirstTimedLapUsesNativeTimingAvailability()
        {
            foreach (SessionState session in new[] { SessionState.Practice, SessionState.Qualify, SessionState.Test })
            foreach (RaceState outState in new[] { RaceState.NotStarted, RaceState.Racing })
            {
                var tracker = new InvalidLapDisplayTracker();
                var events = new RaceEventEngine();
                int tick = 0;
                OverlayViewModel Frame(PitMode pit, RaceState race, float s1, float distance, uint completed = 0, float best = -123, bool invalid = true)
                {
                    var fixture = new RawFixtureBuilder(4).SetSession(session).SetTrackTelemetry(3999.7273f, 600)
                        .SetParticipant(3, true, "ME", 4, completed, completed + 1, race, pit)
                        .SetParticipantLapTimes(3, best, -123).SetParticipantLapDistance(3, distance)
                        .SetParticipantCurrentSector(3, s1 < 0 ? 2 : 0).SetCurrentTiming(60, s1, -1, -1, invalid);
                    var snapshot = Parse(fixture, FixedTime().AddMilliseconds(tick++ * 60));
                    tracker.Observe(snapshot, 1);
                    var view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE",
                        outLapParticipants: tracker.OutLapParticipants);
                    tracker.Apply(view, snapshot, 1);
                    var update = events.Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt,
                        outLapParticipants: tracker.OutLapParticipants);
                    if (completed == 0) AssertFalse(update.DetectedEvents.Any(item => item.Type == OverlayEventType.InvalidLap));
                    return view;
                }
                AssertEqual("아웃랩", PlayerRow(Frame(PitMode.DrivingOutOfPits, RaceState.Racing, -1, 100)).CurrentTime);
                // Observed 2026-09-06 16:08:14 KST: 3999.8115 exceeds reported 3999.7273.
                // First timing starts at S1 -1 -> .07910156 while lap=1 / completed=0 stay unchanged.
                AssertEqual("아웃랩", PlayerRow(Frame(PitMode.None, outState, -1, 3999.8115f)).CurrentTime);
                AssertEqual("랩 타임 주행 중", PlayerRow(Frame(PitMode.None, RaceState.Racing, .07910156f, 4.410772f, invalid: false)).CurrentTime);
                AssertEqual("랩 타임 주행 중", PlayerRow(Frame(PitMode.None, RaceState.Racing, 15, 1100, invalid: false)).CurrentTime);
                AssertEqual("랩 타임 주행 중", PlayerRow(Frame(PitMode.None, RaceState.Racing, .05f, 4, 1)).CurrentTime); // invalid finish has no accepted best
                AssertEqual("0:55.235", PlayerRow(Frame(PitMode.None, RaceState.Racing, .05f, 4, 2, 55.235046f, false)).CurrentTime);
            }
        }

        private static void OutLapSurvivesPauseAndMenu()
        {
            var tracker = new InvalidLapDisplayTracker();
            var session = new SessionStateTracker();
            int tick = 0;
            void Frame(GameState game, PitMode pit, float s1 = -1)
            {
                var snapshot = Parse(new RawFixtureBuilder(4).SetSession(SessionState.Practice).SetGameState(game)
                    .SetParticipant(3, true, "ME", 4, 5, 6, RaceState.Racing, pit)
                    .SetParticipantLapTimes(3, -123, -123).SetParticipantCurrentSector(3, 0)
                    .SetCurrentTiming(50, s1, -1, -1), FixedTime().AddMilliseconds(tick++ * 100));
                session.Observe(snapshot);
                tracker.Observe(snapshot, session.Generation);
            }
            Frame(GameState.InGamePlaying, PitMode.InGarage);
            Frame(GameState.InGamePlaying, PitMode.DrivingOutOfPits);
            Frame(GameState.InGamePlaying, PitMode.None);
            AssertTrue(tracker.OutLapParticipants.Contains(3));
            foreach (GameState game in new[] { GameState.InGamePaused, GameState.InGamePlaying, GameState.InGameMenuTimeTicking, GameState.InGamePlaying })
            { Frame(game, PitMode.None); AssertTrue(tracker.OutLapParticipants.Contains(3)); }
            AssertEqual(4, session.Generation);
            Frame(GameState.InGamePlaying, PitMode.None, .0357f);
            AssertFalse(tracker.OutLapParticipants.Contains(3));
        }

        private static void LateAttachRecognizesUntimedOutLap()
        {
            foreach (RaceState race in new[] { RaceState.NotStarted, RaceState.Racing })
            {
                var fixture = new RawFixtureBuilder(4).SetSession(SessionState.Practice)
                    .SetParticipant(3, true, "ME", 4, 0, 1, race, PitMode.None)
                    .SetParticipantLapTimes(3, -123, -123).SetCurrentTiming(999, -1, -1, -1, true);
                var snapshot = Parse(fixture);
                var tracker = new InvalidLapDisplayTracker(); tracker.Observe(snapshot, 1);
                var view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE",
                    outLapParticipants: tracker.OutLapParticipants);
                tracker.Apply(view, snapshot, 1);
                AssertEqual("아웃랩", PlayerRow(view).CurrentTime);
                AssertTrue(PlayerRow(view).Status != "무효");
                AssertEqual("아웃랩", PlayerRow(BuildTiming(fixture)).CurrentTime); // same rule in snapshot-only diagnostics
                fixture.SetParticipant(3, true, "ME", 4, 0, 1, RaceState.Racing, PitMode.None)
                    .SetParticipantLapTimes(3, -123, -123).SetCurrentTiming(10, 10, -1, -1, false);
                AssertEqual("랩 타임 주행 중", PlayerRow(BuildTiming(fixture)).CurrentTime);
            }
            var stages = new RawFixtureBuilder(4).SetSession(SessionState.Practice);
            for (int index = 0; index < 4; index++)
                stages.SetParticipant(index, true, new[] { "차고 대기", "출차 후 주행", "첫 계측 주행", "인정 기록" }[index],
                    (uint)index + 1, index == 3 ? 1u : 0u, index == 3 ? 2u : 1u,
                    index == 1 ? RaceState.NotStarted : RaceState.Racing, index == 0 ? PitMode.InGarage : PitMode.None)
                    .SetParticipantLapTimes(index, index == 3 ? 55.235046f : -123, -123)
                    .SetParticipantCurrentSector(index, 0).SetCurrentTiming(50, index < 2 ? -1 : .079f, -1, -1, index < 2, index);
            var sample = Parse(stages);
            var display = new InvalidLapDisplayTracker(); display.Observe(sample, 1);
            var control = new RaceControlAnalyzer(EvidenceKind.Fixture).Observe(sample, Classify(sample), 1, sample.CapturedAt);
            var model = OverlayViewModel.Build(sample, ResolveLocal(sample), Classify(sample), 30, 20, false, "FIXTURE",
                raceControl: control, outLapParticipants: display.OutLapParticipants);
            display.Apply(model, sample, 1);
            AssertTrue(model.AllRankingRows.Select(row => row.CurrentTime).SequenceEqual(new[] { "--", "아웃랩", "랩 타임 주행 중", "0:55.235" }));
            AssertEqual("PIT", model.AllRankingRows[0].Status);
            var tower = new OverlayHudView(); tower.SetViewModel(model); LayoutTower(tower); CaptureLayout(tower, "lap-lifecycle-four-stages");
        }

        private static void LapPhaseRejectsStaleState()
        {
            var tracker = new InvalidLapDisplayTracker();
            int tick = 0;
            TelemetrySnapshot Observe(PitMode pit, uint completed = 5, string name = "ME", SessionState session = SessionState.Practice,
                int generation = 1, GameState game = GameState.InGamePlaying, RaceState race = RaceState.Racing)
            {
                var snapshot = Parse(new RawFixtureBuilder(4).SetSession(session).SetGameState(game)
                    .SetParticipant(3, true, name, 4, completed, completed + 1, race, pit), FixedTime().AddMilliseconds(tick++ * 100));
                tracker.Observe(snapshot, generation);
                return snapshot;
            }
            void Arm() { Observe(PitMode.InGarage); Observe(PitMode.None); AssertTrue(tracker.OutLapParticipants.Contains(3)); }
            Arm(); Observe(PitMode.None, name: "REPLACEMENT"); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); Observe(PitMode.None, completed: 0); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); Observe(PitMode.None, session: SessionState.Qualify); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); Observe(PitMode.None, generation: 2); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); Observe(PitMode.None, game: GameState.InGameReplay); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); Observe(PitMode.None, race: RaceState.Retired); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); tracker.Observe(null, 1); Observe(PitMode.None); AssertFalse(tracker.OutLapParticipants.Contains(3));
            Arm(); var duplicate = Observe(PitMode.None); tracker.Observe(duplicate, 1); AssertTrue(tracker.OutLapParticipants.Contains(3));
        }

        private static void AllPitModesShowPitBadge()
        {
            foreach (PitMode pit in new[] { PitMode.None, PitMode.DrivingIntoPits, PitMode.InPit, PitMode.DrivingOutOfPits, PitMode.InGarage, PitMode.DrivingOutOfGarage, (PitMode)999 })
            {
                var fixture = new RawFixtureBuilder(29).SetSession(SessionState.Practice);
                for (int index = 0; index < 29; index++)
                    fixture.SetParticipant(index, true, "PIT_DRIVER_" + index, (uint)index + 1, 0, 1, RaceState.Racing, pit)
                        .SetParticipantLapTimes(index, -1, -1);
                TelemetrySnapshot snapshot = Parse(fixture);
                var control = new RaceControlAnalyzer(EvidenceKind.Fixture).Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt);
                var view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE", raceControl: control);
                bool isPit = pit != PitMode.None && pit != (PitMode)999;
                AssertTrue(control.ParticipantStates.Values.All(state => state.IsPitActive == isPit));
                AssertEqual(isPit ? 29 : 0, view.AllRankingRows.Count(row => row.Status == "PIT"));
                AssertEqual(isPit, control.OverlayState.HasFlag(BroadcastOverlayState.PlayerPit));
                AssertTrue(view.AllRankingRows.All(row => !row.IsDimmed));
                if (pit == PitMode.InGarage)
                {
                    var tower = new OverlayHudView(); tower.SetViewModel(view); LayoutTower(tower); CaptureLayout(tower, "garage-pit-badges");
                }
            }
            foreach ((RaceState race, PitSchedule schedule, string badge) in new[] {
                (RaceState.Retired, PitSchedule.None, "RET"), (RaceState.Dnf, PitSchedule.None, "DNF"),
                (RaceState.Disqualified, PitSchedule.None, "DSQ"), (RaceState.Racing, PitSchedule.DriveThrough, "PIT"),
                (RaceState.Racing, PitSchedule.StopGo, "PIT") })
            {
                var snapshot = Parse(new RawFixtureBuilder(4).SetSession(SessionState.Practice)
                    .SetParticipant(0, true, "PIT", 1, 0, 1, race, PitMode.InGarage).SetParticipantControl(0, schedule));
                var control = new RaceControlAnalyzer(EvidenceKind.Fixture).Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt);
                var view = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot), 30, 20, false, "FIXTURE", raceControl: control);
                AssertEqual(badge, view.AllRankingRows.Single(row => row.ParticipantIndex == 0).Status);
            }
        }

        private static void RaceControlShowsCurrentGreenWithoutHistory()
        {
            var analyzer = new RaceControlAnalyzer(EvidenceKind.Fixture);
            DateTimeOffset now = FixedTime();
            ObserveControl(analyzer, new RawFixtureBuilder(), now);
            ObserveControl(analyzer, new RawFixtureBuilder().SetRootControl(FlagColour.Yellow), now.AddSeconds(1));
            RaceControlUpdate update = ObserveControl(analyzer, new RawFixtureBuilder().SetRootControl(FlagColour.Green), now.AddSeconds(8));
            AssertTrue(update.History.Count >= 2);
            AssertEqual(RaceControlEventType.Green, update.ActiveEvent?.Type);
            foreach ((int width, int height) in new[] { (416, 152), (240, 120), (600, 55) })
            {
                var view = new RaceControlView { Width = width, Height = height };
                view.SetViewModel(RaceControlViewModel.FromUpdate(update), false);
                view.Measure(new Size(width, height));
                view.Arrange(new Rect(0, 0, width, height));
                PumpDispatcher();
                view.UpdateLayout();
                AssertFalse(Descendants<TextBlock>(view).Any(item => item.Name == "HistoryText" || item.Name == "CountText"));
                AssertEqual(Visibility.Collapsed, Named<TextBlock>(view, "DriverText").Visibility);
                AssertEqual(Visibility.Collapsed, Named<TextBlock>(view, "StateLabelText").Visibility);
                AssertEqual(update.ActiveEvent!.Title, Named<TextBlock>(view, "TitleText").Text);
                AssertEqual(update.ActiveEvent.Message, Named<TextBlock>(view, "MessageText").Text);
                if (width == 416) AssertEqual(24.0, Named<TextBlock>(view, "MessageText").FontSize);
                CaptureLayout(view, "green-current-only-" + width + "x" + height);
            }
            AssertTrue(update.History.Any(item => item.Type == RaceControlEventType.Yellow)); // raw history was not removed
        }

        private static void RaceControlReflowsWithoutClipping()
        {
            foreach (bool expanded in new[] { false, true })
            foreach ((int width, int height) in new[] { (72, 48), (288, 66), (160, 260), (600, 55), (416, 152), (832, 304), (240, 120) })
            {
                var view = new RaceControlView { Width = width, Height = height };
                view.SetViewModel(new RaceControlViewModel
                {
                    IsVisible = true, IsExpanded = expanded, EventId = "fixture",
                    Title = expanded ? "레이스 컨트롤 — 랩타임 삭제" : "레이스 컨트롤",
                    DriverLine = "P16 ENG-IceBlasT 긴 플레이어 이름",
                    Message = "레이스 관리자가 트랙 한계 위반으로 해당 참가자의 랩타임을 삭제했습니다.",
                    StateLabel = "!! 이중 황색기"
                }, false);
                var size = new Size(width, height);
                view.Measure(size);
                view.Arrange(new Rect(size));
                PumpDispatcher();
                view.UpdateLayout();
                AssertEqual((double)width, view.Width);
                AssertEqual((double)height, view.Height);
                Grid body = Named<Grid>(view, "Body");
                GeneralTransform transform = body.TransformToAncestor(view);
                Point zero = transform.Transform(new Point());
                Point unitX = transform.Transform(new Point(1, 0));
                Point unitY = transform.Transform(new Point(0, 1));
                AssertTrue(Math.Abs((unitX.X - zero.X) - (unitY.Y - zero.Y)) < 0.001);
                Rect bodyBounds = transform.TransformBounds(new Rect(body.RenderSize));
                AssertTrue(bodyBounds.Left >= 0 && bodyBounds.Top >= 0);
                AssertTrue(bodyBounds.Right <= width + 0.5 && bodyBounds.Bottom <= height + 0.5);
                foreach (TextBlock text in Descendants<TextBlock>(view))
                {
                    if (text.ActualHeight == 0) continue;
                    bool hidden = false;
                    for (DependencyObject? parent = text; parent != null && parent != view; parent = VisualTreeHelper.GetParent(parent))
                        if (parent is UIElement element && element.Visibility != Visibility.Visible) hidden = true;
                    if (hidden) continue;
                    Rect bounds = text.TransformToAncestor(view).TransformBounds(new Rect(text.RenderSize));
                    if (bounds.Right > width + 0.5 || bounds.Bottom > height + 0.5)
                        throw new InvalidOperationException($"{width}x{height} expanded={expanded} {text.Name}: {bounds}");
                    AssertEqual(TextTrimming.None, text.TextTrimming);
                    var measured = new TextBlock
                    {
                        Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily,
                        FontWeight = text.FontWeight, TextWrapping = text.TextWrapping
                    };
                    measured.Measure(new Size(Math.Max(1, text.ActualWidth), double.PositiveInfinity));
                    AssertTrue(text.ActualHeight + 1 >= measured.DesiredSize.Height);
                }
                if ((width == 288 && !expanded) || (expanded && (width == 160 || width == 600 || width == 416)))
                    CaptureLayout(view, "race-control-fit-" + width + "x" + height + "-" + expanded);
            }
        }

        private static void CaptureLayout(FrameworkElement view, string name, int settleMs = 1200)
        {
            if (_layoutCaptureDirectory == null) return;
            Directory.CreateDirectory(_layoutCaptureDirectory);
            // Settle ordinary layouts; motion-specific captures can request an in-flight frame.
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settleMs) };
            timer.Tick += (sender, args) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            view.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(_layoutCaptureDirectory, name + ".png"));
            encoder.Save(stream);
        }

        private static void LayoutTower(FrameworkElement view)
        {
            view.Measure(new Size(OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.Arrange(new Rect(0, 0, OverlayUiMetrics.TowerWidth, OverlayUiMetrics.TowerHeight));
            view.UpdateLayout();
        }

        private static ContentPresenter Container(ItemsControl items, int index)
            => items.ItemContainerGenerator.ContainerFromIndex(index) as ContentPresenter
                ?? throw new InvalidOperationException("Ranking row container " + index + " missing.");

        private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement
            => Descendants<T>(root).FirstOrDefault(item => item.Name == name)
                ?? throw new InvalidOperationException("Element '" + name + "' missing.");

        private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (!(child is DependencyObject dependency)) continue;
                if (dependency is T typed) yield return typed;
                foreach (T descendant in LogicalDescendants<T>(dependency)) yield return descendant;
            }
        }

        private static void AssertColor(string expectedHex, Brush brush)
        {
            var expected = (Color)ColorConverter.ConvertFromString(expectedHex);
            Color actual = brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;
            AssertEqual(expected.ToString(), actual.ToString());
        }

        private static void PumpDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

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
