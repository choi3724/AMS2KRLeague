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
            ApplyStatus(view, StatusSnapshot(1, flags: 1u << 10, oil: 101.2f, water: 91.1f, torque: 498.2f, distance: 123.2f));
            AssertEqual(builds, StatusRebuilds(view)); AssertTrue(before.SequenceEqual(StatusPixels(view)));
            // Keep sub-litre fuel precision in the continuous gauge, independently of rounded strings.
            ApplyStatus(view, StatusSnapshot(2, fuel: .627f));
            AssertEqual(builds, StatusRebuilds(view)); AssertFalse(before.SequenceEqual(StatusPixels(view)));
            // Water level is continuous too, even when the numeric readout stays rounded.
            var fuelChanged = StatusPixels(view);
            ApplyStatus(view, StatusSnapshot(3, fuel: .627f, water: 91.4f));
            AssertEqual(builds, StatusRebuilds(view)); AssertFalse(fuelChanged.SequenceEqual(StatusPixels(view)));
            ApplyStatus(view, StatusSnapshot(4, flags: (1u << 6) | (1u << 3), oil: 102.1f));
            AssertTrue(StatusRebuilds(view) > builds); AssertFalse(before.SequenceEqual(StatusPixels(view)));
            view.SetSample(null); var unknown = StatusPixels(view); AssertFalse(before.SequenceEqual(unknown));
            ApplyStatus(view, StatusSnapshot(5)); AssertTrue(before.SequenceEqual(StatusPixels(view)));
            ApplyStatus(view, StatusSnapshot(6, fuel: float.NaN)); var invalid = StatusPixels(view);
            ApplyStatus(view, StatusSnapshot(7, fuel: float.NaN)); int invalidBuilds = StatusRebuilds(view);
            AssertTrue(invalid.SequenceEqual(StatusPixels(view))); AssertEqual(invalidBuilds, StatusRebuilds(view));
            view.SetSample(null);
        }

        private static void AvanteBarGaugesFollowSourceContours()
        {
            var view = new AvanteClusterView(true) { Width = 2048, Height = 750 };
            string? Capture(string name)
            {
                if (_layoutCaptureDirectory == null) return null;
                Directory.CreateDirectory(_layoutCaptureDirectory);
                return Path.Combine(_layoutCaptureDirectory, name + ".png");
            }
            static int Blue(byte[] pixels, int x, int y) => pixels[(y * 2048 + x) * 4];
            static int Red(byte[] pixels, int x, int y) => pixels[(y * 2048 + x) * 4 + 2];
            ApplyStatus(view, StatusSnapshot(0, fuel: 0, water: 40));
            var empty = StatusPixels(view, Capture("avante-bars-empty"));
            ApplyStatus(view, StatusSnapshot(1, fuel: .6f, water: 88));
            var middle = StatusPixels(view, Capture("avante-bars-middle"));
            ApplyStatus(view, StatusSnapshot(2, fuel: 1, water: 120));
            var full = StatusPixels(view, Capture("avante-bars-full"));

            AssertTrue(Blue(middle, 1700, 628) > Blue(empty, 1700, 628) + 20);
            AssertEqual(Blue(empty, 1850, 628), Blue(middle, 1850, 628));
            AssertTrue(Blue(full, 1850, 628) > Blue(middle, 1850, 628) + 20);
            AssertTrue(Blue(middle, 220, 628) > Blue(empty, 220, 628) + 20);
            AssertEqual(Blue(empty, 400, 628), Blue(middle, 400, 628));

            // Both partial bars must finish their entire colour sweep at their
            // current ends. A fixed full-track gradient would leave these pale
            // endpoints blue until the gauges reached 100%.
            AssertTrue(Red(middle, 326, 628) > Red(full, 326, 628) + 30);
            AssertTrue(Red(middle, 1770, 628) > Red(full, 1770, 628) + 30);
            AssertTrue(Red(middle, 326, 628) > Red(middle, 220, 628) + 50);
            AssertTrue(Red(middle, 1770, 628) > Red(middle, 1680, 628) + 50);

            // The filled pixels follow opposite slanted source caps, not rectangular ends.
            AssertTrue(Blue(full, 172, 623) > Blue(empty, 172, 623) + 20);
            AssertEqual(Blue(empty, 172, 633), Blue(full, 172, 633));
            AssertEqual(Blue(empty, 436, 623), Blue(full, 436, 623));
            AssertTrue(Blue(full, 436, 633) > Blue(empty, 436, 633) + 20);
            AssertEqual(Blue(empty, 1623, 623), Blue(full, 1623, 623));
            AssertTrue(Blue(full, 1623, 633) > Blue(empty, 1623, 633) + 20);
            AssertTrue(Blue(full, 1880, 623) > Blue(empty, 1880, 623) + 20);
            AssertEqual(Blue(empty, 1880, 633), Blue(full, 1880, 633));

            ApplyStatus(view, StatusSnapshot(3, fuel: 1, water: float.NaN));
            var unknown = StatusPixels(view);
            AssertEqual(Blue(empty, 1700, 628), Blue(unknown, 1700, 628));
        }

        private static void AvanteIndicatorAndIgnitionPreview()
        {
            var type = typeof(AvanteClusterView).Assembly.GetType("AMS2LeagueClient.Presentation.AvanteIndicators")!;
            var stateFlags = BindingFlags.NonPublic | BindingFlags.Static;
            string State(string name, ViewedVehicleTelemetrySnapshot vehicle, DrivingTelemetrySample? sample = null) =>
                type.GetMethod(name, stateFlags)!.Invoke(null, name == "Abs" ? new object?[] { vehicle, sample } : new object?[] { vehicle })!.ToString()!;
            var vehicle = (ViewedVehicleTelemetrySnapshot)Activator.CreateInstance(typeof(ViewedVehicleTelemetrySnapshot), true)!;
            void Set(string name, object value) => typeof(ViewedVehicleTelemetrySnapshot).GetProperty(name)!.SetValue(vehicle, value);
            Set("CarFlagsRaw", (1u << 4) | (1u << 6) | 1u);
            AssertEqual("On", State("Abs", vehicle)); AssertEqual("On", State("Tcs", vehicle));
            AssertTrue((bool)type.GetMethod("Headlights", stateFlags)!.Invoke(null, new object?[] { vehicle })!);
            Set("AntiLockActive", true);
            AssertEqual("Active", State("Abs", vehicle));
            Set("UnfilteredThrottle", .9f); Set("Throttle", .4f);
            Set("Gear", 3); Set("SpeedMetresPerSecond", 20f);
            AssertEqual("Active", State("Tcs", vehicle));

            var view = new AvanteClusterView(true) { Width = 2048, Height = 750 };
            ApplyStatus(view, StatusSnapshot(0));
            var method = typeof(AvanteClusterView).GetMethod("DrawIgnition", BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(view, new object[] { 1.0 });
            var settled = StatusPixels(view);
            if (_layoutCaptureDirectory != null)
            {
                var actual = new AvanteClusterView(true) { Width = 820, Height = 300 };
                ApplyStatus(actual, StatusSnapshot(0));
                method.Invoke(actual, new object[] { .55 });
                StatusPixels(actual, Path.Combine(_layoutCaptureDirectory, "avante-ignition-actual-size.png"));
                method.Invoke(view, new object[] { .25 });
                StatusPixels(view, Path.Combine(_layoutCaptureDirectory, "avante-ignition-early.png"));
            }
            method.Invoke(view, new object[] { .55 });
            var lit = StatusPixels(view, _layoutCaptureDirectory == null ? null : Path.Combine(_layoutCaptureDirectory, "avante-ignition-middle.png"));
            if (_layoutCaptureDirectory != null)
            {
                method.Invoke(view, new object[] { .78 });
                StatusPixels(view, Path.Combine(_layoutCaptureDirectory, "avante-ignition-near-end.png"));
                method.Invoke(view, new object[] { .55 });
            }
            // Top of the ring is reached during the sweep, then cleared exactly once.
            int top = (18 * 2048 + 1024) * 4 + 2;
            AssertTrue(lit[top] > settled[top] + 40);
            method.Invoke(view, new object[] { 1.0 });
            AssertTrue(settled.SequenceEqual(StatusPixels(view)));
            method.Invoke(view, new object[] { .55 });
            view.SetSample(new DrivingTelemetrySample(FixedTime(), 1, 0, 0, 0, 0, 0, 50, 3, rpm: 1000, maxRpm: 8000));
            AssertEqual(.55, (double)typeof(AvanteClusterView).GetProperty("IgnitionProgress", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!); // RPM updates do not advance or restart the intro.
            var started = typeof(AvanteClusterView).GetField("_ignitionStarted", BindingFlags.NonPublic | BindingFlags.Instance)!;
            started.SetValue(view, true);
            typeof(AvanteClusterView).GetMethod("StopMotion", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);
            AssertTrue((bool)started.GetValue(view)!); // Temporary hide does not arm a replay.
            AssertFalse((bool)started.GetValue(new AvanteClusterView())!); // OFF->ON creates a new mode view.
            view.SetSample(null);
        }
    }
}
