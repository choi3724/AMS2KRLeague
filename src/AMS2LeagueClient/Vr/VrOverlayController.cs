using System;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Vr
{
    public sealed class VrFrame
    {
        public const int MaximumDimension = 1280;
        public VrFrame(int width, int height, byte[] rgba)
        {
            if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension
                || rgba == null || rgba.Length != checked(width * height * 4))
                throw new ArgumentException("Invalid VR frame.");
            Width = width; Height = height; Rgba = rgba;
        }
        public int Width { get; }
        public int Height { get; }
        public byte[] Rgba { get; }
    }

    // The runtime boundary also lets lifecycle tests run without a headset.
    public interface IVrOverlayRuntime : IDisposable
    {
        bool Connect(out string status);
        bool Poll();
        uint SceneProcessId { get; }
        bool CanSubmit { get; }
        void Submit(VrFrame frame, VrHudSettings settings);
        void Hide();
        void Recenter();
    }

    public sealed class VrOverlayController : IDisposable
    {
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(1.0 / 15);
        private readonly IVrOverlayRuntime _runtime;
        private readonly Func<VrFrame?> _capture;
        private readonly Action<string> _report;
        private VrHudSettings _settings = new VrHudSettings();
        private DateTimeOffset _nextRetry, _nextFrame;
        private bool _disposed;
        private string _status = string.Empty;

        public VrOverlayController(IVrOverlayRuntime runtime, Func<VrFrame?> capture, Action<string> report)
        {
            _runtime = runtime; _capture = capture; _report = report;
        }
        public bool Connected { get; private set; }
        public uint SceneProcessId => Connected ? _runtime.SceneProcessId : 0;
        public OverlayOutputMode Output => _settings.Output;

        public void Configure(VrHudSettings settings)
        {
            _settings = settings.Normalize();
            _nextRetry = _nextFrame = DateTimeOffset.MinValue;
            if (_settings.Output == OverlayOutputMode.Monitor)
            {
                Disconnect();
                Report("VR: 꺼짐 · 모니터 표시");
            }
            else
            {
                _runtime.Recenter();
                Report("VR: SteamVR 연결 확인 중 · 시험 기능");
            }
        }

        public double LastSubmissionMs { get; private set; }

        public void Tick(DateTimeOffset now, bool visible, uint expectedSceneProcessId = 0)
        {
            if (_disposed || Output == OverlayOutputMode.Monitor) return;
            try
            {
                if (!Connected)
                {
                    if (now < _nextRetry) return;
                    _nextRetry = now + RetryInterval;
                    Connected = _runtime.Connect(out string status);
                    Report(status);
                    if (!Connected) return;
                }
                if (!_runtime.Poll()) throw new InvalidOperationException("연결 종료");
                if (expectedSceneProcessId != 0 && _runtime.SceneProcessId != expectedSceneProcessId) visible = false;
                if (!visible)
                {
                    _runtime.Hide();
                    Report("VR: 연결됨 · AMS2 화면 또는 레이아웃 미리보기 대기");
                    return;
                }
                if (now < _nextFrame || !_runtime.CanSubmit) return;
                _nextFrame = now + FrameInterval;
                VrFrame? frame = _capture();
                if (frame == null) { _runtime.Hide(); Report("VR: 연결됨 · 표시할 패널 없음"); return; }
                long submissionStart = System.Diagnostics.Stopwatch.GetTimestamp();
                try { _runtime.Submit(frame, _settings); }
                finally { LastSubmissionMs = System.Diagnostics.Stopwatch.GetElapsedTime(submissionStart).TotalMilliseconds; }
                Report("VR: SteamVR 전송 중 · 시험 기능 · 최대 15fps");
            }
            catch (Exception exception)
            {
                Disconnect();
                _nextRetry = now + RetryInterval;
                string reason = exception switch
                {
                    DllNotFoundException _ => "VR 라이브러리가 없습니다. 프로그램을 다시 설치해 주세요.",
                    BadImageFormatException _ => "VR 라이브러리의 형식이 올바르지 않습니다.",
                    EntryPointNotFoundException _ => "VR 라이브러리의 버전을 확인해 주세요.",
                    _ => exception.Message.Length <= 120 ? exception.Message : exception.GetType().Name
                };
                Report("VR: 연결 재시도 대기 · " + reason);
            }
        }

        public void Recenter() => _runtime.Recenter();
        private void Report(string value) { if (_status == value) return; _status = value; _report(value); }
        private void Disconnect()
        {
            Connected = false;
            try { _runtime.Dispose(); } catch { /* The recorder must outlive a VR runtime failure. */ }
        }
        public void Dispose() { if (_disposed) return; _disposed = true; Disconnect(); }
    }
}
