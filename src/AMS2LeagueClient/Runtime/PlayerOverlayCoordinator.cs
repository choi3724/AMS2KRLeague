using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media;
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
    public sealed class PlayerOverlayCoordinator : IDisposable, IAsyncDisposable
    {
        private readonly OverlayWindow _overlay;
        private readonly ClientStatusViewModel _status;
        private readonly FileLogger _logger;
        private readonly bool _diagnostic;
        private readonly ActivityCaptureRuntime? _activityCapture;
        private readonly Ams2ProcessMonitor _processMonitor = new Ams2ProcessMonitor();
        private readonly GameWindowTracker _windowTracker = new GameWindowTracker();
        private readonly SharedMemoryReader _reader = new SharedMemoryReader();
        private readonly LocalParticipantResolver _localResolver = new LocalParticipantResolver();
        private readonly LeagueClassificationResolver _leagueResolver = new LeagueClassificationResolver();
        private readonly RaceEventEngine _eventEngine = new RaceEventEngine();
        private readonly RaceControlAnalyzer _raceControlAnalyzer = new RaceControlAnalyzer(EvidenceKind.Live);
        private readonly SessionStateTracker _sessionTracker = new SessionStateTracker();
        private readonly OverlayVisibilityController _visibilityController = new OverlayVisibilityController();
        private readonly MultiplayerWaitingOverlayController _multiplayerOverlayController = new MultiplayerWaitingOverlayController();
        private readonly RelativeDistanceTrendTracker _relativeDistanceTrendTracker = new RelativeDistanceTrendTracker();
        private readonly InvalidLapDisplayTracker _invalidLapDisplayTracker = new InvalidLapDisplayTracker();
        private readonly object _readerGate = new object();
        private readonly object _telemetryGate = new object();
        private readonly DispatcherTimer _processTimer;
        private readonly DispatcherTimer _uiTimer;
        private int _drivingLocalIndex = -1, _drivingUpdateCount;
        // WPF callbacks are not GPU presents. Keep this separate from data/update rates.
        private long _lastRenderTicks, _renderGapTicks, _renderMaxGapTicks, _drivingMaxWorkTicks;
        private int _renderIntervals, _renderOverBudget, _drivingBusy, _drivingRejected;
        private TimeSpan _lastRenderTime = TimeSpan.MinValue;
        private DateTimeOffset _nextSpeedDiagnosticAt;
        private double _drivingRate;
        private DateTimeOffset _lastDrivingDataAt = DateTimeOffset.MinValue;
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
        private volatile bool _disposed;
        private Task _detachTask = Task.CompletedTask;
        private Task? _shutdownTask;

        public PlayerOverlayCoordinator(
            OverlayWindow overlay,
            ClientStatusViewModel status,
            FileLogger logger,
            bool diagnostic,
            ActivityCaptureRuntime? activityCapture = null)
        {
            _overlay = overlay;
            _status = status;
            _logger = logger;
            _diagnostic = diagnostic;
            _activityCapture = activityCapture;
            _processTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _processTimer.Tick += ProcessTick;

            // Telemetry projection is 20 Hz data work, not animation rendering;
            // keep it below WPF's compositor/render priority so motion stays smooth.
            _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
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
            _logger.Info("CLIENT_START", "mode=REAL_CLIENT readOnly=true shmRate=30Hz uiMaxRate=20Hz drivingReadCadence=EACH_RENDER_FRAME recordingReadRate=30Hz overlayWindows=BOUNDED_MULTI_HWND diagnostic=" + _diagnostic);
            _overlay.DrivingTelemetryDemandChanged += UpdateDrivingSubscription;
            UpdateDrivingSubscription(this, EventArgs.Empty);
            _processTimer.Start();
            _uiTimer.Start();
            _telemetryTimer = new System.Threading.Timer(ReadTelemetry, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(33.333));
            ProcessTick(this, EventArgs.Empty);
        }

        private bool _drivingSubscribed;
        private void UpdateDrivingSubscription(object? sender, EventArgs args)
        {
            bool active = !_disposed && _overlay.WantsDrivingTelemetry && Volatile.Read(ref _processId) > 0;
            if (active == _drivingSubscribed) return;
            _drivingSubscribed = active;
            if (active) CompositionTarget.Rendering += DrivingFrame;
            else { CompositionTarget.Rendering -= DrivingFrame; _lastRenderTicks = 0; _lastRenderTime = TimeSpan.MinValue; }
        }

        private void ProcessTick(object? sender, EventArgs eventArgs)
        {
            try
            {
                if (!_detachTask.IsCompleted) return; // Drain the old capture before accepting a new process.
                if (_detachTask.IsFaulted) { _detachTask.GetAwaiter().GetResult(); return; }
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
                        return;
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
            bool telemetryGateEntered = false;
            try
            {
                if (Volatile.Read(ref _processId) < 0 || _disposed)
                {
                    return;
                }
                telemetryGateEntered = Monitor.TryEnter(_telemetryGate);
                if (!telemetryGateEntered)
                {
                    // Shared memory is sampled as latest-state data. Dropping a
                    // busy tick is safer than allowing out-of-order callbacks.
                    return;
                }
                if (Volatile.Read(ref _processId) < 0 || _disposed)
                {
                    return;
                }

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
                    if (snapshot.CapturedAt >= _nextSpeedDiagnosticAt)
                    {
                        string? anomaly = DescribeSpeedAnomaly(snapshot, _sessionTracker.Generation);
                        if (anomaly != null)
                        { QueueTelemetryWarning("SHM_SPEED_RANGE", anomaly); _nextSpeedDiagnosticAt = snapshot.CapturedAt.AddSeconds(30); }
                    }
                    Interlocked.Exchange(ref _latest, snapshot);
                    Interlocked.Increment(ref _successCount);
                    _activityCapture?.Observe(snapshot);

                    if (!_sharedMemoryAttached)
                    {
                        _sharedMemoryAttached = true;
                        QueueTelemetryInfo("SHM_ATTACH", "mapping=$pcars2$ access=READ version=" + snapshot.Version + " build=" + snapshot.BuildVersion);
                    }

                    if (_sessionTracker.Observe(snapshot))
                    {
                        _lastPresentationKey = string.Empty;
                        QueueTelemetryInfo("SESSION_TRANSITION", "game=" + StateText.Game(snapshot.GameStateRaw) + " session=" + StateText.Session(snapshot.SessionStateRaw) + " cacheReset=true generation=" + _sessionTracker.Generation);
                    }

                    if (snapshot.NumParticipants != _lastParticipantCount)
                    {
                        _lastParticipantCount = snapshot.NumParticipants;
                        QueueTelemetryInfo("PARTICIPANT_COUNT", "count=" + snapshot.NumParticipants);
                    }

                    if (snapshot.ViewedParticipantIndex != _lastViewedIndex)
                    {
                        _lastViewedIndex = snapshot.ViewedParticipantIndex;
                        QueueTelemetryInfo("VIEWED_PARTICIPANT", "index=" + snapshot.ViewedParticipantIndex);
                    }

                    string invalidSplitKey = (snapshot.SplitTimeAhead < 0 ? "A" : string.Empty)
                        + (snapshot.SplitTimeBehind < 0 ? "B" : string.Empty);
                    if (invalidSplitKey.Length > 0 && invalidSplitKey != _lastInvalidSplitKey)
                    {
                        QueueTelemetryWarning("INVALID_SPLIT", "ahead=" + FormatFloat(snapshot.SplitTimeAhead) + " behind=" + FormatFloat(snapshot.SplitTimeBehind) + " policy=UNKNOWN");
                    }
                    else if (invalidSplitKey.Length == 0 && _lastInvalidSplitKey.Length > 0)
                    {
                        QueueTelemetryInfo("SPLIT_VALID", "source=GAME_SPLIT");
                    }

                    _lastInvalidSplitKey = invalidSplitKey;
                }
                else if (result.Status == TelemetryReadStatus.InconsistentSnapshot)
                {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (now - _lastInconsistentWarningAt >= TimeSpan.FromSeconds(30))
                    {
                        _lastInconsistentWarningAt = now;
                        QueueTelemetryWarning("SHM_STATE", "status=" + result.Status + " message=" + result.Message + " rateLimit=30s");
                    }
                }
                else if (previousStatus != result.Status)
                {
                    QueueTelemetryWarning("SHM_STATE", "status=" + result.Status + " message=" + result.Message);
                }

                SequenceCounterSample? sequenceSample = _sequenceCounterSampler.Observe(
                    DateTimeOffset.UtcNow,
                    _reader.SequenceRetries,
                    _reader.SequenceDrops);
                if (sequenceSample != null)
                {
                    QueueTelemetryWarning(
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
                QueueTelemetryError("SHM_READ_EXCEPTION", exception);
            }
            finally
            {
                if (telemetryGateEntered)
                {
                    Monitor.Exit(_telemetryGate);
                }
            }
        }

        private void DrivingFrame(object? sender, EventArgs eventArgs)
        {
            long ticks = Stopwatch.GetTimestamp();
            if (_disposed || Volatile.Read(ref _processId) < 0 || !_overlay.WantsDrivingTelemetry)
            {
                _lastRenderTicks = 0;
                _lastRenderTime = TimeSpan.MinValue;
                return;
            }
            TimeSpan renderingTime = ((RenderingEventArgs)eventArgs).RenderingTime;
            if (renderingTime == _lastRenderTime) return;
            _lastRenderTime = renderingTime;
            if (_lastRenderTicks != 0)
            {
                long gap = ticks - _lastRenderTicks;
                _renderIntervals++; _renderGapTicks += gap;
                _renderMaxGapTicks = Math.Max(_renderMaxGapTicks, gap);
                if (gap > Stopwatch.Frequency / 60.0) _renderOverBudget++;
            }
            _lastRenderTicks = ticks;
            // Local HUD reads follow distinct render frames; only ReadTelemetry feeds capture.
            // Do not apply recorder cadence to the display path.
            TelemetrySnapshot? snapshot = Volatile.Read(ref _latest);
            DrivingTelemetrySample? sample = null;
            if (snapshot != null && DateTimeOffset.UtcNow - snapshot.CapturedAt < TimeSpan.FromMilliseconds(500)
                )
            {
                if (Monitor.TryEnter(_readerGate))
                {
                    try { sample = _reader.TryReadDriving(_drivingLocalIndex, _sessionTracker.Generation, snapshot.GameStateRaw, snapshot.SessionStateRaw); }
                    finally { Monitor.Exit(_readerGate); }
                    if (sample == null) _drivingRejected++;
                }
                else _drivingBusy++;
            }
            if (sample != null)
            {
                _lastDrivingDataAt = sample.CapturedAt;
                _overlay.UpdateDrivingSample(sample);
                _drivingUpdateCount++;
            }
            else if (DateTimeOffset.UtcNow - _lastDrivingDataAt > TimeSpan.FromMilliseconds(150))
                _overlay.UpdateDrivingSample(null);
            _drivingMaxWorkTicks = Math.Max(_drivingMaxWorkTicks, Stopwatch.GetTimestamp() - ticks);
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
                _status.SessionPlayMode = _activityCapture?.DetectedPlayMode ?? SessionPlayMode.Unknown;
                int pid = Volatile.Read(ref _processId);
                if (pid < 0)
                {
                    _invalidLapDisplayTracker.Observe(null, _sessionTracker.Generation);
                    ApplyVisibility(new OverlayVisibilityDecision(false, "WAIT_PROCESS"), null, null, null, null);
                    return;
                }

                GameWindowSnapshot? window = _windowTracker.TryGetWindow(pid);
                LogWindowChanges(window);

                TelemetrySnapshot? snapshot = Volatile.Read(ref _latest);
                _invalidLapDisplayTracker.Observe(snapshot, _sessionTracker.Generation);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                MultiplayerOverlayDecision? multiplayerDecision = snapshot == null
                    ? null
                    : _multiplayerOverlayController.Observe(snapshot, _sessionTracker.Generation, now, _status.SessionPlayMode);
                LocalParticipantResolution? local = snapshot == null ? null : _localResolver.Resolve(snapshot);
                bool gameplayValid = snapshot != null && local != null && local.IsValid && local.Participant != null;
                _drivingLocalIndex = gameplayValid ? local!.Participant!.Index : -1;
                bool waitingValid = multiplayerDecision?.Mode == MultiplayerOverlayMode.Waiting
                    && multiplayerDecision.Waiting != null;
                GameWindowSnapshot? outputWindow = _overlay.ResolveOutputWindow(window, _overlay.IsVrSceneActive(pid), pid);
                OverlayVisibilityDecision decision = _visibilityController.Evaluate(true, outputWindow, gameplayValid || waitingValid);
                ApplyVisibility(decision, outputWindow, snapshot, local, multiplayerDecision);

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
                    _status.Message = _lastReadStatus switch
                    {
                        TelemetryReadStatus.UnsupportedVersion => "지원하지 않는 AMS2 공유 메모리 버전입니다. 프로그램 업데이트를 확인해 주세요.",
                        TelemetryReadStatus.InvalidData => "게임 데이터가 아직 유효하지 않습니다. 연결을 다시 확인하는 중입니다.",
                        _ => "게임 데이터를 읽지 못했습니다. 연결을 다시 확인하는 중입니다."
                    };
                    _status.AccentColor = "#FF6B6B";
                }

                SamplePerformance();
            }
            catch (Exception exception)
            {
                _overlay.HideOverlay();
                _logger.Error("UI_TICK_EXCEPTION", exception);
            }
            finally { UpdateDrivingSubscription(this, EventArgs.Empty); }
        }

        private void ApplyVisibility(
            OverlayVisibilityDecision decision,
            GameWindowSnapshot? window,
            TelemetrySnapshot? snapshot,
            LocalParticipantResolution? local,
            MultiplayerOverlayDecision? multiplayerDecision)
        {
            string effectiveVisibilityReason = decision.Reason;
            if (decision.ShouldShow && multiplayerDecision != null)
            {
                effectiveVisibilityReason += "/" + multiplayerDecision.Reason;
            }
            if (effectiveVisibilityReason != _lastVisibilityReason)
            {
                _logger.Info(decision.ShouldShow ? "OVERLAY_SHOW" : "OVERLAY_HIDE", "reason=" + effectiveVisibilityReason);
                _lastVisibilityReason = effectiveVisibilityReason;
            }

            if (!decision.ShouldShow || window == null || snapshot == null)
            {
                _overlay.HideOverlay();
                return;
            }

            if (multiplayerDecision?.Mode == MultiplayerOverlayMode.Waiting)
            {
                MultiplayerWaitingOverlayViewModel? waiting = multiplayerDecision.Waiting;
                if (waiting == null)
                {
                    _overlay.HideOverlay();
                    return;
                }

                string waitingKey = "WAITING|" + waiting.Title + "|" + waiting.SessionLabel + "|" + waiting.ParticipantCountText
                    + "|" + waiting.RemainingLabel + "|" + waiting.RemainingValue;
                if (waitingKey != _lastPresentationKey)
                {
                    _lastPresentationKey = waitingKey;
                    Interlocked.Increment(ref _uiUpdateCount);
                }
                _overlay.ShowWaitingAt(window, waiting);
                return;
            }

            if (multiplayerDecision?.Mode != MultiplayerOverlayMode.Gameplay || local?.Participant == null)
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

            RaceEventUpdate eventUpdate = _eventEngine.Observe(snapshot, league, _sessionTracker.Generation, now, raceControlUpdate.OverlayState,
                _invalidLapDisplayTracker.OutLapParticipants);
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

            if (!_overlay.HasPresentationDemand)
            {
                // Keep event/session analysis above; skip only unused display projection.
                _lastPresentationKey = string.Empty;
                _overlay.ShowAt(window);
                return;
            }
            int rankingRowCapacity = _overlay.GetTimingTowerRowCapacity(window);
            string presentationKey = BuildPresentationKey(
                snapshot,
                local.Participant,
                league,
                eventUpdate.CurrentEvent,
                eventUpdate.QueuedCount,
                raceControlUpdate,
                multiplayerDecision.EffectiveRemainingSeconds,
                multiplayerDecision.RemainingDisplayTextOverride)
                + "|towerRows=" + rankingRowCapacity.ToString(CultureInfo.InvariantCulture)
                // Opponents' game timing must refresh even before our first
                // observed line crossing or while the local player's clock is stopped.
                + "|towerSnapshot=" + snapshot.SequenceNumber.ToString(CultureInfo.InvariantCulture);
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
                    raceControl: raceControlUpdate,
                    eventTimeRemainingOverride: multiplayerDecision.EffectiveRemainingSeconds,
                    eventTimeRemainingTextOverride: multiplayerDecision.RemainingDisplayTextOverride,
                    rankingRowCapacity: rankingRowCapacity,
                    outLapParticipants: _invalidLapDisplayTracker.OutLapParticipants);
                _relativeDistanceTrendTracker.Apply(timing, _sessionTracker.Generation);
                _invalidLapDisplayTracker.Apply(timing, snapshot, _sessionTracker.Generation);
                _overlay.SetViewModel(OverlayShellViewModel.Build(snapshot, timing, eventUpdate.CurrentEvent, false, raceControl: raceControlUpdate));
                Interlocked.Increment(ref _uiUpdateCount);
            }

            _overlay.UpdateDrivingSession(snapshot);
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
            _drivingRate = _drivingUpdateCount / elapsed; _drivingUpdateCount = 0;
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
                    + " drivingHz=" + _drivingRate.ToString("0.0", CultureInfo.InvariantCulture)
                    + " queue=" + _eventEngine.Queue.WaitingCount
                    + " animationWindows=" + animationWindows
                    + " renderCallbackHz=" + (_renderGapTicks == 0 ? 0 : _renderIntervals * (double)Stopwatch.Frequency / _renderGapTicks).ToString("0.0", CultureInfo.InvariantCulture)
                    + " renderGapMaxMs=" + (_renderMaxGapTicks * 1000.0 / Stopwatch.Frequency).ToString("0.0", CultureInfo.InvariantCulture)
                    + " renderGapsOver16ms=" + _renderOverBudget
                    + " renderIntervals=" + _renderIntervals
                    + " drivingWorkMaxMs=" + (_drivingMaxWorkTicks * 1000.0 / Stopwatch.Frequency).ToString("0.0", CultureInfo.InvariantCulture)
                    + " drivingReadBusy=" + _drivingBusy + " drivingReadRejected=" + _drivingRejected
                    + " wpfRenderTier=" + (RenderCapability.Tier >> 16));
                _lastRenderTicks = _renderGapTicks = _renderMaxGapTicks = _drivingMaxWorkTicks = 0;
                _renderIntervals = _renderOverBudget = _drivingBusy = _drivingRejected = 0;
                _lastCpuTime = cpu;
                _lastPerformanceAt = now;
            }

            _performanceClock.Restart();
        }

        private void Detach(string reason)
        {
            int oldPid = Interlocked.Exchange(ref _processId, -1);
            _drivingLocalIndex = -1; _lastDrivingDataAt = DateTimeOffset.MinValue;
            Interlocked.Exchange(ref _latest, null);
            _lastReadStatus = TelemetryReadStatus.MappingUnavailable;
            _sharedMemoryAttached = false;
            _lastPresentationKey = _lastWindowKey = _lastInvalidSplitKey = _lastEventId = string.Empty;
            _eventEngine.Reset();
            _raceControlAnalyzer.Reset();
            _multiplayerOverlayController.Reset();
            _overlay.ResetDrivingTelemetry();
            _overlay.HideOverlay();
            _detachTask = Task.Run(() =>
            {
                lock (_telemetryGate)
                {
                    lock (_readerGate) { _reader.Reset(); _sessionTracker.Reset(); }
                    Interlocked.Exchange(ref _latest, null);
                    _activityCapture?.GameDetached();
                }
            });
            _logger.Info("AMS2_DETACH", "pid=" + oldPid + " reason=" + reason + " reattach=WAIT");
        }

        private static string BuildPresentationKey(
            TelemetrySnapshot snapshot,
            ParticipantSnapshot local,
            LeagueClassification league,
            OverlayEvent? currentEvent,
            int queuedEvents,
            RaceControlUpdate raceControl,
            float? effectiveRemainingSeconds,
            string? remainingDisplayTextOverride)
        {
            return snapshot.GameStateRaw + "|" + snapshot.SessionStateRaw + "|" + snapshot.NumParticipants + "|" + league.LeagueParticipantCount + "|" + league.SafetyCarsExcluded + "|"
                + local.Index + "|" + local.RacePosition + "|" + local.CurrentLap + "|" + local.LapsCompleted + "|"
                + (league.Local?.LeaguePosition ?? 0) + "|" + FormatFloat(local.LastLapTime) + "|" + FormatFloat(local.BestLapTime) + "|"
                + FormatFloat(snapshot.CurrentTime) + "|" + FormatFloat(local.CurrentSector1Time) + "|" + FormatFloat(local.CurrentSector2Time) + "|" + FormatFloat(local.CurrentSector3Time) + "|" + local.CurrentSector + "|" + local.LapInvalidated + "|" + snapshot.LapInvalidated + "|"
                + FormatFloat(snapshot.EventTimeRemaining) + "|" + (effectiveRemainingSeconds.HasValue ? FormatFloat(effectiveRemainingSeconds.Value) : "NONE") + "|"
                + (remainingDisplayTextOverride ?? "NONE") + "|"
                + FormatFloat(snapshot.SessionDuration) + "|" + snapshot.SessionAdditionalLaps + "|" + FormatFloat(snapshot.TrackLength) + "|" + FormatFloat(local.CurrentLapDistance) + "|"
                + snapshot.HighestFlagColourRaw + "|" + snapshot.HighestFlagReasonRaw + "|fcy=" + snapshot.YellowFlagStateRaw + "|" + raceControl.Version + "|" + raceControl.OverlayState + "|" + (raceControl.ActiveEvent?.Id ?? "-") + "|"
                + FormatFloat(snapshot.SplitTimeAhead) + "|" + FormatFloat(snapshot.SplitTimeBehind) + "|"
                + (league.Ahead?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "-") + "|"
                + (league.Ahead == null ? "-" : FormatFloat(league.Ahead.Source.CurrentLapDistance)) + "|"
                + (league.Behind?.Source.Index.ToString(CultureInfo.InvariantCulture) ?? "-") + "|"
                + (league.Behind == null ? "-" : FormatFloat(league.Behind.Source.CurrentLapDistance)) + "|"
                + (currentEvent?.Id ?? "-") + "|" + queuedEvents;
        }

        public static string? DescribeSpeedAnomaly(TelemetrySnapshot snapshot, int generation)
        {
            // Diagnostic only: schema 0x0030 stores 0.01m/s in [0,65535].
            // Preserve the SHM value; neither clamp nor reinterpret it here.
            float rootSpeed = snapshot.ViewedVehicleTelemetry?.SpeedMetresPerSecond ?? float.NaN;
            ParticipantSnapshot? participant = null;
            for (int index = 0; index < snapshot.Participants.Count; index++)
            {
                var value = snapshot.Participants[index];
                if (float.IsFinite(value.SpeedMetresPerSecond) && value.SpeedMetresPerSecond > 655.35f)
                { participant = value; break; }
            }
            if (!(float.IsFinite(rootSpeed) && rootSpeed > 655.35f) && participant == null) return null;
            return "at=" + snapshot.CapturedAt.ToString("O") + " sequence=" + snapshot.SequenceNumber
                + " generation=" + generation + " game=" + snapshot.GameStateRaw + " session=" + snapshot.SessionStateRaw
                + " viewedSlot=" + snapshot.ViewedParticipantIndex + " rootSpeed=" + FormatFloat(rootSpeed)
                + " rootBits=" + BitConverter.SingleToInt32Bits(rootSpeed).ToString("X8")
                + " anomalySlot=" + participant?.Index + " participantSpeed=" + (participant == null ? "NONE" : FormatFloat(participant.SpeedMetresPerSecond))
                + " active=" + participant?.IsActive + " raceState=" + participant?.RaceStateRaw
                + " lap=" + participant?.CurrentLap + " distance=" + participant?.CurrentLapDistance
                + " rootOffset=6848 participantOffset=10800 unit=m/s rawPreserved=true";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        // FileLogger already owns the bounded background writer. A second
        // unbounded queue would defeat its memory ceiling during disk stalls.
        private void QueueTelemetryInfo(string eventName, string details) => _logger.Info(eventName, details);
        private void QueueTelemetryWarning(string eventName, string details) => _logger.Warning(eventName, details);
        private void QueueTelemetryError(string eventName, Exception exception) => _logger.Error(eventName, exception);

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public ValueTask DisposeAsync()
        {
            if (_shutdownTask != null) return new ValueTask(_shutdownTask);
            _disposed = true;
            _overlay.DrivingTelemetryDemandChanged -= UpdateDrivingSubscription;
            UpdateDrivingSubscription(this, EventArgs.Empty);
            _processTimer.Stop(); _uiTimer.Stop();
            _overlay.HideOverlay();
            System.Threading.Timer? telemetryTimer = _telemetryTimer;
            _telemetryTimer = null;
            _shutdownTask = Task.Run(async () =>
            {
                // Timer callbacks and a previous process detach finish before
                // the reader and downstream capture are allowed to shut down.
                if (telemetryTimer != null) await telemetryTimer.DisposeAsync().ConfigureAwait(false);
                try { await _detachTask.ConfigureAwait(false); }
                catch (Exception exception) { _logger.Error("DETACH_DRAIN_FAILED", exception); }
                lock (_telemetryGate) { lock (_readerGate) { _reader.Dispose(); } }
                _logger.Info("CLIENT_STOP", "clean=true");
            });
            return new ValueTask(_shutdownTask);
        }

    }
}
