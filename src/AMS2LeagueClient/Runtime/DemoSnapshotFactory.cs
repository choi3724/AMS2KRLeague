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
        public static TelemetrySnapshot CreateSnapshot()
        {
            string[] names =
            {
                "김도윤", "SAFETY CAR", "이준서", "박시우", "최민재", "정현우", "강지훈", "윤서진", "한도현", "오지환",
                "송민준", "임태윤", "조현석", "배준호", "문지후", "권성민", "ENG-IceBlasT", "마르코 보렐리", "올리 제임스", "디미트리스 보르카스",
                "조나단 포터", "아드리안 톨레도", "티모시 크로스", "루카스 하만", "피터 홀즈", "마르셀 라이만", "안드레스 가리도", "브랜든 시몬스",
                "필립 에거", "미론 곤차로바"
            };
            var participants = new ParticipantSnapshot[names.Length];
            const int playerIndex = 16;
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
                case OverlayEventType.PitEntry:
                    return new OverlayEvent(type, OverlayEventPriority.Normal, now, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), text.Get(OverlayTextKey.PitEntry), "LAP 3", string.Empty, "DEMO");
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
