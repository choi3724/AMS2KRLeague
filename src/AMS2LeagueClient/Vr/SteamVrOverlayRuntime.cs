using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AMS2LeagueClient.Core.Presentation;
using Valve.VR;

namespace AMS2LeagueClient.Vr
{
    public sealed class SteamVrOverlayRuntime : IVrOverlayRuntime
    {
        private CVRSystem? _system;
        private CVROverlay? _overlay;
        private ulong _handle;
        private bool _initialized, _shown;
        private D3D11OverlayTexture? _texture;
        private HmdMatrix34_t? _anchor;
        private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[1];
        public uint SceneProcessId { get; private set; }
        public bool CanSubmit => _texture != null;

        public bool Connect(out string status)
        {
            status = "VR: SteamVR를 실행해 주세요 · Meta/OpenXR 단독 실행은 현재 미지원";
            Process[] servers = Process.GetProcessesByName("vrserver");
            bool running = servers.Length > 0;
            foreach (Process process in servers) process.Dispose();
            if (!running) return false; // Never start or reconfigure the user's VR runtime.
            if (!OpenVR.IsRuntimeInstalled() || !OpenVR.IsHmdPresent())
            {
                status = "VR: SteamVR 헤드셋 연결 대기";
                return false;
            }
            EVRInitError error = EVRInitError.None;
            _system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
            if (error != EVRInitError.None || _system == null)
            {
                status = "VR: SteamVR 연결 실패 · " + error;
                return false;
            }
            _initialized = true;
            try
            {
                _overlay = OpenVR.Overlay ?? throw new InvalidOperationException("SteamVR 오버레이 API 없음");
                Check(_overlay.CreateOverlay("kr.ams2league.hud." + Environment.ProcessId, "AMS2 리그 오버레이", ref _handle));
                Check(_overlay.SetOverlayInputMethod(_handle, VROverlayInputMethod.None));
                Check(_overlay.SetOverlayFlag(_handle, VROverlayFlags.IsPremultiplied, true));
                int adapterIndex = -1;
                _system.GetDXGIOutputInfo(ref adapterIndex);
                _texture = new D3D11OverlayTexture(adapterIndex);
                _anchor = null;
                status = "VR: SteamVR 연결됨 · 시험 기능";
                return true;
            }
            catch { Dispose(); throw; }
        }

        public bool Poll()
        {
            if (_system == null || _overlay == null) return false;
            var item = new VREvent_t();
            uint size = (uint)Marshal.SizeOf<VREvent_t>();
            for (int i = 0; i < 32 && _system.PollNextEvent(ref item, size); i++)
            {
                if (item.eventType == (uint)EVREventType.VREvent_Quit
                    || item.eventType == (uint)EVREventType.VREvent_ProcessQuit
                    || item.eventType == (uint)EVREventType.VREvent_DriverRequestedQuit)
                {
                    _system.AcknowledgeQuit_Exiting();
                    return false;
                }
            }
            for (int i = 0; i < 32 && _overlay.PollNextOverlayEvent(_handle, ref item, size); i++)
            {
                // Drain notifications; persistent D3D textures do not use the raw image loader.
            }
            SceneProcessId = OpenVR.Applications.GetCurrentSceneProcessId();
            return _system.IsTrackedDeviceConnected(OpenVR.k_unTrackedDeviceIndex_Hmd);
        }

        public void Submit(VrFrame frame, VrHudSettings settings)
        {
            if (_overlay == null || _system == null || _texture == null) return;
            Check(_overlay.SetOverlayWidthInMeters(_handle, (float)settings.WidthMetres));
            HmdMatrix34_t transform = RelativeTransform(settings);
            if (settings.FollowHead)
                Check(_overlay.SetOverlayTransformTrackedDeviceRelative(_handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform));
            else
            {
                if (!_anchor.HasValue)
                {
                    _system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, _poses);
                    if (!_poses[0].bPoseIsValid) { Hide(); return; }
                    _anchor = _poses[0].mDeviceToAbsoluteTracking;
                }
                transform = Multiply(_anchor.Value, transform);
                Check(_overlay.SetOverlayTransformAbsolute(_handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref transform));
            }
            _texture.Submit(frame, pointer =>
            {
                var texture = new Texture_t { handle = pointer, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Gamma };
                Check(_overlay.SetOverlayTexture(_handle, ref texture));
            });
            if (!_shown) { Check(_overlay.ShowOverlay(_handle)); _shown = true; }
        }

        public static HmdMatrix34_t RelativeTransform(VrHudSettings settings)
        {
            VrHudSettings s = settings.Normalize();
            double yaw = s.YawDegrees * Math.PI / 180, pitch = s.PitchDegrees * Math.PI / 180;
            float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
            float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
            return new HmdMatrix34_t
            {
                m0 = cy, m1 = sy * sp, m2 = sy * cp, m3 = (float)s.HorizontalMetres,
                m4 = 0, m5 = cp, m6 = -sp, m7 = (float)s.VerticalMetres,
                m8 = -sy, m9 = cy * sp, m10 = cy * cp, m11 = -(float)s.DistanceMetres
            };
        }

        public static HmdMatrix34_t Multiply(HmdMatrix34_t a, HmdMatrix34_t b)
        {
            return new HmdMatrix34_t
            {
                m0 = a.m0*b.m0+a.m1*b.m4+a.m2*b.m8, m1 = a.m0*b.m1+a.m1*b.m5+a.m2*b.m9,
                m2 = a.m0*b.m2+a.m1*b.m6+a.m2*b.m10, m3 = a.m0*b.m3+a.m1*b.m7+a.m2*b.m11+a.m3,
                m4 = a.m4*b.m0+a.m5*b.m4+a.m6*b.m8, m5 = a.m4*b.m1+a.m5*b.m5+a.m6*b.m9,
                m6 = a.m4*b.m2+a.m5*b.m6+a.m6*b.m10, m7 = a.m4*b.m3+a.m5*b.m7+a.m6*b.m11+a.m7,
                m8 = a.m8*b.m0+a.m9*b.m4+a.m10*b.m8, m9 = a.m8*b.m1+a.m9*b.m5+a.m10*b.m9,
                m10 = a.m8*b.m2+a.m9*b.m6+a.m10*b.m10, m11 = a.m8*b.m3+a.m9*b.m7+a.m10*b.m11+a.m11
            };
        }

        public void Hide()
        {
            if (_shown && _overlay != null) Check(_overlay.HideOverlay(_handle));
            _shown = false;
        }
        public void Recenter() => _anchor = null;
        private static void Check(EVROverlayError error)
        { if (error != EVROverlayError.None) throw new InvalidOperationException("SteamVR: " + error); }
        public void Dispose()
        {
            try
            {
                if (_overlay != null && _handle != 0) _overlay.DestroyOverlay(_handle);
            }
            finally
            {
                try { if (_initialized) OpenVR.Shutdown(); }
                finally
                {
                    _texture?.Dispose(); _texture = null;
                    _initialized = _shown = false; _handle = 0; SceneProcessId = 0;
                    _overlay = null; _system = null; _anchor = null;
                }
            }
        }
    }
}
