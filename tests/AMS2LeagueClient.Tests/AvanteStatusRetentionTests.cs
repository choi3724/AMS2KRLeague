using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static TelemetrySnapshot StatusSnapshot(int revision, float fuel = .63f, uint flags = 0,
            float oil = 101.1f, float water = 91.1f, float torque = 498.1f, float distance = 123.1f)
        {
            var vehicle = (ViewedVehicleTelemetrySnapshot)Activator.CreateInstance(typeof(ViewedVehicleTelemetrySnapshot), true)!;
            void Set(string name, object value) => typeof(ViewedVehicleTelemetrySnapshot).GetProperty(name)!.SetValue(vehicle, value);
            Set("MaxRpm", 8000f); Set("FuelLevel", fuel); Set("FuelCapacityLitres", 110f);
            Set("OilTemperatureCelsius", oil); Set("WaterTemperatureCelsius", water);
            Set("EngineTorqueNewtonMetres", torque); Set("OdometerKilometres", distance); Set("CarFlagsRaw", flags);
            return new TelemetrySnapshot(FixedTime().AddMilliseconds(revision * 7), 14, 3398, 0, 2, 1, 2, 0, 2, 0, 0, 0, 0, 0,
                Array.Empty<ParticipantSnapshot>(), rootCarName: "status-retention", viewedVehicleTelemetry: vehicle);
        }
        private static void ApplyStatus(AvanteClusterView view, TelemetrySnapshot snapshot)
        {
            view.SetSession(snapshot);
            view.SetSample(new DrivingTelemetrySample(snapshot.CapturedAt, 1, 0, 0, 0, 0, 0, 50, 3, rpm: 6000, maxRpm: 8000));
        }
        private static int StatusRebuilds(AvanteClusterView view) => (int)typeof(AvanteClusterView)
            .GetField("_statusRebuilds", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        private static byte[] StatusPixels(AvanteClusterView view, string? path = null)
        {
            view.Measure(new Size(view.Width, view.Height)); view.Arrange(new Rect(0, 0, view.Width, view.Height)); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)view.Width, (int)view.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            if (path != null)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(path); encoder.Save(stream);
            }
            var bytes = new byte[(int)view.Width * (int)view.Height * 4]; bitmap.CopyPixels(bytes, (int)view.Width * 4, 0); return bytes;
        }
        private static void AvanteStatusPreservesDisplayedValues()
        {
            var view = new AvanteClusterView(true) { Width = 1024, Height = 375 };
            ApplyStatus(view, StatusSnapshot(0));
            var before = StatusPixels(view); int builds = StatusRebuilds(view);
            // Raw changes below the visible rounding threshold and unused flags have no visible effect.
            ApplyStatus(view, StatusSnapshot(1, flags: 1u << 10, oil: 101.2f, water: 91.2f, torque: 498.2f, distance: 123.2f));
            AssertEqual(builds, StatusRebuilds(view)); AssertTrue(before.SequenceEqual(StatusPixels(view)));
            // Keep sub-litre fuel precision in the continuous gauge, independently of rounded strings.
            ApplyStatus(view, StatusSnapshot(2, fuel: .627f));
            AssertEqual(builds, StatusRebuilds(view)); AssertFalse(before.SequenceEqual(StatusPixels(view)));
            ApplyStatus(view, StatusSnapshot(3, flags: (1u << 6) | (1u << 3), oil: 102.1f));
            AssertTrue(StatusRebuilds(view) > builds); AssertFalse(before.SequenceEqual(StatusPixels(view)));
            view.SetSample(null); var unknown = StatusPixels(view); AssertFalse(before.SequenceEqual(unknown));
            ApplyStatus(view, StatusSnapshot(4)); AssertTrue(before.SequenceEqual(StatusPixels(view)));
            ApplyStatus(view, StatusSnapshot(5, fuel: float.NaN)); var invalid = StatusPixels(view);
            ApplyStatus(view, StatusSnapshot(6, fuel: float.NaN)); int invalidBuilds = StatusRebuilds(view);
            AssertTrue(invalid.SequenceEqual(StatusPixels(view))); AssertEqual(invalidBuilds, StatusRebuilds(view));
            view.SetSample(null);
        }
    }
}
