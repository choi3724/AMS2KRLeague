using System;
using System.Runtime.InteropServices;

namespace AMS2LeagueClient.Vr
{
    // Owns an independent D3D11 device; never hooks or accesses the game's graphics device.
    // All calls are made on the overlay UI thread. COM slots follow Windows d3d11.h / dxgi.h.
    public sealed class D3D11OverlayTexture : IDisposable
    {
        private IntPtr _device, _context, _texture;
        private int _width, _height;

        public D3D11OverlayTexture(int adapterIndex)
        {
            if (adapterIndex < 0) throw new ArgumentOutOfRangeException(nameof(adapterIndex));
            IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
                Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out factory));
                Marshal.ThrowExceptionForHR(Method<EnumAdapters>(factory, 7)(factory, (uint)adapterIndex, out adapter));
                // Explicit SteamVR adapter + D3D_DRIVER_TYPE_UNKNOWN; SDK version 7.
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0,
                    IntPtr.Zero, 0, 7, out _device, out _, out _context));
            }
            catch { Dispose(); throw; }
            finally { Release(ref adapter); Release(ref factory); }
        }

        public void Submit(VrFrame frame, Action<IntPtr> submitTexture)
        {
            if (_device == IntPtr.Zero) throw new ObjectDisposedException(nameof(D3D11OverlayTexture));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (submitTexture == null) throw new ArgumentNullException(nameof(submitTexture));
            IntPtr target = _texture;
            bool replacing = target == IntPtr.Zero || _width != frame.Width || _height != frame.Height;
            if (replacing)
            {
                var description = new TextureDescription
                {
                    Width = (uint)frame.Width, Height = (uint)frame.Height, MipLevels = 1, ArraySize = 1,
                    Format = 28, SampleCount = 1, BindFlags = 8, MiscFlags = 2
                    // R8G8B8A8_UNORM, DEFAULT usage, SHADER_RESOURCE, RESOURCE_MISC_SHARED.
                };
                Marshal.ThrowExceptionForHR(Method<CreateTexture2D>(_device, 5)(_device, ref description, IntPtr.Zero, out target));
            }
            try
            {
                GCHandle pixels = GCHandle.Alloc(frame.Rgba, GCHandleType.Pinned);
                try
                {
                    // UpdateSubresource snapshots CPU bytes before returning; no async image-loader event.
                    Method<UpdateSubresource>(_context, 48)(_context, target, 0, IntPtr.Zero,
                        pixels.AddrOfPinnedObject(), (uint)frame.Width * 4, 0);
                }
                finally { pixels.Free(); }
                Method<Flush>(_context, 111)(_context);
                submitTexture(target);
                Method<Flush>(_context, 111)(_context);
                Marshal.ThrowExceptionForHR(Method<GetDeviceRemovedReason>(_device, 39)(_device));
                if (replacing)
                {
                    // Retain the previous texture until its replacement has been accepted.
                    Release(ref _texture);
                    _texture = target; _width = frame.Width; _height = frame.Height;
                }
            }
            catch { if (replacing) Release(ref target); throw; }
        }

        public void Dispose()
        {
            Release(ref _texture); Release(ref _context); Release(ref _device);
            _width = _height = 0;
        }

        private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
        private static void Release(ref IntPtr instance)
        {
            if (instance == IntPtr.Zero) return;
            Marshal.Release(instance); instance = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureDescription
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality,
                Usage, BindFlags, CpuAccessFlags, MiscFlags;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2D(IntPtr device, ref TextureDescription description, IntPtr data, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UpdateSubresource(IntPtr context, IntPtr resource, uint subresource, IntPtr box, IntPtr data, uint rowPitch, uint depthPitch);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void Flush(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDeviceRemovedReason(IntPtr device);
        [DllImport("dxgi.dll", ExactSpelling = true)]
        private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags,
            IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out uint featureLevel, out IntPtr context);
    }
}
