using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Diagnostics;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Localization;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.RaceControl;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Core.Session;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Runtime
{
    public sealed class PlayerOverlayCoordinator : IDisposable
    {
        private readonly OverlayWindow _overlay;
        private readonly ClientStatusViewModel _status;
        private readonly FileLogger _logger;
        private readonly bool _diagnostic;
        private readonly Ams2ProcessMonitor _processMonitor = new Ams2ProcessMonitor();
        private readonly GameWindowTracker _windowTracker = new GameWindowTracker();
        private readonly SharedMemoryReader _reader = new SharedMemoryReader();
        private readonly LocalParticipantResolver _localResolver = new LocalParticipantResolver();
        private readonly LeagueClassificationResolver _leagueResolver = new LeagueClassificationResolver();
        private readonly RaceEventEngine _eventEngine = new RaceEventEngine();
        private readonly RaceControlAnalyzer _raceControlAnalyzer = new RaceControlAnalyzer(EvidenceKind.Live);
        private readonly SessionStateTracker _sessionTracker = new SessionStateTracker();
        private readonly OverlayVisibilityController _visibilityController = new OverlayVisibilityController();
        private readonly object _readerGate = new object();
        private readonly DispatcherTimer _processTimer;
        private readonly DispatcherTimer _uiTimer;
        private readonly Stopwatch _uiCadenceClock = Stopwatch.StartNew();
        private readonly Stopwatch _rateClock = Stopwatch.StartNew();
        private readonly Stopwatch _performanceClock = Stopwatch.StartNew();
        private static readonly long UiCadenceTicks = (long)(Stopwatch.Frequency / 20.0);
        private long _nextUiDueTicks = UiCadenceTicks;
        private System.Threading.Timer? _telemetryTimer;
        private TelemetrySnapshot? _latest;
        private int _processId = -1;
        private string _processName = string.Empty;
        private TelemetryReadStatus _lastReadStatus = TelemetryReadStatus.MappingUnavailable;
        private string _lastReadMessage = string.Empty;
        private int _successCount;
        private int _uiUpdateCount;
        private double _snapshotRate;
        private double _uiRate;
        private string _lastVisibilityReason = string.Empty;
        private string _lastWindowKey = string.Empty;
        private string _lastRect = string.Empty;
        private bool? _lastForeground;
        private bool? _lastMinimized;
        private uint? _lastDpi;
        private long? _lastMonitor;
        private string _lastPresentationKey = string.Empty;
        private string _lastInvalidSplitKey = string.Empty;
        private int _lastParticipantCount = -1;
        private int _lastViewedIndex = int.MinValue;
        private string _lastRelativeKey = string.Empty;
        private string _lastEventId = string.Empty;
        private DateTimeOffset _lastInconsistentWarningAt = DateTimeOffset.MinValue;
        private bool _sharedMemoryAttached;
        private readonly SequenceCounterSampler _sequenceCounterSampler = new SequenceCounterSampler(TimeSpan.FromSeconds(30));
        private bool _styleLogged;
        private TimeSpan _lastCpuTime;
        private DateTimeOffset _lastPerformanceAt = DateTimeOffset.UtcNow;
        private bool _disposed;

        public PlayerOverlayCoordinator(
            OverlayWindow overlay,
            ClientStatusViewModel status,
            FileLogger logger,
            bool diagnostic)
        {
            _overlay = overlay;
            _status = status;
            _logger = logger;
            _diagnostic = diagnostic;
            _processTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _processTimer.Tick += ProcessTick;

            _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                // Poll the dispatcher faster than the target cadence. The absolute
                // deadline gate in UiTick keeps rendering at no more than 20 Hz while
                // avoiding 50 ms ticks quantizing to ~15 Hz under normal WPF jitter.
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _uiTimer.Tick += UiTick;
        }

        public void Start()
        {
            _status.SetWaiting();
            _logger.Info("CLIENT_START", "mode=REAL_CLIENT readOnly=true shmRate=30Hz uiMaxRate=20Hz overlayWindows=BOUNDED_MULTI_HWND diagnostic=" + _diagnostic);
            _processTimer.Start();
            _uiTimer.Start();
            _telemetryTimer = new System.Threading.Timer(ReadTelemetry, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33.333));
            ProcessTick(this, EventArgs.Empty);
        }

        private void ProcessTick(object? sender, EventArgs eventArgs)
        {
            try
            {
                Ams2ProcessInfo? process = _processMonitor.FindRunningProcess();
                if (process == null)
                {
                    if (Volatile.Read(ref _processId) != -1)
                    {
                        Detach("process exited");
                    }

                    _status.SetWaiting();
                    return;
                }

                int previous = Volatile.Read(ref _processId);
                if (previous != process.ProcessId)
                {
                    if (previous != -1)
                    {
                        Detach("process replaced");
                    }

                    _processName = process.ProcessName;
                    Volatile.Write(ref _processId, process.ProcessId);
                    _logger.Info("AMS2_ATTACH", "process=" + _processName + " pid=" + process.ProcessId);
                    _status.ProcessText = "AMS2 프로세스: 연결됨 (PID " + process.ProcessId + ")";
                    _status.Message = "AMS2 감지됨. 읽기 전용 공유 메모리를 기다리는 중입니다...";
                }
            }
            catch (Exception exception)
            {
                _logger.Error("PROCESS_MONITOR_EXCEPTION", exception);
            }
        }

        private void ReadTelemetry(object? state)
        {
            if (Volatile.Read(ref _processId) < 0 || _disposed)
            {
                return;
            }

            try
            {
                TelemetryReadResult result;
                lock (_readerGate)
                {
                    result = _reader.TryRead();
                }

                TelemetryReadStatus previousStatus = _lastReadStatus;
                _lastReadStatus = result.Status;
                _lastReadMessage = result.Message;

                if (result.Status == TelemetryReadStatus.Success && result.Snapshot != null)
                {
                    TelemetrySnapshot snapshot = result.Snapshot;
                    Interlocked.Exchange(ref _latest, snapshot);
                    Interlocked.Increment(ref _successCount);

                    if (!_sharedMemoryAttached)
                    {
                        _sharedMemoryAttached = true;
                        _logger.Info("SHM_ATTACH", "mapping=$pcars2$ access=READ version=" + snapshot.Version + " build=" + snapshot.BuildVersion);
                    }

                    if (_sessionTracker.Observe(snapshot))
                    {
                        _lastPresentationKey = string.Empty;
                        _logger.Info("SESSION_TRANSITION", "game=" + StateText.Game(snapshot.GameStateRaw) + " session=" + StateText.Session(snapshot.SessionStateRaw) + " cacheReset=true generation=" + _sessionTracker.Generation);
                    }

                    if (snapshot.NumParticipants != _lastParticipantCount)
                    {
                        _lastParticipantCount = snapshot.NumParticipants;
                        _logger.Info("PARTICIPANT_COUNT", "count=" + snapshot.NumParticipants);
                    }

                    if (snapshot.ViewedParticipantIndex != _lastViewedIndex)
                    {
                        _lastViewedIndex = snapshot.ViewedParticipantIndex;
                        _logger.Info("VIEWED_PARTICIPANT", "index=" + snapshot.ViewedParticipantIndex);
                    }

                    string invalidSplitKey = (snapshot.SplitTimeAhead < 0 ? "A" : string.Empty)
                        + (snapshot.SplitTimeBehind < 0 ? "B" : string.Empty);
                    if (invalidSplitKey.Length > 0 && invalidSplitKey != _lastInvalidSplitKey)
                    {
                        _logger.Warning("INVALID_SPLIT", "ahead=" + FormatFloat(snapshot.SplitTimeAhead) + " behind=" + FormatFloat(snapshot.SplitTimeBehind) + " policy=UNKNOWN");
                    }
                    else if (invalidSplitKey.Length == 0 && _lastInvalidSplitKey.Length > 0)
                    {
                        _logger.Info("SPLIT_VALID", "source=GAME_SPLIT");
                    }

                    _lastInvalidSplitKey = invalidSplitKey;
                }
                else if (result.Status == TelemetryReadStatus.InconsistentSnapshot)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (now - _lastInconsistentWarningAt >= TimeSpan.FromSeconds(30))
                    {
                        _lastInconsistentWarningAt = now;
                        _logger.Warning("SHM_STATE", "status=" + result.Status + " message=" + result.Message + " rateLimit=30s");
                    }
                }
                else if (previousStatus != result.Status)
                {
                    _logger.Warning("SHM_STATE", "status=" + result.Status + " message=" + result.Message);
                }

                SequenceCounterSample? sequenceSample = _sequenceCounterSampler.Observe(
                    DateTimeOffset.UtcNow,
                    _reader.SequenceRetries,
                    _reader.SequenceDrops);
                if (sequenceSample != null)
                {
                    _logger.Warning(
                        "SEQUENCE_CONSISTENCY",
                        "retries=" + sequenceSample.Retries
                        + " drops=" + sequenceSample.Drops
                        + " retryDelta=" + sequenceSample.RetryDelta
                        + " dropDelta=" + sequenceSample.DropDelta
                        + " sampling=30s");
                }
            }
            catch (Exception exception)
            {
                _lastReadStatus = TelemetryReadStatus.Error;
                _lastReadMessage = exception.Message;
                _logger.Error("SHM_READ_EXCEPTION", exception);
            }
        }

        private void UiTick(object? sender, EventArgs eventArgs)
        {
            long nowTicks = _uiCadenceClock.ElapsedTicks;
            if (nowTicks < _nextUiDueTicks)
            {
                return;
            }

            do
            {
                _nextUiDueTicks += UiCadenceTicks;
            }
            while (_nextUiDueTicks <= nowTicks);

            try
            {
                UpdateRates();
                int pid = Volatile.Read(ref _processId);
                if (pid < 0)
                {
                    ApplyVisibility(new OverlayVisibilityDecision(false, "WAIT_PROCESS"), null, null, null);
                    return;
                }

                GameWindowSnapshot? window = _windowTracker.TryGetWindow(pid);
                LogWindowChanges(window);

                TelemetrySnapshot? snapshot = Volatile.Read(ref _latest);
                LocalParticipantResolution? local = snapshot == null ? null : _localResolver.Resolve(snapshot);
                bool gameplayValid = snapshot != null && local != null && local.IsValid && local.Participant != null;
                OverlayVisibilityDecision decision = _visibilityController.Evaluate(true, window, gameplayValid);
                ApplyVisibility(decision, window, snapshot, local);

                if (_lastReadStatus == TelemetryReadStatus.MappingUnavailable)
                {
                    _status.SetSharedMemoryUnavailable(pid);
                }
                else if (snapshot != null)
                {
                    string windowText = window == null
                        ? "게임 창: 대기 중"
                        : "게임 창: " + window.RectKey + " · DPI " + window.Dpi + " · " + (window.IsForeground ? "전면" : "후면");
                    _status.SetAttached(pid, snapshot.Version, snapshot.BuildVersion, windowText);
                }
                else if (_lastReadStatus == TelemetryReadStatus.UnsupportedVersion || _lastReadStatus == TelemetryReadStatus.InvalidData || _lastReadStatus == TelemetryReadStatus.Error)
                {
                    _status.StateLabel = "텔레메트리 오류";
                    _status.Message = _lastReadMessage;
                    _status.AccentColor = "#FF6B6B";
                }

                SamplePerformance();
            }
            catch (Exception exception)
            {
                _overlay.HideOverlay();
                _logger.Error("UI_TICK_EXCEPTION", exception);
            }
        }

        private void ApplyVisibility(
            OverlayVisibilityDecision decision,
            GameWindowSnapshot? window,
            TelemetrySnapshot? snapshot,
            LocalParticipantResolution? local)
        {
            if (decision.Reason != _lastVisibilityReason)
            {
                _logger.Info(decision.ShouldShow ? "OVERLAY_SHOW" : "OVERLAY_HIDE", "reason=" + decision.Reason);
                _lastVisibilityReason = decision.Reason;
            }

            if (!decision.ShouldShow || window == null || snapshot == null || local?.Participant == null)
            {
                _overlay.HideOverlay();
                return;
            }

            LeagueClassification league = _leagueResolver.Resolve(snapshot, local.Participant);
            if (!league.IsLocalEligible)
            {
                _overlay.HideOverlay();
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            RaceControlUpdate raceControlUpdate = _raceControlAnalyzer.Observe(snapshot, league, _sessionTracker.Generation, now);
            if (raceControlUpdate.StateReset)
            {
                _logger.Info(
                    "RACE_CONTROL_BASELINE",
                    "sessionGeneration=" + _sessionTracker.Generation
                    + " rootFlagColour=" + snapshot.HighestFlagColourRaw
                    + " rootFlagReason=" + snapshot.HighestFlagReasonRaw
                    + " rootPitMode=" + snapshot.RootPitModeRaw
                    + " rootPitSchedule=" + snapshot.RootPitScheduleRaw
                    + " localIndex=" + local.Participant.Index
                    + " localRaceState=" + local.Participant.RaceStateRaw
                    + " localPitMode=" + local.Participant.PitModeRaw
                    + " localPitSchedule=" + local.Participant.PitScheduleRaw
                    + " localFlagColour=" + local.Participant.HighestFlagColourRaw
                    + " localFlagReason=" + local.Participant.HighestFlagReasonRaw
                    + " leagueCount=" + league.LeagueParticipantCount
                    + " safetyCarsExcluded=" + league.SafetyCarsExcluded
                    + " evidence=Live confidence=ConfirmedLive");
            }
            foreach (RaceControlEvent detected in raceControlUpdate.DetectedEvents)
            {
                _logger.Info(
                    "RACE_CONTROL_DETECTED",
                    "type=" + detected.Type
                    + " priority=" + detected.Priority
                    + " participant=" + detected.ParticipantIndex
                    + " generation=" + detected.ParticipantGeneration
                    + " leaguePosition=" + detected.LeaguePosition
                    + " raw=" + detected.RawEnum
                    + " derived=" + detected.DerivedState
                    + " source=" + detected.Source
                    + " evidence=" + detected.EvidenceKind
                    + " confidence=" + detected.Confidence);
            }

            RaceEventUpdate eventUpdate = _eventEngine.Observe(snapshot, league, _sessionTracker.Generation, now, raceControlUpdate.OverlayState);
            foreach (OverlayEvent detected in eventUpdate.DetectedEvents)
            {
                _logger.Info("EVENT_DETECTED", "type=" + detected.Type + " priority=" + detected.Priority + " source=" + detected.SourceKind);
                _logger.Info("EVENT_QUEUE_ENTERED", "id=" + detected.Id + " waiting=" + eventUpdate.QueuedCount);
            }

            string eventId = eventUpdate.CurrentEvent?.Id ?? string.Empty;
            if (eventId != _lastEventId)
            {
                if (_lastEventId.Length > 0)
                {
                    _logger.Info("EVENT_ANIMATION_HIDE_START", "id=" + _lastEventId);
                    _logger.Info("EVENT_DISPOSED", "id=" + _lastEventId);
                }
                if (eventUpdate.CurrentEvent != null)
                {
                    _logger.Info("EVENT_ANIMATION_SHOW_START", "id=" + eventId + " type=" + eventUpdate.CurrentEvent.Type);
                    _logger.Info("EVENT_HOLD", "id=" + eventId + " durationMs=" + eventUpdate.CurrentEvent.DisplayDuration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture));
                }
                _lastEventId = eventId;
            }

            string relativeKey = (league.Ahead?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "-") + "/" + (league.Behind?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "-");
            if (relativeKey != _lastRelativeKey)
            {
                _lastRelativeKey = relativeKey;
                _logger.Info("RELATIVE_CHANGE", "aheadIndex=" + (league.Ahead?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "none") + " behindIndex=" + (league.Behind?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "none") + " rawCount=" + league.RawParticipantCount + " leagueCount=" + league.LeagueParticipantCount + " safetyCarsExcluded=" + league.SafetyCarsExcluded);
            }

            string presentationKey = BuildPresentationKey(snapshot, local.Participant, league, eventUpdate.CurrentEvent, eventUpdate.QueuedCount, raceControlUpdate);
            if (_diagnostic)
            {
                presentationKey += "|rates=" + _snapshotRate.ToString("0.0", CultureInfo.InvariantCulture) + "/" + _uiRate.ToString("0.0", CultureInfo.InvariantCulture);
            }

            if (presentationKey != _lastPresentationKey)
            {
                _lastPresentationKey = presentationKey;
                OverlayViewModel timing = OverlayViewModel.Build(
                    snapshot,
                    local.Participant,
                    league,
                    _snapshotRate,
                    _uiRate,
                    _diagnostic,
                    OverlayTextCatalog.Korean.Get(OverlayTextKey.RealReadOnly),
                    eventUpdate.CurrentEvent,
                    eventUpdate.QueuedCount,
                    broadcastStates: raceControlUpdate.ParticipantStates,
                    raceControl: raceControlUpdate);
                _overlay.SetViewModel(OverlayShellViewModel.Build(snapshot, timing, eventUpdate.CurrentEvent, false, raceControl: raceControlUpdate));
                Interlocked.Increment(ref _uiUpdateCount);
            }

            _overlay.ShowAt(window);
            if (!_styleLogged)
            {
                _styleLogged = true;
                _logger.Info("OVERLAY_STYLES", _overlay.GetStyleState().ToString());
            }
        }

        private void LogWindowChanges(GameWindowSnapshot? window)
        {
            string key = window == null
                ? "none"
                : window.RectKey + "/dpi=" + window.Dpi + "/monitor=" + window.MonitorHandle + "/fg=" + window.IsForeground + "/min=" + window.IsMinimized;
            if (key == _lastWindowKey)
            {
                return;
            }

            _lastWindowKey = key;
            _logger.Info("GAME_WINDOW", key);
            if (window == null)
            {
                _lastRect = string.Empty;
                _lastForeground = null;
                _lastMinimized = null;
                _lastDpi = null;
                _lastMonitor = null;
                return;
            }

            if (window.RectKey != _lastRect)
            {
                _lastRect = window.RectKey;
                _logger.Info("CLIENT_RECT_CHANGE", "rect=" + window.RectKey);
            }

            if (_lastForeground != window.IsForeground)
            {
                _lastForeground = window.IsForeground;
                _logger.Info("FOREGROUND_CHANGE", "foreground=" + window.IsForeground);
            }

            if (_lastMinimized != window.IsMinimized)
            {
                _lastMinimized = window.IsMinimized;
                _logger.Info("MINIMIZED_CHANGE", "minimized=" + window.IsMinimized);
            }

            if (_lastDpi != window.Dpi)
            {
                _lastDpi = window.Dpi;
                _logger.Info("DPI_CHANGE", "dpi=" + window.Dpi);
            }

            if (_lastMonitor != window.MonitorHandle)
            {
                _lastMonitor = window.MonitorHandle;
                _logger.Info("MONITOR_CHANGE", "monitor=" + window.MonitorHandle);
            }
        }

        private void UpdateRates()
        {
            double elapsed = _rateClock.Elapsed.TotalSeconds;
            if (elapsed < 1.0)
            {
                return;
            }

            _snapshotRate = Interlocked.Exchange(ref _successCount, 0) / elapsed;
            _uiRate = Interlocked.Exchange(ref _uiUpdateCount, 0) / elapsed;
            _rateClock.Restart();
        }

        private void SamplePerformance()
        {
            if (_performanceClock.Elapsed < TimeSpan.FromSeconds(10))
            {
                return;
            }

            using (System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess())
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                TimeSpan cpu = process.TotalProcessorTime;
                double wallMs = (now - _lastPerformanceAt).TotalMilliseconds;
                double cpuPercent = wallMs <= 0
                    ? 0
                    : (cpu - _lastCpuTime).TotalMilliseconds / (wallMs * Environment.ProcessorCount) * 100.0;
                int animationWindows = (_eventEngine.Queue.Current == null ? 0 : 1)
                    + (_raceControlAnalyzer.History.Items.Count == 0 ? 0 : 1);
                _logger.Info("PERFORMANCE", "cpu=" + cpuPercent.ToString("0.000", CultureInfo.InvariantCulture)
                    + "% ramMB=" + (process.WorkingSet64 / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)
                    + " shmHz=" + _snapshotRate.ToString("0.0", CultureInfo.InvariantCulture)
                    + " uiHz=" + _uiRate.ToString("0.0", CultureInfo.InvariantCulture)
                    + " queue=" + _eventEngine.Queue.WaitingCount
                    + " animationWindows=" + animationWindows);
                _lastCpuTime = cpu;
                _lastPerformanceAt = now;
            }

            _performanceClock.Restart();
        }

        private void Detach(string reason)
        {
            int oldPid = Interlocked.Exchange(ref _processId, -1);
            lock (_readerGate)
            {
                _reader.Reset();
                _sessionTracker.Reset();
            }

            Interlocked.Exchange(ref _latest, null);
            _lastReadStatus = TelemetryReadStatus.MappingUnavailable;
            _sharedMemoryAttached = false;
            _lastPresentationKey = string.Empty;
            _lastWindowKey = string.Empty;
            _lastInvalidSplitKey = string.Empty;
            _lastEventId = string.Empty;
            _eventEngine.Reset();
            _raceControlAnalyzer.Reset();
            _overlay.HideOverlay();
            _logger.Info("AMS2_DETACH", "pid=" + oldPid + " reason=" + reason + " reattach=WAIT");
        }

        private static string BuildPresentationKey(
            TelemetrySnapshot snapshot,
            ParticipantSnapshot local,
            LeagueClassification league,
            OverlayEvent? currentEvent,
            int queuedEvents,
            RaceControlUpdate raceControl)
        {
            return snapshot.GameStateRaw + "|" + snapshot.SessionStateRaw + "|" + snapshot.NumParticipants + "|" + league.LeagueParticipantCount + "|" + league.SafetyCarsExcluded + "|"
                + local.Index + "|" + local.RacePosition + "|" + local.CurrentLap + "|" + local.LapsCompleted + "|"
                + (league.Local?.LeaguePosition ?? 0) + "|" + FormatFloat(local.LastLapTime) + "|" + FormatFloat(local.BestLapTime) + "|"
                + FormatFloat(snapshot.CurrentTime) + "|" + FormatFloat(local.CurrentSector1Time) + "|" + FormatFloat(local.CurrentSector2Time) + "|" + FormatFloat(local.CurrentSector3Time) + "|" + local.CurrentSector + "|" + local.LapInvalidated + "|" + snapshot.LapInvalidated + "|"
                + FormatFloat(snapshot.EventTimeRemaining) + "|" + FormatFloat(snapshot.SessionDuration) + "|" + snapshot.SessionAdditionalLaps + "|" + FormatFloat(snapshot.TrackLength) + "|" + FormatFloat(local.CurrentLapDistance) + "|"
                + snapshot.HighestFlagColourRaw + "|" + snapshot.HighestFlagReasonRaw + "|" + raceControl.Version + "|" + raceControl.OverlayState + "|" + (raceControl.ActiveEvent?.Id ?? "-") + "|"
                + FormatFloat(snapshot.SplitTimeAhead) + "|" + FormatFloat(snapshot.SplitTimeBehind) + "|"
                + (league.Ahead?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "-") + "|"
                + (league.Ahead == null ? "-" : FormatFloat(league.Ahead.Source.CurrentLapDistance)) + "|"
                + (league.Behind?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "-") + "|"
                + (league.Behind == null ? "-" : FormatFloat(league.Behind.Source.CurrentLapDistance)) + "|"
                + (currentEvent?.Id ?? "-") + "|" + queuedEvents;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _processTimer.Stop();
            _uiTimer.Stop();
            _telemetryTimer?.Dispose();
            lock (_readerGate)
            {
                _reader.Dispose();
            }
            _overlay.HideOverlay();
            _logger.Info("CLIENT_STOP", "clean=true");
        }
    }
}
