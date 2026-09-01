using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class RankingRowViewModel
    {
        public int ParticipantIndex { get; set; } = -1;
        public string Position { get; set; } = "P—";
        public string Name { get; set; } = "—";
        public string Class { get; set; } = "—";
        public string Lap { get; set; } = "L—";
        public bool IsPlayer { get; set; }
        public string Background { get; set; } = OverlayUiPalette.NormalRowBackground;
        public string Accent { get; set; } = "Transparent";
        public string Foreground { get; set; } = "#DDE7F1";
        public string Status { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#91A5B8";
    }

    public sealed class OverlayViewModel
    {
        public const int MaxRankingRows = 15;

        public string EnvironmentLabel { get; set; } = "실제 AMS2 · 읽기 전용";
        public bool IsDiagnostic { get; set; }
        public string PositionText { get; set; } = "P— / —";
        public string LapText { get; set; } = "LAP — / —";
        public string AheadPosition { get; set; } = "P—";
        public string AheadName { get; set; } = "앞차 없음";
        public string AheadGap { get; set; } = "—";
        public string AheadSource { get; set; } = "UNKNOWN";
        public string AheadIndex { get; set; } = "—";
        public string BehindPosition { get; set; } = "P—";
        public string BehindName { get; set; } = "뒤차 없음";
        public string BehindGap { get; set; } = "—";
        public string AheadDistance { get; set; } = "—";
        public string BehindDistance { get; set; } = "—";
        public bool IsBottomGapPanelVisible { get; set; }
        public string BehindSource { get; set; } = "UNKNOWN";
        public string BehindIndex { get; set; } = "—";
        public string LastLapText { get; set; } = "—";
        public string BestLapText { get; set; } = "—";
        public string CurrentLapText { get; set; } = "—";
        public string Sector1Text { get; set; } = "—";
        public string Sector2Text { get; set; } = "—";
        public string Sector3Text { get; set; } = "—";
        public string CurrentLapStateText { get; set; } = "게임 텔레메트리";
        public string ShmVersion { get; set; } = "—";
        public string BuildVersion { get; set; } = "—";
        public string GameState { get; set; } = "—";
        public string SessionState { get; set; } = "—";
        public string ParticipantCount { get; set; } = "—";
        public string LeagueParticipantCount { get; set; } = "—";
        public string SafetyCarsExcluded { get; set; } = "0";
        public string RawPosition { get; set; } = "—";
        public string LeaguePosition { get; set; } = "—";
        public string RawAhead { get; set; } = "—";
        public string LeagueAhead { get; set; } = "—";
        public string RawBehind { get; set; } = "—";
        public string LeagueBehind { get; set; } = "—";
        public string ViewedIndex { get; set; } = "—";
        public string LocalState { get; set; } = "—";
        public string LocalPit { get; set; } = "—";
        public string SnapshotRate { get; set; } = "0.0 Hz";
        public string UiRate { get; set; } = "0.0 Hz";
        public string EventQueueText { get; set; } = "0";
        public string CurrentEventText { get; set; } = "—";
        public string PlayerOverlayLabel { get; set; } = "플레이어 오버레이";
        public string RaceGapLabel { get; set; } = "트랙 전후방 · 게임 텔레메트리";
        public string LastLabel { get; set; } = "직전";
        public string BestLabel { get; set; } = "최고";
        public string CurrentLabel { get; set; } = "현재";
        public string DiagnosticLabel { get; set; } = "진단 모드";
        public string RemainingTimeLabel { get; set; } = "남은 시간";
        public string RemainingTimeText { get; set; } = "—";
        public string OverallPositionText { get; set; } = "P— / —";
        public string ClassPositionText { get; set; } = "C— / —";
        public string CurrentLapHeaderText { get; set; } = "LAP —";
        public string RankingRangeText { get; set; } = "순위";
        public IReadOnlyList<RankingRowViewModel> RankingRows { get; set; } = Array.Empty<RankingRowViewModel>();
        public int TotalPositionsVisible => RankingRows.Count;
        public bool IsPlayerVisibleInRanking => RankingRows.Any(row => row.IsPlayer);
        public string TrackLengthText { get; set; } = "—";
        public string EventTimeRemainingText { get; set; } = "—";
        public string DiagnosticParticipantText { get; set; } = "—";
        public string PitScheduleRawText { get; set; } = "—";
        public string PitModeRawText { get; set; } = "—";
        public string RaceStateRawText { get; set; } = "—";
        public string FlagColourRawText { get; set; } = "—";
        public string FlagReasonRawText { get; set; } = "—";
        public string DerivedPenaltyText { get; set; } = "—";
        public string DerivedRaceControlText { get; set; } = "—";
        public string EventProvenanceText { get; set; } = "—";
        public string EventConfidenceText { get; set; } = "—";

        public static OverlayViewModel Build(
            TelemetrySnapshot snapshot,
            ParticipantSnapshot local,
            LeagueClassification league,
            double snapshotRate,
            double uiRate,
            bool diagnostic,
            string environmentLabel,
            OverlayEvent? currentEvent = null,
            int queuedEvents = 0,
            OverlayTextCatalog? text = null,
            IReadOnlyDictionary<int, ParticipantBroadcastState>? broadcastStates = null,
            RaceControlUpdate? raceControl = null,
            float? eventTimeRemainingOverride = null,
            string? eventTimeRemainingTextOverride = null)
        {
            OverlayTextCatalog catalog = text ?? OverlayTextCatalog.Korean;
            broadcastStates = broadcastStates ?? raceControl?.ParticipantStates;
            var gapPresenter = new GapPresenter();
            TrackProximity proximity = new TrackProximityResolver().Resolve(
                snapshot.TrackLength,
                local,
                league.Participants.Select(item => item.Source));
            ParticipantSnapshot? ahead = proximity.Ahead ?? league.Ahead?.Source;
            ParticipantSnapshot? behind = proximity.Behind ?? league.Behind?.Source;
            LeagueParticipant? aheadLeague = LeagueParticipantOf(league, ahead);
            LeagueParticipant? behindLeague = LeagueParticipantOf(league, behind);
            bool aheadMatchesGameSplit = ahead != null
                && league.Ahead?.Source.Index == ahead.Index
                && league.CanUseAheadGameSplit;
            bool behindMatchesGameSplit = behind != null
                && league.Behind?.Source.Index == behind.Index
                && league.CanUseBehindGameSplit;
            GapDisplay aheadGap = aheadMatchesGameSplit
                ? gapPresenter.Present(snapshot.SplitTimeAhead, local, ahead)
                : new GapDisplay("—", GapSource.Unknown);
            GapDisplay behindGap = behindMatchesGameSplit
                ? gapPresenter.Present(snapshot.SplitTimeBehind, local, behind)
                : new GapDisplay("—", GapSource.Unknown);
            float last = local.LastLapTime > 0 ? local.LastLapTime : snapshot.LastLapTime;
            float best = local.BestLapTime > 0 ? local.BestLapTime : snapshot.BestLapTime;
            uint displayLap = local.CurrentLap > 0 ? local.CurrentLap : local.LapsCompleted + 1;
            string noCar = catalog.CultureName == "ko-KR" ? "차량 없음" : "NO CAR";
            IReadOnlyList<RankingRowViewModel> rankingRows = BuildRankingRows(league, local.Index, broadcastStates, league.FastestLapParticipant?.Source.Index);
            bool playerPinnedAfterLeaders = league.Local?.LeaguePosition > MaxRankingRows;
            float displayedEventTimeRemaining = eventTimeRemainingOverride ?? snapshot.EventTimeRemaining;
            string range = rankingRows.Count == 0
                ? "순위"
                : playerPinnedAfterLeaders && rankingRows.Count > 1
                    ? rankingRows[0].Position + " — " + rankingRows[rankingRows.Count - 2].Position
                        + " · PLAYER " + rankingRows[rankingRows.Count - 1].Position
                    : rankingRows[0].Position + " — " + rankingRows[rankingRows.Count - 1].Position;

            return new OverlayViewModel
            {
                EnvironmentLabel = environmentLabel,
                IsDiagnostic = diagnostic,
                PositionText = "P" + (league.Local?.LeaguePosition ?? 0) + " / " + league.LeagueParticipantCount,
                LapText = "LAP " + displayLap + " / " + (snapshot.LapsInEvent == 0 ? "—" : snapshot.LapsInEvent.ToString(CultureInfo.InvariantCulture)),
                AheadPosition = PositionOf(aheadLeague),
                AheadName = NameOf(ahead, noCar),
                AheadGap = aheadGap.Text,
                AheadDistance = proximity.AheadDistance.Text,
                AheadSource = StateText.GapSourceText(aheadGap.Source),
                AheadIndex = IndexOf(ahead),
                BehindPosition = PositionOf(behindLeague),
                BehindName = NameOf(behind, noCar),
                BehindGap = behindGap.Text,
                BehindDistance = proximity.BehindDistance.Text,
                IsBottomGapPanelVisible = ahead != null || behind != null,
                BehindSource = StateText.GapSourceText(behindGap.Source),
                BehindIndex = IndexOf(behind),
                LastLapText = FormatLapTime(last),
                BestLapText = FormatLapTime(best),
                CurrentLapText = FormatLapTime(snapshot.CurrentTime),
                Sector1Text = FormatSectorTime(PreferParticipantTime(local.CurrentSector1Time, snapshot.CurrentSector1Time), 1, snapshot.NumSectors, local.CurrentSector),
                Sector2Text = FormatSectorTime(PreferParticipantTime(local.CurrentSector2Time, snapshot.CurrentSector2Time), 2, snapshot.NumSectors, local.CurrentSector),
                Sector3Text = FormatSectorTime(PreferParticipantTime(local.CurrentSector3Time, snapshot.CurrentSector3Time), 3, snapshot.NumSectors, local.CurrentSector),
                CurrentLapStateText = local.LapInvalidated || snapshot.LapInvalidated ? catalog.Get(OverlayTextKey.CurrentLapInvalid) : catalog.Get(OverlayTextKey.GameTelemetry),
                ShmVersion = snapshot.Version.ToString(CultureInfo.InvariantCulture),
                BuildVersion = snapshot.BuildVersion.ToString(CultureInfo.InvariantCulture),
                GameState = StateText.Game(snapshot.GameStateRaw),
                SessionState = catalog.SessionName(snapshot.KnownSessionState),
                ParticipantCount = league.RawParticipantCount.ToString(CultureInfo.InvariantCulture),
                LeagueParticipantCount = league.LeagueParticipantCount.ToString(CultureInfo.InvariantCulture),
                SafetyCarsExcluded = league.SafetyCarsExcluded.ToString(CultureInfo.InvariantCulture),
                RawPosition = "P" + local.RacePosition,
                LeaguePosition = "P" + (league.Local?.LeaguePosition ?? 0),
                RawAhead = RawRelative(league.RawAhead),
                LeagueAhead = LeagueRelative(league.Ahead),
                RawBehind = RawRelative(league.RawBehind),
                LeagueBehind = LeagueRelative(league.Behind),
                ViewedIndex = snapshot.ViewedParticipantIndex.ToString(CultureInfo.InvariantCulture),
                LocalState = catalog.RaceStateName(local.KnownRaceState),
                LocalPit = catalog.PitStateName(local.KnownPitMode),
                SnapshotRate = snapshotRate.ToString("0.0", CultureInfo.InvariantCulture) + " Hz",
                UiRate = uiRate.ToString("0.0", CultureInfo.InvariantCulture) + " Hz",
                EventQueueText = queuedEvents.ToString(CultureInfo.InvariantCulture),
                CurrentEventText = currentEvent?.Type.ToString() ?? "—",
                PlayerOverlayLabel = catalog.Get(OverlayTextKey.PlayerOverlay),
                RaceGapLabel = catalog.Get(OverlayTextKey.RaceGapGameProvided),
                LastLabel = catalog.Get(OverlayTextKey.Last),
                BestLabel = catalog.Get(OverlayTextKey.Best),
                CurrentLabel = catalog.Get(OverlayTextKey.Current),
                DiagnosticLabel = catalog.Get(OverlayTextKey.DiagnosticMode),
                RemainingTimeText = eventTimeRemainingTextOverride ?? FormatRemainingTime(displayedEventTimeRemaining),
                OverallPositionText = "P" + (league.Local?.LeaguePosition ?? 0) + " / " + league.LeagueParticipantCount,
                ClassPositionText = FormatClassPosition(league, local),
                CurrentLapHeaderText = "LAP " + displayLap,
                RankingRangeText = range,
                RankingRows = rankingRows,
                TrackLengthText = IsPositiveFinite(snapshot.TrackLength) ? snapshot.TrackLength.ToString("0", CultureInfo.InvariantCulture) + "m" : "—",
                EventTimeRemainingText = eventTimeRemainingTextOverride
                    ?? (IsNonNegativeFinite(displayedEventTimeRemaining) ? displayedEventTimeRemaining.ToString("0", CultureInfo.InvariantCulture) + "s" : "—"),
                DiagnosticParticipantText = "P" + (league.Local?.LeaguePosition ?? 0) + " " + local.Name + " #" + local.Index,
                PitScheduleRawText = local.PitScheduleRaw.ToString(CultureInfo.InvariantCulture),
                PitModeRawText = local.PitModeRaw.ToString(CultureInfo.InvariantCulture),
                RaceStateRawText = local.RaceStateRaw.ToString(CultureInfo.InvariantCulture),
                FlagColourRawText = local.HighestFlagColourRaw.ToString(CultureInfo.InvariantCulture) + " / ROOT " + snapshot.HighestFlagColourRaw,
                FlagReasonRawText = local.HighestFlagReasonRaw.ToString(CultureInfo.InvariantCulture) + " / ROOT " + snapshot.HighestFlagReasonRaw,
                DerivedPenaltyText = broadcastStates != null && broadcastStates.TryGetValue(local.Index, out ParticipantBroadcastState? localBroadcast) ? localBroadcast.PenaltyState.ToString() : "—",
                DerivedRaceControlText = raceControl?.ActiveEvent?.Type.ToString() ?? "—",
                EventProvenanceText = raceControl?.ActiveEvent?.Source ?? "—",
                EventConfidenceText = raceControl?.ActiveEvent?.Confidence.ToString() ?? "—"
            };
        }

        public static OverlayViewModel Build(TelemetrySnapshot snapshot, ParticipantSnapshot local, RelativeDrivers relatives, double snapshotRate, double uiRate, bool diagnostic, string environmentLabel)
        {
            LeagueClassification league = new LeagueClassificationResolver().Resolve(snapshot, local);
            return Build(snapshot, local, league, snapshotRate, uiRate, diagnostic, environmentLabel, text: OverlayTextCatalog.English);
        }

        public static string FormatLapTime(float seconds)
        {
            if (!IsPositiveFinite(seconds)) return "—";
            int minutes = (int)(seconds / 60.0f);
            float remaining = seconds - minutes * 60.0f;
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + remaining.ToString("00.000", CultureInfo.InvariantCulture);
        }

        public static string FormatRemainingTime(float secondsRemaining)
        {
            if (!IsNonNegativeFinite(secondsRemaining)) return "—";

            // AMS2's shipped header labels this field as milliseconds, but the
            // live game publishes seconds (for example ~3596 at 59:56 remaining).
            long totalSeconds = Math.Max(0, (long)Math.Ceiling(secondsRemaining));
            long hours = totalSeconds / 3600;
            long minutes = totalSeconds / 60 % 60;
            long seconds = totalSeconds % 60;
            return hours > 0
                ? hours.ToString(CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture)
                : minutes.ToString(CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<RankingRowViewModel> BuildRankingRows(
            LeagueClassification league,
            int localIndex,
            IReadOnlyDictionary<int, ParticipantBroadcastState>? broadcastStates,
            int? fastestIndex)
        {
            List<LeagueParticipant> selected = league.Participants
                .Take(MaxRankingRows)
                .ToList();
            LeagueParticipant? local = league.Participants.FirstOrDefault(item => item.Source.Index == localIndex);
            if (local != null && selected.All(item => item.Source.Index != localIndex))
            {
                selected = league.Participants.Take(MaxRankingRows - 1).ToList();
                selected.Add(local);
            }

            return selected
                .Select(item => new RankingRowViewModel
                {
                    ParticipantIndex = item.Source.Index,
                    Position = "P" + item.LeaguePosition,
                    Name = string.IsNullOrWhiteSpace(item.Source.Name) ? "—" : item.Source.Name,
                    Class = CompactClass(item.Source.VehicleClass),
                    Lap = "L" + (item.Source.CurrentLap > 0 ? item.Source.CurrentLap : item.Source.LapsCompleted + 1),
                    IsPlayer = item.Source.Index == localIndex,
                    Background = item.Source.Index == localIndex ? OverlayUiPalette.PlayerRowBackground : OverlayUiPalette.NormalRowBackground,
                    Accent = item.Source.Index == localIndex ? "#82F1D0" : "Transparent",
                    Foreground = item.Source.Index == localIndex ? "#FFFFFF" : "#DDE7F1",
                    Status = StatusOf(item.Source.Index, broadcastStates, fastestIndex),
                    StatusColor = StatusColorOf(item.Source.Index, broadcastStates, fastestIndex)
                })
                .ToArray();
        }

        private static string StatusOf(
            int participantIndex,
            IReadOnlyDictionary<int, ParticipantBroadcastState>? states,
            int? fastestIndex)
        {
            if (states != null && states.TryGetValue(participantIndex, out ParticipantBroadcastState? state) && state.CompactCode.Length > 0)
            {
                return state.CompactCode;
            }
            return fastestIndex == participantIndex ? "BEST" : string.Empty;
        }

        private static string StatusColorOf(
            int participantIndex,
            IReadOnlyDictionary<int, ParticipantBroadcastState>? states,
            int? fastestIndex)
        {
            if (states != null && states.TryGetValue(participantIndex, out ParticipantBroadcastState? state))
            {
                switch (state.PenaltyState)
                {
                    case ParticipantPenaltyState.Disqualified:
                    case ParticipantPenaltyState.Dnf:
                    case ParticipantPenaltyState.Retired:
                        return "#FF7777";
                    case ParticipantPenaltyState.DriveThrough:
                    case ParticipantPenaltyState.StopGo:
                        return "#FFD166";
                    case ParticipantPenaltyState.Pit:
                        return "#57D5FF";
                }
            }
            return fastestIndex == participantIndex ? "#B68CFF" : "#91A5B8";
        }

        private static string FormatClassPosition(LeagueClassification league, ParticipantSnapshot local)
        {
            if (string.IsNullOrWhiteSpace(local.VehicleClass)) return "C— / —";
            LeagueParticipant[] sameClass = league.Participants
                .Where(item => string.Equals(item.Source.VehicleClass, local.VehicleClass, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            int offset = Array.FindIndex(sameClass, item => item.Source.Index == local.Index);
            return offset < 0 ? "C— / " + sameClass.Length : "C" + (offset + 1) + " / " + sameClass.Length;
        }

        private static string CompactClass(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            string compact = value.Replace("_Gen", " G", StringComparison.OrdinalIgnoreCase).Replace('_', ' ');
            return compact.Length <= 8 ? compact : compact.Substring(0, 8);
        }

        private static float PreferParticipantTime(float participantTime, float viewedParticipantTime)
            => IsPositiveFinite(participantTime) ? participantTime : viewedParticipantTime;

        private static string FormatSectorTime(float seconds, int sector, int numSectors, int currentSector)
        {
            if (currentSector < 0 || sector > currentSector + 1 || (numSectors > 0 && sector > numSectors)) return "—";
            return FormatLapTime(seconds);
        }

        private static bool IsPositiveFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;

        private static bool IsNonNegativeFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;

        private static string PositionOf(LeagueParticipant? participant)
            => participant == null ? "P—" : "P" + participant.LeaguePosition;

        private static LeagueParticipant? LeagueParticipantOf(LeagueClassification league, ParticipantSnapshot? participant)
            => participant == null
                ? null
                : league.Participants.FirstOrDefault(item => item.Source.Index == participant.Index);

        private static string NameOf(ParticipantSnapshot? participant, string none)
            => participant == null || string.IsNullOrWhiteSpace(participant.Name) ? none : participant.Name;

        private static string IndexOf(ParticipantSnapshot? participant)
            => participant == null ? "—" : participant.Index.ToString(CultureInfo.InvariantCulture);

        private static string RawRelative(ParticipantSnapshot? participant)
            => participant == null ? "—" : "P" + participant.RacePosition + " #" + participant.Index;

        private static string LeagueRelative(LeagueParticipant? participant)
            => participant == null ? "—" : "P" + participant.LeaguePosition + " #" + participant.Source.Index;
    }
}
