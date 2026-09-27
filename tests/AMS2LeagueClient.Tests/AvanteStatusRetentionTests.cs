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
            var gaugeType = typeof(AvanteClusterView).Assembly.GetType("AMS2LeagueClient.Presentation.AvanteBarGauge")!;
            var gaugeFlags = BindingFlags.NonPublic | BindingFlags.Static;
            foreach (string name in new[] { "Fuel", "Coolant" })
            {
                var outline = gaugeType.GetField(name, gaugeFlags)!.GetValue(null)!;
                var marks = (Geometry)gaugeType.GetMethod("QuarterMarks", gaugeFlags)!.Invoke(null, new[] { outline })!;
                AssertEqual(3, marks.GetFlattenedPathGeometry().Figures.Count);
                AssertTrue(marks.Bounds.Top > 618 && marks.Bounds.Bottom < 639); // On the fixed glass within the bar.
            }
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

            // The original's quarter lines live on the glass, never above the bar.
            foreach (int x in new[] { 231, 299, 366, 1692, 1758, 1823 })
                AssertTrue(Blue(middle, x, 610) < 40);

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
            AssertTrue(Blue(full, 170, 623) > Blue(empty, 170, 623) + 20);
            AssertEqual(Blue(empty, 170, 635), Blue(full, 170, 635));
            AssertEqual(Blue(empty, 438, 623), Blue(full, 438, 623));
            AssertTrue(Blue(full, 438, 635) > Blue(empty, 438, 635) + 20);
            AssertEqual(Blue(empty, 1622, 623), Blue(full, 1622, 623));
            AssertTrue(Blue(full, 1622, 635) > Blue(empty, 1622, 635) + 20);
            AssertTrue(Blue(full, 1884, 623) > Blue(empty, 1884, 623) + 20);
            AssertEqual(Blue(empty, 1884, 635), Blue(full, 1884, 635));

            ApplyStatus(view, StatusSnapshot(3, fuel: 1, water: float.NaN));
            var unknown = StatusPixels(view);
            AssertEqual(Blue(empty, 1700, 628), Blue(unknown, 1700, 628));
        }

        private static void AvanteIndicatorAndIgnitionPreview()
        {
            static int Blue(byte[] pixels, int x, int y) => pixels[(y * 2048 + x) * 4];
            static void AssertConcentricCircle(string field, double sourceCenterX, double sourceCenterY,
                double sourceRadiusX, double sourceRadiusY)
            {
                var bounds = (Rect)typeof(AvanteClusterView).GetField(field,
                    BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                const double sourceSize = 1254;
                AssertTrue(Math.Abs(bounds.X + sourceCenterX * bounds.Width / sourceSize - 1024) < 1);
                AssertTrue(Math.Abs(bounds.Y + sourceCenterY * bounds.Height / sourceSize - 397) < 1);
                AssertTrue(Math.Abs(sourceRadiusX * bounds.Width / sourceSize
                    - sourceRadiusY * bounds.Height / sourceSize) < 1);
            }
            // Peak-brightness cardinal samples from the supplied raster: its
            // line is oval/off-centre even though the dial geometry is circular.
            AssertConcentricCircle("OuterFlameBounds", 634.5, 629.5, 415.5, 398.5);
            AssertConcentricCircle("InnerFlameBounds", 628.5, 620, 513.5, 498);
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

            // The two green symbols belong above the dial's right shoulder, not
            // in the far-right corner of the expanded panel.
            var lightsOff = new AvanteClusterView(true) { Width = 820, Height = 436 };
            var lightsOn = new AvanteClusterView(true) { Width = 820, Height = 436 };
            ApplyStatus(lightsOff, StatusSnapshot(0));
            ApplyStatus(lightsOn, StatusSnapshot(0, flags: 1u));
            var unlitPixels = StatusPixels(lightsOff);
            var litPixels = StatusPixels(lightsOn);
            static int GreenCount(byte[] pixels, int width, int x0, int x1, int y0, int y1)
            {
                int count = 0;
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
                {
                    int p = (y * width + x) * 4;
                    if (pixels[p + 1] > pixels[p + 2] + 45 && pixels[p + 1] > pixels[p] + 30 && pixels[p + 1] > 110) count++;
                }
                return count;
            }
            AssertEqual(0, GreenCount(unlitPixels, 820, 510, 575, 65, 98));
            AssertTrue(GreenCount(litPixels, 820, 510, 542, 65, 98) > 20); // Side lamps.
            AssertTrue(GreenCount(litPixels, 820, 542, 575, 65, 98) > 20); // Dipped beam.
            for (int y = 65; y < 98; y++) for (int x = 740; x < 790; x++)
                for (int channel = 0; channel < 4; channel++)
                    AssertEqual(unlitPixels[(y * 820 + x) * 4 + channel], litPixels[(y * 820 + x) * 4 + channel]);
            var compactHousing = (Geometry)typeof(AvanteClusterView).GetMethod("Housing",
                BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { false })!;
            AssertTrue(compactHousing.FillContains(new Point(1024, -12))); // Rounded crown above the old y=0 cut.
            AssertFalse(compactHousing.FillContains(new Point(920, -12)));
            var compactLights = new AvanteClusterView(false) { Width = 569, Height = 545 };
            ApplyStatus(compactLights, StatusSnapshot(0, flags: 1u));
            var compactPixels = StatusPixels(compactLights, _layoutCaptureDirectory == null ? null
                : Path.Combine(_layoutCaptureDirectory, "avante-headlights-compact-on.png"));
            AssertTrue(GreenCount(compactPixels, 569, 210, 260, 255, 292) > 20); // Side lamp left of gear.
            AssertTrue(GreenCount(compactPixels, 569, 315, 360, 255, 292) > 20); // Dipped beam right of gear.
            AssertEqual(0, GreenCount(compactPixels, 569, 410, 485, 85, 120)); // Old shoulder position cleared.
            static (int Left, int Right) GreenBounds(byte[] pixels, int x0, int x1)
            {
                int left = x1, right = x0;
                for (int y = 255; y < 292; y++) for (int x = x0; x < x1; x++)
                {
                    int p = (y * 569 + x) * 4;
                    if (pixels[p + 1] > pixels[p + 2] + 45 && pixels[p + 1] > pixels[p] + 30 && pixels[p + 1] > 110)
                    { left = Math.Min(left, x); right = Math.Max(right, x); }
                }
                return (left, right);
            }
            var side = GreenBounds(compactPixels, 210, 260);
            var beam = GreenBounds(compactPixels, 315, 360);
            // Positions are mapped from the user's 944x800 Photoshop dial to this 569x545 capture.
            AssertTrue(side.Left >= 211 && side.Left <= 216 && side.Right >= 245 && side.Right <= 251);
            AssertTrue(beam.Left >= 323 && beam.Left <= 328 && beam.Right >= 354 && beam.Right <= 361);
            int unitLeft = 569, unitTop = 545, unitRight = 0, unitBottom = 0;
            for (int y = 355; y < 385; y++) for (int x = 337; x < 380; x++)
            {
                int p = (y * 569 + x) * 4;
                if (compactPixels[p + 2] < 190 || compactPixels[p + 1] < 205 || compactPixels[p] < 210) continue;
                unitLeft = Math.Min(unitLeft, x); unitRight = Math.Max(unitRight, x);
                unitTop = Math.Min(unitTop, y); unitBottom = Math.Max(unitBottom, y);
            }
            AssertTrue(unitLeft >= 342 && unitLeft <= 346 && unitRight >= 366 && unitRight <= 370);
            AssertTrue(unitTop >= 365 && unitTop <= 369 && unitBottom >= 373 && unitBottom <= 377);

            var view = new AvanteClusterView(true) { Width = 2048, Height = 1090 };
            ApplyStatus(view, StatusSnapshot(0));
            var method = typeof(AvanteClusterView).GetMethod("DrawIgnition", BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(view, new object[] { 1.0 });
            var settled = StatusPixels(view);
            if (_layoutCaptureDirectory != null)
            {
                var actual = new AvanteClusterView(true) { Width = 820, Height = 436 };
                ApplyStatus(actual, StatusSnapshot(0));
                method.Invoke(actual, new object[] { .18 });
                StatusPixels(actual, Path.Combine(_layoutCaptureDirectory, "avante-ignition-actual-size.png"));
                var compact = new AvanteClusterView(false) { Width = 569, Height = 545 };
                ApplyStatus(compact, StatusSnapshot(0));
                method.Invoke(compact, new object[] { .18 });
                StatusPixels(compact, Path.Combine(_layoutCaptureDirectory, "avante-ignition-compact.png"));
                var headlights = new AvanteClusterView(true) { Width = 820, Height = 436 };
                ApplyStatus(headlights, StatusSnapshot(0, flags: 1u));
                StatusPixels(headlights, Path.Combine(_layoutCaptureDirectory, "avante-headlights-on.png"));
                method.Invoke(view, new object[] { .08 });
                StatusPixels(view, Path.Combine(_layoutCaptureDirectory, "avante-ignition-early.png"));
            }
            method.Invoke(view, new object[] { .18 });
            var lit = StatusPixels(view, _layoutCaptureDirectory == null ? null : Path.Combine(_layoutCaptureDirectory, "avante-ignition-middle.png"));
            AssertTrue(Blue(lit, 1110, 530) > Blue(settled, 1110, 530) + 30); // Cyan face grows beneath the dial readout.
            for (int y = 902; y < 1090; y += 8)
                for (int x = 640; x <= 1408; x += 16)
                {
                    int pixel = (y * 2048 + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                        AssertEqual(settled[pixel + channel], lit[pixel + channel]); // No flame below the panel or its border.
                }
            for (int y = 800; y <= 870; y += 10)
                for (int x = 720; x <= 1320; x += 10)
                {
                    int pixel = (y * 2048 + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                        AssertEqual(settled[pixel + channel], lit[pixel + channel]); // Lower panel stays in front of the flame.
                }
            if (_layoutCaptureDirectory != null)
            {
                method.Invoke(view, new object[] { .9 });
                StatusPixels(view, Path.Combine(_layoutCaptureDirectory, "avante-ignition-near-end.png"));
                method.Invoke(view, new object[] { .55 });
            }
            // The broad flame must leave the instrument housing while remaining
            // inside the enlarged transparent output canvas.
            bool outerFlameVisible = false;
            for (int y = 0; y < 140 && !outerFlameVisible; y++)
                for (int x = 560; x < 1490; x++)
                    if (lit[(y * 2048 + x) * 4 + 3] > settled[(y * 2048 + x) * 4 + 3] + 60)
                    { outerFlameVisible = true; break; }
            AssertTrue(outerFlameVisible);
            AssertEqual((byte)0, settled[3]); // The enlarged margin remains transparent at rest.
            AssertEqual((byte)0, settled[((1090 - 1) * 2048 + 2047) * 4 + 3]);
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
