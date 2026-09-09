using System;
using System.Globalization;
using System.Linq;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Core.Presentation
{
    public enum MultiplayerOverlayMode
    {
        Hidden,
        Gameplay,
        Waiting
    }

    // Display selection, never an authority or capture/upload policy signal.
    public enum SessionPlayMode { Unknown, SinglePlayer, Multiplayer }

    public sealed class MultiplayerWaitingOverlayViewModel
    {
        public string Title { get; set; } = "세션 대기 · 모드 미확인";
        public string SessionLabel { get; set; } = "—";
        public string ParticipantCountText { get; set; } = "리그 — / 원본 —";
        public string RemainingLabel { get; set; } = "상태";
        public string RemainingValue { get; set; } = "세션 종료 대기";
    }

    public sealed class MultiplayerOverlayDecision
    {
        public MultiplayerOverlayDecision(
            MultiplayerOverlayMode mode,
            string reason,
            MultiplayerWaitingOverlayViewModel? waiting,
            float? effectiveRemainingSeconds,
            string? remainingDisplayTextOverride)
        {
            Mode = mode;
            Reason = reason ?? string.Empty;
            Waiting = waiting;
            EffectiveRemainingSeconds = effectiveRemainingSeconds;
            RemainingDisplayTextOverride = remainingDisplayTextOverride;
        }

        public MultiplayerOverlayMode Mode { get; }
        public string Reason { get; }
        public MultiplayerWaitingOverlayViewModel? Waiting { get; }
        public float? EffectiveRemainingSeconds { get; }
        public string? RemainingDisplayTextOverride { get; }
    }

    /// <summary>
    /// Selects the compact session waiting surface without guessing session
    /// state. It also retains a valid timer for at most three seconds inside the
    /// same observed game/session generation to absorb a single transient -1.
    /// </summary>
    public sealed class MultiplayerWaitingOverlayController
    {
        public static readonly TimeSpan RemainingFallbackDuration = TimeSpan.FromSeconds(3);

        private readonly ParticipantRoleClassifier _roles = new ParticipantRoleClassifier();
        private int _remainingGeneration = int.MinValue;
        private float? _lastValidRemaining;
        private DateTimeOffset _lastValidRemainingAt;

        public MultiplayerOverlayDecision Observe(TelemetrySnapshot snapshot, int sessionGeneration, DateTimeOffset now,
            SessionPlayMode playMode = SessionPlayMode.Unknown)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            if (_remainingGeneration != sessionGeneration)
            {
                _remainingGeneration = sessionGeneration;
                _lastValidRemaining = null;
                _lastValidRemainingAt = default;
            }

            int rawParticipantCount = Math.Max(0, Math.Min(snapshot.NumParticipants, snapshot.Participants.Count));
            ParticipantSnapshot[] active = snapshot.Participants
                .Take(rawParticipantCount)
                .Where(item => item.IsActive)
                .ToArray();
            bool remainingValid = IsFiniteNonNegative(snapshot.EventTimeRemaining);
            bool waitingGameState = snapshot.KnownGameState == GameState.InGameMenuTimeTicking;
            // An absent timer/NotStarted also occurs during unlimited driving.
            // Only the game's menu state selects the waiting surface.
            if (active.Length > 0 && waitingGameState)
            {
                int leagueCount = active.Count(_roles.IsLeagueDriver);
                string sessionLabel = snapshot.KnownSessionState switch
                {
                    SessionState.Practice => "자유 연습 주행",
                    SessionState.Test => "테스트 주행",
                    SessionState.Invalid => "세션 전환 중",
                    null => "세션 미확인",
                    _ => OverlayTextCatalog.Korean.SessionName(snapshot.KnownSessionState)
                };
                return new MultiplayerOverlayDecision(
                    MultiplayerOverlayMode.Waiting,
                    "SESSION_MENU_WAITING",
                    new MultiplayerWaitingOverlayViewModel
                    {
                        // SHM v14 has no authoritative online flag. AI count and privacy are not mode evidence.
                        Title = playMode switch
                        {
                            SessionPlayMode.SinglePlayer => "싱글플레이어 세션 대기",
                            SessionPlayMode.Multiplayer => "멀티플레이어 세션 대기",
                            _ => "세션 대기 · 모드 미확인"
                        },
                        SessionLabel = sessionLabel,
                        ParticipantCountText = "리그 " + leagueCount.ToString(CultureInfo.InvariantCulture)
                            + " / 원본 " + rawParticipantCount.ToString(CultureInfo.InvariantCulture),
                        RemainingLabel = remainingValid ? "남은 시간" : "상태",
                        RemainingValue = remainingValid
                            ? OverlayViewModel.FormatRemainingTime(snapshot.EventTimeRemaining)
                            : HasTerminalRaceState(snapshot)
                                ? "세션 종료"
                                : "세션 종료 대기"
                    },
                    remainingValid ? (float?)snapshot.EventTimeRemaining : null,
                    null);
            }

            if (snapshot.KnownGameState != GameState.InGamePlaying)
            {
                return new MultiplayerOverlayDecision(MultiplayerOverlayMode.Hidden, "NON_GAMEPLAY_STATE", null, null, null);
            }

            if (remainingValid)
            {
                _lastValidRemaining = snapshot.EventTimeRemaining;
                _lastValidRemainingAt = now;
                return new MultiplayerOverlayDecision(MultiplayerOverlayMode.Gameplay, "GAMEPLAY", null, snapshot.EventTimeRemaining, null);
            }

            bool canUseFallback = _lastValidRemaining.HasValue
                && now >= _lastValidRemainingAt
                && now - _lastValidRemainingAt <= RemainingFallbackDuration;
            return new MultiplayerOverlayDecision(
                MultiplayerOverlayMode.Gameplay,
                canUseFallback ? "GAMEPLAY_TIMER_TRANSIENT" : "GAMEPLAY",
                null,
                canUseFallback ? _lastValidRemaining : null,
                canUseFallback
                    ? null
                    : HasTerminalRaceState(snapshot)
                        ? "세션 종료"
                        : "—");
        }

        public void Reset()
        {
            _remainingGeneration = int.MinValue;
            _lastValidRemaining = null;
            _lastValidRemainingAt = default;
        }

        private static bool HasTerminalRaceState(TelemetrySnapshot snapshot)
        {
            if (IsTerminal(snapshot.RaceStateRaw)) return true;
            int index = snapshot.ViewedParticipantIndex;
            return index >= 0
                && index < snapshot.Participants.Count
                && snapshot.Participants[index].IsActive
                && IsTerminal(snapshot.Participants[index].RaceStateRaw);
        }

        private static bool IsTerminal(uint raw)
            => raw == (uint)RaceState.Finished
                || raw == (uint)RaceState.Retired
                || raw == (uint)RaceState.Dnf;

        private static bool IsFiniteNonNegative(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
    }

    public static class AuxiliaryOverlayLayoutMetrics
    {
        public const int SessionWidth = OverlayUiMetrics.SessionWidth;
        public const int SessionHeight = OverlayUiMetrics.SessionHeight;
        public const int WaitingWidth = OverlayUiMetrics.WaitingWidth;
        public const int WaitingHeight = OverlayUiMetrics.WaitingHeight;
        public const int RaceControlCompactWidth = OverlayUiMetrics.RaceControlCompactWidth;
        public const int RaceControlExpandedWidth = OverlayUiMetrics.RaceControlExpandedWidth;
        public const int RaceControlCompactHeight = OverlayUiMetrics.RaceControlCompactHeight;
        public const int RaceControlExpandedHeight = OverlayUiMetrics.RaceControlExpandedHeight;
        public const int EventWidth = OverlayUiMetrics.EventWidth;
        public const int EventHeight = OverlayUiMetrics.EventHeight;
        public const int RelativeWidth = OverlayUiMetrics.RelativeWidth;
        public const int RelativeHeight = OverlayUiMetrics.RelativeHeight;
        public const int LapTimingWidth = OverlayUiMetrics.LapTimingWidth;
        public const int LapTimingHeight = OverlayUiMetrics.LapTimingHeight;

        public static int RaceControlTopOffset
            => SessionHeight + LapTimingHeight + (LeftTowerLayoutMetrics.SessionGap * 2);
    }
}
