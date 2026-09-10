using System;
using System.Linq;
using System.Runtime.InteropServices;
using AMS2LeagueClient.Vr;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void VrPersistentTexturePixels()
        {
            using var texture = new D3D11OverlayTexture(0);
            IntPtr first = IntPtr.Zero;
            var pixels = new byte[1280 * 720 * 4];
            var frame = new VrFrame(1280, 720, pixels);
            for (int update = 0; update < 30; update++)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte alpha = (byte)((i / 4 + update) % 256);
                    pixels[i] = (byte)Math.Min(update * 7, alpha);
                    pixels[i + 1] = (byte)(alpha / 2); pixels[i + 2] = (byte)(alpha / 3); pixels[i + 3] = alpha;
                }
                texture.Submit(frame, pointer =>
                {
                    if (first == IntPtr.Zero) first = pointer;
                    AssertEqual(first, pointer); // no per-frame texture recreation / raw image loading
                });
                byte[] expected = (byte[])pixels.Clone();
                Array.Clear(pixels); // caller may reuse its CPU buffer as soon as Submit returns
                AssertTrue(expected.SequenceEqual(ReadD3DTexture(first, 1280, 720)));
            }
            var resized = new VrFrame(3, 2, Enumerable.Range(0, 24).Select(i => (byte)i).ToArray());
            bool failed = false;
            try { texture.Submit(resized, _ => throw new InvalidOperationException("synthetic compositor failure")); }
            catch (InvalidOperationException) { failed = true; }
            AssertTrue(failed);
            texture.Submit(frame, pointer => AssertEqual(first, pointer)); // failed resize retained old texture
            IntPtr replacement = IntPtr.Zero;
            texture.Submit(resized, pointer => { AssertTrue(pointer != first); replacement = pointer; });
            AssertTrue(resized.Rgba.SequenceEqual(ReadD3DTexture(replacement, 3, 2)));
            texture.Dispose(); texture.Dispose();
            bool disposed = false;
            try { texture.Submit(frame, _ => { }); } catch (ObjectDisposedException) { disposed = true; }
            AssertTrue(disposed);
            Console.WriteLine("PROOF D3D11: 30 persistent 1280x720 uploads, exact RGBA/alpha GPU readback, CPU buffer reuse, resize/failure/disposal; no headset");
        }

        private static byte[] ReadD3DTexture(IntPtr texture, int width, int height)
        {
            IntPtr device = IntPtr.Zero, context = IntPtr.Zero, staging = IntPtr.Zero;
            try
            {
                D3DMethod<D3DGetObject>(texture, 3)(texture, out device);
                D3DMethod<D3DGetObject>(device, 40)(device, out context);
                D3DMethod<D3DGetDescription>(texture, 10)(texture, out D3DDescription description);
                AssertEqual((uint)width, description.Width); AssertEqual((uint)height, description.Height);
                AssertEqual(28u, description.Format); AssertEqual(2u, description.MiscFlags);
                description.Usage = 3; description.BindFlags = 0; description.CpuAccessFlags = 0x20000; description.MiscFlags = 0;
                Marshal.ThrowExceptionForHR(D3DMethod<D3DCreateTexture>(device, 5)(device, ref description, IntPtr.Zero, out staging));
                D3DMethod<D3DCopyResource>(context, 47)(context, staging, texture);
                Marshal.ThrowExceptionForHR(D3DMethod<D3DMap>(context, 14)(context, staging, 0, 1, 0, out D3DMapped mapped));
                try
                {
                    byte[] result = new byte[width * height * 4];
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(IntPtr.Add(mapped.Data, checked(y * (int)mapped.RowPitch)), result, y * width * 4, width * 4);
                    return result;
                }
                finally { D3DMethod<D3DUnmap>(context, 15)(context, staging, 0); }
            }
            finally
            {
                if (staging != IntPtr.Zero) Marshal.Release(staging);
                if (context != IntPtr.Zero) Marshal.Release(context);
                if (device != IntPtr.Zero) Marshal.Release(device);
            }
        }

        private static T D3DMethod<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DDescription
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality,
                Usage, BindFlags, CpuAccessFlags, MiscFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct D3DMapped { public IntPtr Data; public uint RowPitch, DepthPitch; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void D3DGetObject(IntPtr instance, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void D3DGetDescription(IntPtr instance, out D3DDescription description);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int D3DCreateTexture(IntPtr instance, ref D3DDescription description, IntPtr data, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void D3DCopyResource(IntPtr instance, IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int D3DMap(IntPtr instance, IntPtr resource, uint subresource, uint mapType, uint flags, out D3DMapped mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void D3DUnmap(IntPtr instance, IntPtr resource, uint subresource);
    }
}
