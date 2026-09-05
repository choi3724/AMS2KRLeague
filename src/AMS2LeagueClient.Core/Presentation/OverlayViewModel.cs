using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public sealed class RankingRowViewModel : INotifyPropertyChanged
    {
        public int ParticipantIndex { get; set; } = -1;
        public string Position { get; set; } = "P—";
        public string Name { get; set; } = "—";
        public string Class { get; set; } = "—";
        public string Lap { get; set; } = "L—";
        public string CurrentTime { get; set; } = "—";
        public bool IsPlayer { get; set; }
        public string Background { get; set; } = OverlayUiPalette.NormalRowBackground;
        public string Accent { get; set; } = "Transparent";
        public string Foreground { get; set; } = "#DDE7F1";
        public string ClassBackground { get; set; } = ClassBadgePalette.FallbackBackground;
        public string ClassForeground { get; set; } = ClassBadgePalette.FallbackForeground;
        public string TimeForeground { get; set; } = OverlayUiPalette.ActiveTime;
        public ParticipantRowDisplayState DisplayState { get; set; } = ParticipantRowDisplayState.Active;
        public bool IsDimmed { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#91A5B8";

        public event PropertyChangedEventHandler? PropertyChanged;

        public void UpdateFrom(RankingRowViewModel source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            bool timeChanged = CurrentTime != source.CurrentTime;
            CurrentTime = source.CurrentTime;
            if (ParticipantIndex == source.ParticipantIndex
                && Position == source.Position
                && Name == source.Name
                && Class == source.Class
                && Lap == source.Lap
                && IsPlayer == source.IsPlayer
                && Background == source.Background
                && Accent == source.Accent
                && Foreground == source.Foreground
                && ClassBackground == source.ClassBackground
                && ClassForeground == source.ClassForeground
                && TimeForeground == source.TimeForeground
                && DisplayState == source.DisplayState
                && IsDimmed == source.IsDimmed
                && Status == source.Status
                && StatusColor == source.StatusColor)
            {
                if (timeChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTime)));
                return;
            }

            ParticipantIndex = source.ParticipantIndex;
            Position = source.Position;
            Name = source.Name;
            Class = source.Class;
            Lap = source.Lap;
            CurrentTime = source.CurrentTime;
            IsPlayer = source.IsPlayer;
            Background = source.Background;
            Accent = source.Accent;
            Foreground = source.Foreground;
            ClassBackground = source.ClassBackground;
            ClassForeground = source.ClassForeground;
            TimeForeground = source.TimeForeground;
            DisplayState = source.DisplayState;
            IsDimmed = source.IsDimmed;
            Status = source.Status;
            StatusColor = source.StatusColor;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
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
        public int? AheadLapGapCandidate { get; set; }
        public int? BehindLapGapCandidate { get; set; }
        public string AheadParticipantKey { get; set; } = string.Empty;
        public string BehindParticipantKey { get; set; } = string.Empty;
        public string AheadDistance { get; set; } = "—";
        public string BehindDistance { get; set; } = "—";
        public int AheadParticipantIndex { get; set; } = -1;
        public int BehindParticipantIndex { get; set; } = -1;
        public int? AheadDistanceMeters { get; set; }
        public int? BehindDistanceMeters { get; set; }
        public string AheadDistanceTrendArrow { get; set; } = string.Empty;
        public string BehindDistanceTrendArrow { get; set; } = string.Empty;
        public string AheadDistanceColor { get; set; } = "#F1F5F9";
        public string BehindDistanceColor { get; set; } = "#F1F5F9";
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
        public int RankingRowCapacity { get; set; } = MaxRankingRows;
        public IReadOnlyList<RankingRowViewModel> AllRankingRows { get; set; } = Array.Empty<RankingRowViewModel>();
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
            string? eventTimeRemainingTextOverride = null,
            int rankingRowCapacity = MaxRankingRows,
            IReadOnlyDictionary<int, float>? participantLapTimes = null)
        {
            rankingRowCapacity = LeftTowerLayoutMetrics.ClampRankingRows(rankingRowCapacity);
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
                ? gapPresenter.Present(snapshot.SplitTimeAhead, ahead)
                : new GapDisplay("—", GapSource.Unknown);
            GapDisplay behindGap = behindMatchesGameSplit
                ? gapPresenter.Present(snapshot.SplitTimeBehind, behind)
                : new GapDisplay("—", GapSource.Unknown);
            var progressDistance = new TrackProgressDistanceResolver();
            TrackProgressDistance aheadProgress = progressDistance.Resolve(snapshot.TrackLength, local, ahead);
            TrackProgressDistance behindProgress = progressDistance.Resolve(snapshot.TrackLength, local, behind);
            float last = local.LastLapTime > 0 ? local.LastLapTime : snapshot.LastLapTime;
            float best = local.BestLapTime > 0 ? local.BestLapTime : snapshot.BestLapTime;
            uint displayLap = local.CurrentLap > 0 ? local.CurrentLap : local.LapsCompleted + 1;
            string noCar = catalog.CultureName == "ko-KR" ? "차량 없음" : "NO CAR";
            IReadOnlyList<RankingRowViewModel> allRankingRows = BuildRankingRows(
                snapshot,
                league,
                local.Index,
                broadcastStates,
                league.FastestLapParticipant?.Source.Index,
                participantLapTimes);
            IReadOnlyList<RankingRowViewModel> rankingRows = SelectRankingRows(allRankingRows, rankingRowCapacity);
            bool playerPinnedAfterLeaders = league.Local?.LeaguePosition > rankingRowCapacity;
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
                AheadLapGapCandidate = aheadProgress.LapGap,
                AheadParticipantKey = ParticipantKey(ahead),
                AheadDistance = proximity.AheadDistance.Text,
                AheadParticipantIndex = ahead?.Index ?? -1,
                AheadDistanceMeters = DisplayedMeters(proximity.AheadDistance),
                AheadSource = StateText.GapSourceText(aheadGap.Source),
                AheadIndex = IndexOf(ahead),
                BehindPosition = PositionOf(behindLeague),
                BehindName = NameOf(behind, noCar),
                BehindGap = behindGap.Text,
                BehindLapGapCandidate = behindProgress.LapGap,
                BehindParticipantKey = ParticipantKey(behind),
                BehindDistance = proximity.BehindDistance.Text,
                BehindParticipantIndex = behind?.Index ?? -1,
                BehindDistanceMeters = DisplayedMeters(proximity.BehindDistance),
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
                RankingRowCapacity = rankingRowCapacity,
                AllRankingRows = allRankingRows,
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

        public void ResizeRanking(int capacity)
        {
            capacity = LeftTowerLayoutMetrics.ClampRankingRows(capacity);
            if (RankingRowCapacity == capacity) return;
            RankingRowCapacity = capacity;
            RankingRows = SelectRankingRows(AllRankingRows.Count > 0 ? AllRankingRows : RankingRows, capacity);
            bool pinned = RankingRows.Count > 1 && RankingRows[RankingRows.Count - 1].IsPlayer
                && TimingTowerTransitionTracker.ParsePosition(RankingRows[RankingRows.Count - 1].Position)
                    > TimingTowerTransitionTracker.ParsePosition(RankingRows[RankingRows.Count - 2].Position) + 1;
            RankingRangeText = RankingRows.Count == 0 ? "순위"
                : pinned ? RankingRows[0].Position + " — " + RankingRows[RankingRows.Count - 2].Position
                    + " · PLAYER " + RankingRows[RankingRows.Count - 1].Position
                : RankingRows[0].Position + " — " + RankingRows[RankingRows.Count - 1].Position;
        }

        private static IReadOnlyList<RankingRowViewModel> SelectRankingRows(
            IReadOnlyList<RankingRowViewModel> rows, int capacity)
        {
            var selected = rows.Take(capacity).ToList();
            RankingRowViewModel? player = rows.FirstOrDefault(row => row.IsPlayer);
            if (player != null && !selected.Contains(player))
            {
                selected[selected.Count - 1] = player;
            }
            return selected;
        }

        private static IReadOnlyList<RankingRowViewModel> BuildRankingRows(
            TelemetrySnapshot snapshot,
            LeagueClassification league,
            int localIndex,
            IReadOnlyDictionary<int, ParticipantBroadcastState>? broadcastStates,
            int? fastestIndex,
            IReadOnlyDictionary<int, float>? participantLapTimes)
        {
            return league.Participants
                .Select(item =>
                {
                    ParticipantRowDisplayState displayState = ParticipantRowStateResolver.Resolve(item.Source);
                    bool dimmed = ParticipantRowStateResolver.ShouldDim(displayState);
                    bool player = item.Source.Index == localIndex;
                    ClassBadgeStyle classBadge = ClassBadgePalette.Resolve(item.Source.VehicleClass);
                    return new RankingRowViewModel
                    {
                        ParticipantIndex = item.Source.Index,
                        Position = "P" + item.LeaguePosition,
                        Name = string.IsNullOrWhiteSpace(item.Source.Name) ? "—" : item.Source.Name,
                        Class = CompactClass(item.Source.VehicleClass),
                        Lap = "L" + (item.Source.CurrentLap > 0 ? item.Source.CurrentLap : item.Source.LapsCompleted + 1),
                        CurrentTime = FormatParticipantCurrentTime(
                            snapshot.KnownSessionState,
                            item.Source,
                            player && snapshot.ViewedParticipantIndex == item.Source.Index ? snapshot.CurrentTime : (float?)null,
                            participantLapTimes != null && participantLapTimes.TryGetValue(item.Source.Index, out float measured)
                                ? measured : (float?)null),
                        IsPlayer = player,
                        DisplayState = displayState,
                        IsDimmed = dimmed,
                        Background = dimmed
                            ? OverlayUiPalette.InactiveRowBackground
                            : player ? OverlayUiPalette.PlayerRowBackground : OverlayUiPalette.NormalRowBackground,
                        Accent = dimmed ? "#5D6A76" : player ? "#82F1D0" : "Transparent",
                        Foreground = dimmed
                            ? OverlayUiPalette.InactiveText
                            : player ? "#FFFFFF" : OverlayUiPalette.ActiveText,
                        ClassBackground = dimmed ? "#394652" : classBadge.Background,
                        ClassForeground = dimmed ? "#AAB4BE" : classBadge.Foreground,
                        TimeForeground = dimmed ? OverlayUiPalette.InactiveTime : OverlayUiPalette.ActiveTime,
                        Status = StatusOf(item.Source.Index, broadcastStates, fastestIndex),
                        StatusColor = StatusColorOf(item.Source.Index, broadcastStates, fastestIndex)
                    };
                })
                .ToArray();
        }

        internal static string FormatParticipantCurrentTime(
            SessionState? sessionState,
            ParticipantSnapshot participant,
            float? localCurrentTime,
            float? observedLapTime = null)
        {
            switch (participant.KnownRaceState)
            {
                case RaceState.Disqualified:
                    return "DSQ";
                case RaceState.Retired:
                    return "RET";
                case RaceState.Dnf:
                    return "DNF";
                case RaceState.Finished:
                    if (sessionState == AMS2LeagueClient.Core.Telemetry.SessionState.Practice
                        || sessionState == AMS2LeagueClient.Core.Telemetry.SessionState.Qualify
                        || sessionState == AMS2LeagueClient.Core.Telemetry.SessionState.Test
                        || sessionState == AMS2LeagueClient.Core.Telemetry.SessionState.TimeAttack)
                    {
                        return IsPositiveFinite(participant.BestLapTime)
                            ? FormatLapTime(participant.BestLapTime)
                            : "--";
                    }

                    // AMS2 does not expose a reliable official per-driver race
                    // time here. Never present the retained partial-sector sum
                    // as a final time after this participant has finished.
                    return "FIN";
            }

            if (!participant.IsActive || participant.KnownRaceState != RaceState.Racing) return "--";
            if (localCurrentTime.HasValue && IsPositiveFinite(localCurrentTime.Value))
            {
                return FormatLapTime(localCurrentTime.Value);
            }

            if (observedLapTime.HasValue && IsPositiveFinite(observedLapTime.Value))
                return "~" + FormatLapTime(observedLapTime.Value);
            // Sector arrays can contain shared race-start elapsed values, not
            // individual lap starts. Never sum them into a fabricated live lap.
            return IsPositiveFinite(participant.LastLapTime) ? "L" + FormatLapTime(participant.LastLapTime) : "--";
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

        private static int? DisplayedMeters(TrackProgressDistance distance)
        {
            if (!distance.IsAvailable || !distance.Text.EndsWith("m", StringComparison.Ordinal)) return null;
            return (int)Math.Round(Math.Abs(distance.SignedMeters), MidpointRounding.AwayFromZero);
        }

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

        private static string ParticipantKey(ParticipantSnapshot? participant)
            => participant == null
                ? string.Empty
                : participant.Index + "|" + participant.Name + "|" + participant.VehicleName + "|" + participant.VehicleClass;

        private static string IndexOf(ParticipantSnapshot? participant)
            => participant == null ? "—" : participant.Index.ToString(CultureInfo.InvariantCulture);

        private static string RawRelative(ParticipantSnapshot? participant)
            => participant == null ? "—" : "P" + participant.RacePosition + " #" + participant.Index;

        private static string LeagueRelative(LeagueParticipant? participant)
            => participant == null ? "—" : "P" + participant.LeaguePosition + " #" + participant.Source.Index;
    }
}
