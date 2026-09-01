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

    public sealed class MultiplayerWaitingOverlayViewModel
    {
        public string Title { get; set; } = "멀티플레이어 세션 대기";
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
    /// Selects the compact multiplayer waiting surface without guessing session
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

        public MultiplayerOverlayDecision Observe(TelemetrySnapshot snapshot, int sessionGeneration, DateTimeOffset now)
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
            bool multiplayer = active.Length > 1;
            bool remainingValid = IsFiniteNonNegative(snapshot.EventTimeRemaining);
            bool waitingGameState = snapshot.KnownGameState == GameState.InGameMenuTimeTicking;
            bool notStarted = snapshot.RaceStateRaw == (uint)RaceState.NotStarted
                || ViewedParticipantIsNotStarted(snapshot);
            bool endTransition = snapshot.KnownGameState == GameState.InGamePlaying
                && !remainingValid
                && notStarted;

            if (multiplayer && (waitingGameState || endTransition))
            {
                int leagueCount = active.Count(_roles.IsLeagueDriver);
                string sessionLabel = snapshot.KnownSessionState == SessionState.Invalid
                    ? "INVALID"
                    : snapshot.KnownSessionState.HasValue
                        ? OverlayTextCatalog.Korean.SessionName(snapshot.KnownSessionState)
                        : StateText.Session(snapshot.SessionStateRaw);
                return new MultiplayerOverlayDecision(
                    MultiplayerOverlayMode.Waiting,
                    waitingGameState ? "MULTIPLAYER_MENU_WAITING" : "MULTIPLAYER_SESSION_TRANSITION",
                    new MultiplayerWaitingOverlayViewModel
                    {
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
                        : "종료 처리 중");
        }

        public void Reset()
        {
            _remainingGeneration = int.MinValue;
            _lastValidRemaining = null;
            _lastValidRemainingAt = default;
        }

        private static bool ViewedParticipantIsNotStarted(TelemetrySnapshot snapshot)
        {
            int index = snapshot.ViewedParticipantIndex;
            return index >= 0
                && index < snapshot.Participants.Count
                && snapshot.Participants[index].IsActive
                && snapshot.Participants[index].KnownRaceState == RaceState.NotStarted;
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
        public const int SessionWidth = 280;
        public const int SessionHeight = 150;
        public const int WaitingWidth = 390;
        public const int WaitingHeight = 150;
        public const int RaceControlCompactWidth = 360;
        public const int RaceControlExpandedWidth = 520;
        public const int RaceControlCompactHeight = 82;
        public const int RaceControlExpandedHeight = 190;

        public static int RaceControlTopOffset
            => SessionHeight + LeftTowerLayoutMetrics.SessionGap;
    }
}
