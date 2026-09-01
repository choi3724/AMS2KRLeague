using System;
using System.Linq;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class SessionInfoViewModel
    {
        public string PrimaryLabel { get; set; } = "남은 시간";
        public string PrimaryValue { get; set; } = "—";
        public string PositionLabel { get; set; } = "순위";
        public string PositionValue { get; set; } = "P— / —";
        public string LapLabel { get; set; } = "현재 랩";
        public string LapValue { get; set; } = "—";
    }

    public sealed class EventCardViewModel
    {
        public string EventId { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
        public string Title { get; set; } = string.Empty;
        public string PrimaryText { get; set; } = string.Empty;
        public string SecondaryText { get; set; } = string.Empty;
        public string Accent { get; set; } = "#82F1D0";
        public string Arrow { get; set; } = string.Empty;
        public bool IsDemo { get; set; }

        public static EventCardViewModel FromEvent(OverlayEvent? item, bool demo, OverlayViewModel? timing = null)
        {
            if (item == null) return new EventCardViewModel();
            bool down = item.Type == OverlayEventType.PositionLost;
            bool critical = item.Priority == OverlayEventPriority.Critical;
            return new EventCardViewModel
            {
                EventId = item.Id,
                IsVisible = true,
                Title = item.Title,
                PrimaryText = item.PrimaryText,
                SecondaryText = item.Type == OverlayEventType.Battle && timing != null
                    ? timing.AheadGap + " · " + timing.AheadDistance
                    : item.SecondaryText,
                Accent = down ? "#FF7777" : critical ? "#FFD166" : "#82F1D0",
                Arrow = item.Type == OverlayEventType.PositionGained ? "▲" : down ? "▼" : string.Empty,
                IsDemo = demo
            };
        }
    }

    public sealed class RaceControlViewModel
    {
        public bool IsVisible { get; set; }
        public string EventId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Accent { get; set; } = "#FFD166";
        public bool IsExpanded { get; set; }
        public string Title { get; set; } = "레이스 컨트롤";
        public string DriverLine { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StateLabel { get; set; } = string.Empty;
        public string HistoryText { get; set; } = string.Empty;
        public string CountText { get; set; } = "0";

        public static RaceControlViewModel FromEvent(OverlayEvent? item)
        {
            bool visible = item != null && (item.Type == OverlayEventType.FinalLap || item.Type == OverlayEventType.Finish || item.Type == OverlayEventType.Retired || item.Type == OverlayEventType.Disqualified);
            return new RaceControlViewModel
            {
                IsVisible = visible,
                EventId = visible ? item!.Id : string.Empty,
                Text = visible ? item!.Title : string.Empty,
                Accent = item?.Type == OverlayEventType.FinalLap ? "#FFD166" : "#82F1D0"
            };
        }

        public static RaceControlViewModel FromUpdate(RaceControlUpdate? update)
        {
            if (update == null) return new RaceControlViewModel();
            RaceControlEvent? item = update.ActiveEvent;
            BroadcastOverlayState displayState = update.OverlayState & ~BroadcastOverlayState.SessionTransition;
            bool stateVisible = displayState != BroadcastOverlayState.NormalRacing;
            // History is retained for diagnostics and for an expanded live card,
            // but must not keep an empty compact card alive after the event ends.
            bool visible = item != null || stateVisible;
            string accent = AccentFor(item, update.OverlayState);
            string driver = item == null || item.ParticipantIndex < 0
                ? string.Empty
                : "P" + item.LeaguePosition + " " + item.Driver;
            string history = string.Join("\n", update.History.Take(3).Select(historyItem =>
                historyItem.DetectedAt.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                + "  "
                + (historyItem.ParticipantIndex >= 0 ? historyItem.Driver + "  " : string.Empty)
                + historyItem.Message));
            return new RaceControlViewModel
            {
                IsVisible = visible,
                IsExpanded = item != null,
                EventId = item?.Id ?? "STATE:" + update.Version,
                Text = item?.Title ?? "레이스 컨트롤 " + update.History.Count,
                Title = item?.Title ?? "레이스 컨트롤",
                DriverLine = driver,
                Message = item?.Message ?? StateTextFor(update.OverlayState),
                StateLabel = StateTextFor(update.OverlayState),
                HistoryText = history,
                CountText = update.History.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Accent = accent
            };
        }

        private static string AccentFor(RaceControlEvent? item, BroadcastOverlayState state)
        {
            if ((state & BroadcastOverlayState.RedFlag) != 0 || item?.Type == RaceControlEventType.Disqualified) return "#FF5C5C";
            if ((state & BroadcastOverlayState.BlueFlagPlayer) != 0) return "#57A9FF";
            if ((state & BroadcastOverlayState.FullCourseYellow) != 0) return "#FFE14D";
            if ((state & BroadcastOverlayState.DoubleYellow) != 0) return "#FF9F43";
            if ((state & BroadcastOverlayState.Yellow) != 0) return "#FFD166";
            if (item?.Priority == RaceControlPriority.Penalty) return "#FFB454";
            return "#82F1D0";
        }

        private static string StateTextFor(BroadcastOverlayState state)
        {
            if ((state & BroadcastOverlayState.RedFlag) != 0) return "적색기";
            if ((state & BroadcastOverlayState.FullCourseYellow) != 0) return "전 코스 황색기";
            if ((state & BroadcastOverlayState.DoubleYellow) != 0) return "!! 이중 황색기";
            if ((state & BroadcastOverlayState.Yellow) != 0) return "! 황색기";
            if ((state & BroadcastOverlayState.Chequered) != 0) return "FINAL";
            if ((state & BroadcastOverlayState.FinalLap) != 0) return "마지막 랩";
            if ((state & BroadcastOverlayState.BlueFlagPlayer) != 0) return "청색기";
            if ((state & BroadcastOverlayState.PlayerDsq) != 0) return "실격";
            if ((state & BroadcastOverlayState.PlayerPenalty) != 0) return "페널티";
            if ((state & BroadcastOverlayState.PlayerPit) != 0) return "PIT";
            return string.Empty;
        }
    }

    public sealed class OverlayShellViewModel
    {
        public OverlayViewModel Timing { get; set; } = new OverlayViewModel();
        public SessionInfoViewModel Session { get; set; } = new SessionInfoViewModel();
        public EventCardViewModel EventCard { get; set; } = new EventCardViewModel();
        public RaceControlViewModel RaceControl { get; set; } = new RaceControlViewModel();

        public static OverlayShellViewModel Build(
            TelemetrySnapshot snapshot,
            OverlayViewModel timing,
            OverlayEvent? currentEvent,
            bool demo,
            OverlayTextCatalog? text = null,
            RaceControlUpdate? raceControl = null)
        {
            OverlayTextCatalog catalog = text ?? OverlayTextCatalog.Korean;
            bool timed = snapshot.SessionDuration > 0 || (snapshot.LapsInEvent == 0 && snapshot.EventTimeRemaining >= 0);
            uint currentLap = ParseLap(timing.CurrentLapHeaderText);
            uint remainingLaps = snapshot.LapsInEvent > currentLap ? snapshot.LapsInEvent - currentLap : 0;
            return new OverlayShellViewModel
            {
                Timing = timing,
                Session = new SessionInfoViewModel
                {
                    PrimaryLabel = timed ? "남은 시간" : "남은 랩",
                    PrimaryValue = timed ? timing.RemainingTimeText : remainingLaps + " / " + snapshot.LapsInEvent,
                    PositionValue = timing.OverallPositionText,
                    LapValue = currentLap.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                EventCard = EventCardViewModel.FromEvent(currentEvent, demo, timing),
                RaceControl = raceControl == null ? RaceControlViewModel.FromEvent(currentEvent) : RaceControlViewModel.FromUpdate(raceControl)
            };
        }

        private static uint ParseLap(string value)
        {
            string raw = (value ?? string.Empty).Replace("LAP", string.Empty).Trim();
            return uint.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0;
        }
    }

    public static class LeftTowerLayoutMetrics
    {
        public const int Width = OverlayUiMetrics.TowerWidth;
        public const int DesiredHeight = OverlayUiMetrics.TowerHeight;
        public const int DiagnosticHeight = OverlayUiMetrics.DiagnosticTowerHeight;
        public const int RankingRows = OverlayViewModel.MaxRankingRows;
        public const int RankingRowPitch = OverlayUiMetrics.RowPitch;
        public const int HeaderAndFooterHeight = OverlayUiMetrics.HeaderAndFooterHeight;
        public const int SessionGap = OverlayUiMetrics.ComponentGap;
        public const int GapRows = 2;
        public const int TimingColumns = 3;
        public const int SectorColumns = 3;

        public static int RequiredHeight => HeaderAndFooterHeight + RankingRows * RankingRowPitch;

        public static bool FitsWithoutOverlap(int viewportHeight)
            => viewportHeight >= RequiredHeight;
    }
}
