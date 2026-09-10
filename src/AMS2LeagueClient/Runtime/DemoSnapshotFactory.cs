using System;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Runtime
{
    public static class DemoSnapshotFactory
    {
        internal static DrivingTelemetryHistory CreateDrivingPreview()
        {
            var history = new DrivingTelemetryHistory();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            for (int i = 0; i <= 200; i++)
            {
                history.Add(CreatePreviewSample(now.AddSeconds((i - 200) * 0.05)));
            }
            return history;
        }

        internal static DrivingTelemetrySample CreatePreviewSample(DateTimeOffset time)
        {
            double phase = time.ToUnixTimeMilliseconds() / 1000.0 * 1.5;
            return new DrivingTelemetrySample(time, 0, 0, 0.5 + Math.Sin(phase) * 0.45,
                0.5 + Math.Sin(phase + 2.1) * 0.45, 0.5 + Math.Sin(phase + 4.2) * 0.45,
                Math.Max(0, Math.Sin(phase * 0.5) - 0.9) * 3, 123 / 3.6, 3,
                absActive: Math.Sin(phase) > 0.5, steering: Math.Sin(phase * 0.4) * 0.3, rpm: 5941, maxRpm: 8000);
        }

        public static TelemetrySnapshot CreateSnapshot()
        {
            string[] names = new string[30];
            for (int index = 0; index < names.Length; index++)
            {
                names[index] = "드라이버 " + (index + 1).ToString("00");
            }
            names[1] = "SAFETY CAR";
            var participants = new ParticipantSnapshot[names.Length];
            const int playerIndex = 16;
            names[playerIndex] = "플레이어";
            const float playerDistance = 2500;
            for (int index = 0; index < participants.Length; index++)
            {
                bool safetyCar = index == 1;
                float best = safetyCar ? 92.000f : 100.100f + index * 0.3f;
                float distance = playerDistance + ((playerIndex - index) * (index < playerIndex ? 57 : 74));
                participants[index] = new ParticipantSnapshot(
                    index,
                    true,
                    names[index],
                    (uint)index + 1,
                    2,
                    3,
                    1,
                    (uint)RaceState.Racing,
                    (uint)PitMode.None,
                    index == playerIndex ? 100.973f : best,
                    index == playerIndex ? 101.520f : best + 0.7f,
                    safetyCar ? "Mercedes AMG SafetyCar" : "Aston Martin Vantage GT3",
                    safetyCar ? "SafetyCar" : "GT3",
                    distance,
                    false,
                    index == playerIndex ? 34.271f : 34.8f,
                    index == playerIndex ? 29.882f : 30.1f,
                    -1);
            }

            return new TelemetrySnapshot(
                DateTimeOffset.UtcNow,
                14,
                24132163,
                204,
                (uint)GameState.InGamePlaying,
                (uint)SessionState.Race,
                (uint)RaceState.Racing,
                playerIndex,
                participants.Length,
                0,
                101.520f,
                100.973f,
                1.214f,
                0.873f,
                participants,
                "Bathurst",
                "2020",
                3,
                false,
                102.881f,
                34.271f,
                29.882f,
                -1,
                trackLength: 6213.0f,
                eventTimeRemaining: 1458.0f);
        }

        public static OverlayShellViewModel CreateShell(bool diagnostic, OverlayEventType? eventType = null)
        {
            TelemetrySnapshot snapshot = CreateSnapshot();
            LocalParticipantResolution local = new LocalParticipantResolver().Resolve(snapshot);
            if (!local.IsValid || local.Participant == null) throw new InvalidOperationException(local.Reason);
            LeagueClassification league = new LeagueClassificationResolver().Resolve(snapshot, local.Participant);
            OverlayEvent? item = eventType.HasValue ? CreateEvent(eventType.Value) : null;
            OverlayViewModel timing = OverlayViewModel.Build(
                snapshot,
                local.Participant,
                league,
                30.0,
                20.0,
                diagnostic,
                OverlayTextCatalog.Korean.Get(OverlayTextKey.DemoSimulation),
                item,
                eventType.HasValue ? 2 : 0);
            timing.AheadDistanceTrendArrow = "▲";
            timing.AheadDistanceColor = "#FF7777";
            timing.BehindDistanceTrendArrow = "▼";
            timing.BehindDistanceColor = "#FF7777";
            return OverlayShellViewModel.Build(snapshot, timing, item, true);
        }

        public static OverlayViewModel CreateViewModel(bool diagnostic) => CreateShell(diagnostic).Timing;

        public static OverlayEvent CreateEvent(OverlayEventType type)
        {
            OverlayTextCatalog text = OverlayTextCatalog.Korean;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            switch (type)
            {
                case OverlayEventType.PositionGained:
                    return new OverlayEvent(type, OverlayEventPriority.High, now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12), text.Get(OverlayTextKey.PositionGained), "P5 → P4", "▲ 1", "DEMO");
                case OverlayEventType.PositionLost:
                    return new OverlayEvent(type, OverlayEventPriority.High, now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12), text.Get(OverlayTextKey.PositionLost), "P4 → P5", "▼ 1", "DEMO");
                case OverlayEventType.PersonalBest:
                    return new OverlayEvent(type, OverlayEventPriority.Low, now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12), text.Get(OverlayTextKey.PersonalBest), "1:40.973", "-0.547", "DEMO");
                case OverlayEventType.RaceFastestLap:
                    return new OverlayEvent(type, OverlayEventPriority.Normal, now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12), text.Get(OverlayTextKey.RaceFastestLap), "LEE", "1:40.973", "DEMO");
                case OverlayEventType.Battle:
                case OverlayEventType.BattleBehind:
                    bool behind = type == OverlayEventType.BattleBehind;
                    return new OverlayEvent(type, OverlayEventPriority.Low, now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10),
                        text.Get(behind ? OverlayTextKey.BattleBehind : OverlayTextKey.BattleAhead), behind ? "P5 드라이버 05" : "P3 드라이버 03",
                        behind ? "+0.420 · 40m" : "+0.680 · 60m", "DEMO");
                case OverlayEventType.PitEntry:
                    return new OverlayEvent(type, OverlayEventPriority.Normal, now, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), text.Get(OverlayTextKey.PitEntry), "랩 3", string.Empty, "DEMO");
                case OverlayEventType.FinalLap:
                    return new OverlayEvent(type, OverlayEventPriority.Critical, now, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(20), text.Get(OverlayTextKey.FinalLap), "현재 순위 P4", string.Empty, "DEMO");
                case OverlayEventType.Finish:
                    return new OverlayEvent(type, OverlayEventPriority.Critical, now, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), text.Get(OverlayTextKey.Finish), "최종 순위 P4", string.Empty, "DEMO");
                default:
                    return new OverlayEvent(type, OverlayEventPriority.Normal, now, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12), type.ToString(), string.Empty, string.Empty, "DEMO");
            }
        }
    }
}
