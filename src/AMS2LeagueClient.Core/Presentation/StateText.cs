using System;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Core.RaceControl;

namespace AMS2LeagueClient.Core.Presentation
{
    public static class StateText
    {
        public static string TowerStatus(string code) => code switch
        {
            "FIN" => "완주", "FINAL" => "완주", "RET" => "중도 포기", "DNF" => "미완주",
            "DSQ" => "실격", "PIT" => "피트", "BEST" => "최고",
            // Penalties have their own column; they must not replace pit/finish information.
            "DT" => string.Empty, "SG" => string.Empty,
            "MAND" => "의무 피트", "DMG" => "수리", "?" => "미확인", _ => code
        };

        public static string Penalty(ParticipantSnapshot driver)
        {
            if (driver.KnownRaceState == RaceState.Disqualified || driver.KnownHighestFlagColour == FlagColour.Black) return "실격";
            return driver.KnownPitSchedule switch
            {
                PitSchedule.DriveThrough => "드라이브스루",
                PitSchedule.StopGo => "스톱 앤 고",
                null => "미확인",
                _ => "—"
            };
        }

        public static string PenaltyLabel(ParticipantPenaltyState state) => state switch
        {
            ParticipantPenaltyState.None => "없음", ParticipantPenaltyState.DriveThrough => "드라이브스루",
            ParticipantPenaltyState.StopGo => "스톱 앤 고", ParticipantPenaltyState.MandatoryPit => "의무 피트",
            ParticipantPenaltyState.DamagePit => "수리 피트", ParticipantPenaltyState.Disqualified => "실격",
            ParticipantPenaltyState.Retired => "중도 포기", ParticipantPenaltyState.Dnf => "미완주",
            ParticipantPenaltyState.Pit => "피트", _ => "미확인"
        };

        public static string GameLabel(uint raw) => (GameState)raw switch
        {
            GameState.Exited => "종료", GameState.FrontEnd => "메인 메뉴", GameState.InGamePlaying => "주행 중",
            GameState.InGamePaused => "일시정지", GameState.InGameMenuTimeTicking => "세션 메뉴",
            GameState.InGameRestarting => "재시작 중", GameState.InGameReplay => "리플레이",
            GameState.FrontEndReplay => "리플레이 메뉴", _ => "미확인 (" + raw + ")"
        };

        public static string Game(uint raw)
        {
            return Enum.IsDefined(typeof(GameState), raw)
                ? ((GameState)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string Session(uint raw)
        {
            return Enum.IsDefined(typeof(SessionState), raw)
                ? ((SessionState)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string Race(uint raw)
        {
            return Enum.IsDefined(typeof(RaceState), raw)
                ? ((RaceState)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string Pit(uint raw)
        {
            return Enum.IsDefined(typeof(PitMode), raw)
                ? ((PitMode)raw).ToString().ToUpperInvariant()
                : "UNKNOWN(" + raw + ")";
        }

        public static string GapSourceText(GapSource source)
        {
            switch (source)
            {
                case GapSource.GameSplit:
                    return "GAME_SPLIT";
                case GapSource.Estimated:
                    return "ESTIMATED";
                case GapSource.LapDelta:
                    return "LAP_DELTA";
                case GapSource.Status:
                    return "STATUS";
                default:
                    return "UNKNOWN";
            }
        }
    }
}
