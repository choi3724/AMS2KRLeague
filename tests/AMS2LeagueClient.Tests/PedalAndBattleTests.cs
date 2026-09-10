using System;
using System.Linq;
using System.Text.Json;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void SeparatePedalLayoutMigration()
        {
            var profile = new OverlayLayoutProfile();
            profile.Capture(OverlayComponentKeys.PedalTelemetry, new OverlayBounds(1000, 700, 560, 160), 1920, 1080);
            profile.SetEnabled(OverlayComponentKeys.PedalTelemetry, false);
            profile.SetDrivingPanelDefaults();
            AssertEqual(560, profile.Resolve(OverlayComponentKeys.PedalTelemetry, default, 1920, 1080).Width);
            AssertFalse(profile.IsEnabled(OverlayComponentKeys.PedalGauge));
            AssertEqual("legacy", profile.DrivingHud.Normalize().TelemetryDesign);
            profile.DrivingHud.TelemetryDesign = "racing";
            profile.DrivingHud.TowerDesign = "racing";
            profile.SetEnabled(OverlayComponentKeys.PedalGauge, true);
            profile.Capture(OverlayComponentKeys.PedalGauge, new OverlayBounds(200, 300, 200, 220), 1920, 1080);
            string before = JsonSerializer.Serialize(profile);
            profile = JsonSerializer.Deserialize<OverlayLayoutProfile>(before)!;
            profile.SetDrivingPanelDefaults();
            AssertEqual(before, JsonSerializer.Serialize(profile));
            AssertEqual(560, profile.Resolve(OverlayComponentKeys.PedalTelemetry, default, 1920, 1080).Width);
            AssertEqual(200, profile.Resolve(OverlayComponentKeys.PedalGauge, default, 1920, 1080).Width);
            AssertTrue(profile.IsEnabled(OverlayComponentKeys.PedalGauge));
            AssertFalse(profile.IsEnabled(OverlayComponentKeys.PedalTelemetry));
            AssertEqual("racing", profile.DrivingHud.Normalize().TowerDesign);
            AssertEqual("racing", profile.DrivingHud.Normalize().TelemetryDesign);
            var defaults = OverlayComponentLayoutCalculator.Calculate(1920, 1080, 96, false, false);
            AssertTrue(defaults.PedalGauge.Right <= defaults.Pedals.X);
            Console.WriteLine("PROOF original 0.6.1 bounds/toggles preserved; racing styles and independent gauge bounds survive reload");
        }

        private static void BattlesAheadAndBehind()
        {
            RawFixtureBuilder Fixture() => new RawFixtureBuilder(3).SetViewedIndex(1).SetTrackTelemetry(5000, 600)
                .SetParticipantLapDistance(0, 1050).SetParticipantLapDistance(1, 1000).SetParticipantLapDistance(2, 960)
                .SetSplitAhead(0.6f).SetSplitBehind(0.4f);
            RaceEventUpdate Observe(RaceEventEngine engine, RawFixtureBuilder fixture, double seconds, BroadcastOverlayState state = BroadcastOverlayState.NormalRacing)
            {
                var snapshot = Parse(fixture, FixedTime().AddSeconds(seconds));
                return engine.Observe(snapshot, Classify(snapshot), 1, snapshot.CapturedAt, state);
            }
            var engine = new RaceEventEngine();
            var fixture = Fixture();
            Observe(engine, fixture, 0);
            AssertEqual(0, Observe(engine, fixture, 4).DetectedEvents.Count);
            var events = Observe(engine, fixture, 6).DetectedEvents;
            OverlayEvent ahead = events.Single(item => item.Type == OverlayEventType.Battle);
            OverlayEvent behind = events.Single(item => item.Type == OverlayEventType.BattleBehind);
            AssertEqual("앞차와 접전", ahead.Title); AssertEqual("뒷차와 접전", behind.Title);
            AssertEqual("P3 DRIVER_2", behind.PrimaryText); AssertTrue(behind.SecondaryText.Contains("40m"));
            AssertEqual(0, Observe(engine, fixture, 7).DetectedEvents.Count);
            AssertEqual(OverlayEventType.BattleBehind, engine.Tick(FixedTime().AddSeconds(9)).CurrentEvent!.Type);
            AssertEqual(2, Observe(engine, fixture, 32).DetectedEvents.Count(item => item.Type == OverlayEventType.Battle || item.Type == OverlayEventType.BattleBehind));
            var timing = new OverlayViewModel { AheadName = ahead.Driver, AheadGap = "+0.650", AheadDistance = "55m",
                BehindName = behind.Driver, BehindGap = "+0.350", BehindDistance = "35m" };
            AssertEqual("+0.350 · 35m", EventCardViewModel.FromEvent(behind, false, timing).SecondaryText);
            AssertEqual("+0.650 · 55m", EventCardViewModel.FromEvent(ahead, false, timing).SecondaryText);
            timing.BehindName = "ANOTHER_DRIVER";
            AssertEqual(behind.SecondaryText, EventCardViewModel.FromEvent(behind, false, timing).SecondaryText);
            var view = new EventCardView();
            view.SetViewModel(EventCardViewModel.FromEvent(behind, false), false);
            view.Measure(new System.Windows.Size(520, 84)); view.Arrange(new System.Windows.Rect(0, 0, 520, 84));
            CaptureLayout(view, "battle-behind-event");

            foreach (float gap in new[] { -1f, float.NaN, float.PositiveInfinity, 1.01f })
            {
                engine = new RaceEventEngine(); fixture = Fixture().SetSplitBehind(gap);
                Observe(engine, fixture, 0);
                var result = Observe(engine, fixture, 6).DetectedEvents;
                AssertTrue(result.Any(item => item.Type == OverlayEventType.Battle));
                AssertFalse(result.Any(item => item.Type == OverlayEventType.BattleBehind));
            }
            foreach (var invalid in new[] {
                Fixture().SetParticipantLapDistance(2, 899), // more than 100m behind; signed distance must be absolute
                Fixture().SetParticipantLapDistance(2, 1100), // classified behind but physically ahead
                Fixture().SetParticipant(2, true, "PIT", 3, 2, 3, RaceState.Racing, PitMode.InPit),
                Fixture().SetParticipantVehicle(2, "SafetyCar", "SafetyCar") })
            {
                engine = new RaceEventEngine(); Observe(engine, invalid, 0);
                AssertFalse(Observe(engine, invalid, 6).DetectedEvents.Any(item => item.Type == OverlayEventType.BattleBehind));
            }
            foreach (var state in new[] { BroadcastOverlayState.Yellow, BroadcastOverlayState.FullCourseYellow,
                BroadcastOverlayState.RedFlag, BroadcastOverlayState.Chequered, BroadcastOverlayState.PlayerDsq })
            {
                engine = new RaceEventEngine(); fixture = Fixture(); Observe(engine, fixture, 0);
                AssertFalse(Observe(engine, fixture, 6, state).DetectedEvents.Any(item => item.Type == OverlayEventType.Battle || item.Type == OverlayEventType.BattleBehind));
            }
            Console.WriteLine("PROOF battles: both directions queued, independent cooldowns, rear live gap, invalid split/distance/position/pit/safety-car/flag suppression");
        }
    }
}
