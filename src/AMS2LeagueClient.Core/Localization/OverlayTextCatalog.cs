using System;
using System.Collections.Generic;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Localization
{
    public enum OverlayTextKey
    {
        AppTitle,
        PlayerOverlay,
        RealReadOnly,
        DemoSimulation,
        RaceGapGameProvided,
        Current,
        Last,
        Best,
        CurrentLapInvalid,
        GameTelemetry,
        PositionGained,
        PositionLost,
        PersonalBest,
        RaceFastestLap,
        FinalLap,
        PitEntry,
        PitExit,
        Finish,
        Retired,
        Disqualified,
        InvalidLap,
        BattleAhead,
        CurrentPosition,
        FinalPosition,
        CompletedLap,
        AheadGap,
        Practice,
        Qualifying,
        Race,
        FormationLap,
        TimeAttack,
        Test,
        Waiting,
        DiagnosticMode,
        RawParticipants,
        LeagueParticipants,
        SafetyCarsExcluded,
        RawPosition,
        LeaguePosition,
        RawAhead,
        LeagueAhead,
        RawBehind,
        LeagueBehind,
        GapSource,
        EventQueue,
        CurrentEvent,
        SharedMemoryVersion,
        Ams2Build,
        GameState,
        Session,
        ViewedIndex,
        LocalStatePit,
        ReadUiRate,
        SessionInformation
    }

    public sealed class OverlayTextCatalog
    {
        private readonly IReadOnlyDictionary<OverlayTextKey, string> _values;
        private readonly OverlayTextCatalog? _fallback;

        private OverlayTextCatalog(string cultureName, IReadOnlyDictionary<OverlayTextKey, string> values, OverlayTextCatalog? fallback)
        {
            CultureName = cultureName;
            _values = values;
            _fallback = fallback;
        }

        public string CultureName { get; }

        public string Get(OverlayTextKey key)
        {
            if (_values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return _fallback?.Get(key) ?? key.ToString();
        }

        public string SessionName(SessionState? state)
        {
            return state switch
            {
                SessionState.Practice => Get(OverlayTextKey.Practice),
                SessionState.Qualify => Get(OverlayTextKey.Qualifying),
                SessionState.Race => Get(OverlayTextKey.Race),
                SessionState.FormationLap => Get(OverlayTextKey.FormationLap),
                SessionState.TimeAttack => Get(OverlayTextKey.TimeAttack),
                SessionState.Test => Get(OverlayTextKey.Test),
                _ => Get(OverlayTextKey.Waiting)
            };
        }

        public string RaceStateName(RaceState? state)
        {
            return state switch
            {
                RaceState.Finished => Get(OverlayTextKey.Finish),
                RaceState.Disqualified => Get(OverlayTextKey.Disqualified),
                RaceState.Retired => Get(OverlayTextKey.Retired),
                RaceState.Dnf => Get(OverlayTextKey.Retired),
                RaceState.Racing => Get(OverlayTextKey.Race),
                _ => Get(OverlayTextKey.Waiting)
            };
        }

        public string PitStateName(PitMode? state)
        {
            return state switch
            {
                PitMode.DrivingIntoPits => Get(OverlayTextKey.PitEntry),
                PitMode.InPit => Get(OverlayTextKey.PitEntry),
                PitMode.DrivingOutOfPits => Get(OverlayTextKey.PitExit),
                PitMode.InGarage => "차고",
                PitMode.DrivingOutOfGarage => "차고 출발",
                _ => "주행 중"
            };
        }

        public static OverlayTextCatalog English { get; } = new OverlayTextCatalog(
            "en-US",
            new Dictionary<OverlayTextKey, string>
            {
                [OverlayTextKey.AppTitle] = "AMS2 LEAGUE",
                [OverlayTextKey.PlayerOverlay] = "PLAYER OVERLAY",
                [OverlayTextKey.RealReadOnly] = "REAL AMS2 · READ-ONLY",
                [OverlayTextKey.DemoSimulation] = "DEMO / SIMULATION",
                [OverlayTextKey.RaceGapGameProvided] = "TRACK PROXIMITY · GAME TELEMETRY",
                [OverlayTextKey.Current] = "CURRENT",
                [OverlayTextKey.Last] = "LAST",
                [OverlayTextKey.Best] = "BEST",
                [OverlayTextKey.CurrentLapInvalid] = "CURRENT LAP INVALID",
                [OverlayTextKey.GameTelemetry] = "GAME TELEMETRY",
                [OverlayTextKey.PositionGained] = "POSITION GAINED",
                [OverlayTextKey.PositionLost] = "POSITION LOST",
                [OverlayTextKey.PersonalBest] = "PERSONAL BEST",
                [OverlayTextKey.RaceFastestLap] = "RACE FASTEST LAP",
                [OverlayTextKey.FinalLap] = "FINAL LAP",
                [OverlayTextKey.PitEntry] = "PIT ENTRY",
                [OverlayTextKey.PitExit] = "PIT EXIT",
                [OverlayTextKey.Finish] = "RACE FINISH",
                [OverlayTextKey.Retired] = "RETIRED",
                [OverlayTextKey.Disqualified] = "DISQUALIFIED",
                [OverlayTextKey.InvalidLap] = "CURRENT LAP INVALID",
                [OverlayTextKey.BattleAhead] = "BATTLE AHEAD",
                [OverlayTextKey.CurrentPosition] = "CURRENT POSITION",
                [OverlayTextKey.FinalPosition] = "FINAL POSITION",
                [OverlayTextKey.CompletedLap] = "COMPLETED LAP",
                [OverlayTextKey.AheadGap] = "GAP AHEAD",
                [OverlayTextKey.Practice] = "PRACTICE",
                [OverlayTextKey.Qualifying] = "QUALIFYING",
                [OverlayTextKey.Race] = "RACE",
                [OverlayTextKey.FormationLap] = "FORMATION LAP",
                [OverlayTextKey.TimeAttack] = "TIME ATTACK",
                [OverlayTextKey.Test] = "TEST",
                [OverlayTextKey.Waiting] = "WAITING",
                [OverlayTextKey.DiagnosticMode] = "DIAGNOSTIC MODE",
                [OverlayTextKey.RawParticipants] = "RAW PARTICIPANTS",
                [OverlayTextKey.LeagueParticipants] = "LEAGUE PARTICIPANTS",
                [OverlayTextKey.SafetyCarsExcluded] = "SAFETY CARS EXCLUDED",
                [OverlayTextKey.RawPosition] = "RAW POSITION",
                [OverlayTextKey.LeaguePosition] = "LEAGUE POSITION",
                [OverlayTextKey.RawAhead] = "RAW AHEAD",
                [OverlayTextKey.LeagueAhead] = "LEAGUE AHEAD",
                [OverlayTextKey.RawBehind] = "RAW BEHIND",
                [OverlayTextKey.LeagueBehind] = "LEAGUE BEHIND",
                [OverlayTextKey.GapSource] = "GAP SOURCE",
                [OverlayTextKey.EventQueue] = "EVENT QUEUE",
                [OverlayTextKey.CurrentEvent] = "CURRENT EVENT",
                [OverlayTextKey.SharedMemoryVersion] = "SHM VERSION",
                [OverlayTextKey.Ams2Build] = "AMS2 BUILD",
                [OverlayTextKey.GameState] = "GAME STATE",
                [OverlayTextKey.Session] = "SESSION",
                [OverlayTextKey.ViewedIndex] = "VIEWED INDEX",
                [OverlayTextKey.LocalStatePit] = "LOCAL STATE / PIT",
                [OverlayTextKey.ReadUiRate] = "READ / UI RATE",
                [OverlayTextKey.SessionInformation] = "SESSION INFO"
            },
            null);

        public static OverlayTextCatalog Korean { get; } = new OverlayTextCatalog(
            "ko-KR",
            new Dictionary<OverlayTextKey, string>
            {
                [OverlayTextKey.AppTitle] = "AMS2 LEAGUE",
                [OverlayTextKey.PlayerOverlay] = "플레이어 오버레이",
                [OverlayTextKey.RealReadOnly] = "실제 AMS2 · 읽기 전용",
                [OverlayTextKey.DemoSimulation] = "데모 / 시뮬레이션",
                [OverlayTextKey.RaceGapGameProvided] = "트랙 전후방 · 게임 텔레메트리",
                [OverlayTextKey.Current] = "현재",
                [OverlayTextKey.Last] = "직전",
                [OverlayTextKey.Best] = "최고",
                [OverlayTextKey.CurrentLapInvalid] = "현재 랩 무효",
                [OverlayTextKey.GameTelemetry] = "게임 텔레메트리",
                [OverlayTextKey.PositionGained] = "순위 상승",
                [OverlayTextKey.PositionLost] = "순위 하락",
                [OverlayTextKey.PersonalBest] = "개인 최고기록",
                [OverlayTextKey.RaceFastestLap] = "레이스 최고 랩",
                [OverlayTextKey.FinalLap] = "마지막 랩",
                [OverlayTextKey.PitEntry] = "피트 진입",
                [OverlayTextKey.PitExit] = "피트 이탈",
                [OverlayTextKey.Finish] = "레이스 종료",
                [OverlayTextKey.Retired] = "리타이어",
                [OverlayTextKey.Disqualified] = "실격",
                [OverlayTextKey.InvalidLap] = "현재 랩 무효",
                [OverlayTextKey.BattleAhead] = "앞차와 접전",
                [OverlayTextKey.CurrentPosition] = "현재 순위",
                [OverlayTextKey.FinalPosition] = "최종 순위",
                [OverlayTextKey.CompletedLap] = "완주 LAP",
                [OverlayTextKey.AheadGap] = "앞차와 간격",
                [OverlayTextKey.Practice] = "연습",
                [OverlayTextKey.Qualifying] = "예선",
                [OverlayTextKey.Race] = "레이스",
                [OverlayTextKey.FormationLap] = "포메이션 랩",
                [OverlayTextKey.TimeAttack] = "타임 어택",
                [OverlayTextKey.Test] = "테스트",
                [OverlayTextKey.Waiting] = "대기 중",
                [OverlayTextKey.DiagnosticMode] = "진단 모드",
                [OverlayTextKey.RawParticipants] = "원본 참가자",
                [OverlayTextKey.LeagueParticipants] = "리그 참가자",
                [OverlayTextKey.SafetyCarsExcluded] = "제외된 세이프티카",
                [OverlayTextKey.RawPosition] = "원본 순위",
                [OverlayTextKey.LeaguePosition] = "리그 순위",
                [OverlayTextKey.RawAhead] = "원본 앞차",
                [OverlayTextKey.LeagueAhead] = "리그 앞차",
                [OverlayTextKey.RawBehind] = "원본 뒤차",
                [OverlayTextKey.LeagueBehind] = "리그 뒤차",
                [OverlayTextKey.GapSource] = "간격 출처",
                [OverlayTextKey.EventQueue] = "이벤트 대기열",
                [OverlayTextKey.CurrentEvent] = "현재 이벤트",
                [OverlayTextKey.SharedMemoryVersion] = "공유 메모리 버전",
                [OverlayTextKey.Ams2Build] = "AMS2 빌드",
                [OverlayTextKey.GameState] = "게임 상태",
                [OverlayTextKey.Session] = "세션",
                [OverlayTextKey.ViewedIndex] = "표시 참가자 인덱스",
                [OverlayTextKey.LocalStatePit] = "로컬 상태 / 피트",
                [OverlayTextKey.ReadUiRate] = "읽기 / UI 속도",
                [OverlayTextKey.SessionInformation] = "세션 정보"
            },
            English);

        public static OverlayTextCatalog ForCulture(string? cultureName)
            => string.Equals(cultureName, "en-US", StringComparison.OrdinalIgnoreCase) ? English : Korean;
    }

    public static class KoreanUi
    {
        public static string AppTitle => OverlayTextCatalog.Korean.Get(OverlayTextKey.AppTitle);
        public static string PlayerOverlay => OverlayTextCatalog.Korean.Get(OverlayTextKey.PlayerOverlay);
        public static string ReadOnlySharedMemory => "읽기 전용 공유 메모리";
        public static string NoInjection => "인젝션 없음";
        public static string NoInputHook => "입력 후킹 없음";
        public static string CloseHint => "이 창을 닫으면 클라이언트가 종료됩니다. 오버레이는 입력 포커스를 받지 않습니다.";
    }
}
