using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void OverlayTypographyStaysIndependent()
        {
            var settings = new DrivingHudSettings { TelemetryDesign = "racing" };
            foreach (string key in OverlayComponentKeys.TextConfigurable)
                settings.OverlayText[key] = new DrivingHudSettings.TextAppearance { Font = "Consolas", Scale = 1.4 };
            settings.OverlayText[OverlayComponentKeys.AvanteCluster] = new DrivingHudSettings.TextAppearance { Font = "Arial", Scale = 2 };
            settings.OverlayText[OverlayComponentKeys.AvanteClusterExpanded] = new DrivingHudSettings.TextAppearance { Font = "Arial", Scale = 2 };
            settings.OverlayText["unknown"] = new DrivingHudSettings.TextAppearance { Font = "Arial", Scale = 2 };
            DrivingHudSettings saved = JsonSerializer.Deserialize<DrivingHudSettings>(JsonSerializer.Serialize(settings))!.Normalize();
            AssertEqual(OverlayComponentKeys.TextConfigurable.Length, saved.OverlayText.Count);
            AssertFalse(saved.OverlayText.ContainsKey(OverlayComponentKeys.AvanteCluster));
            AssertFalse(saved.OverlayText.ContainsKey(OverlayComponentKeys.AvanteClusterExpanded));
            foreach (string key in OverlayComponentKeys.TextConfigurable)
            {
                AssertEqual("Consolas", saved.TextFor(key).Font);
                AssertEqual(1.4, saved.TextFor(key).Scale);
            }
            string path = Path.Combine(Path.GetTempPath(), "ams2-typography-" + Guid.NewGuid() + ".json");
            var window = new OverlayWindow(false, path);
            try { window.SaveDrivingHudSettings(saved); }
            finally { window.Close(); }
            window = new OverlayWindow(false, path);
            try
            {
                AssertEqual("Consolas", window.GetDrivingHudSettings().TextFor(OverlayComponentKeys.RelativeDrivers).Font);
                AssertEqual(1.4, window.GetDrivingHudSettings().TextFor(OverlayComponentKeys.Speed).Scale);
                AssertFalse(window.GetDrivingHudSettings().OverlayText.ContainsKey(OverlayComponentKeys.AvanteCluster));
                window.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                window.ShowDemoAt(-5000, -5000, 96); PumpDispatcher();
                Window relativePanel = Application.Current.Windows.Cast<Window>().Single(item => item.Title == "AMS2 전후방 거리");
                RelativeDriversView relativeView = FindDescendant<RelativeDriversView>(relativePanel)!;
                TextBlock label = Descendants<TextBlock>(relativeView).First(item => item.FontFamily.Source == "Consolas");
                double largerText = label.FontSize, originalWidth = relativePanel.Width, originalHeight = relativePanel.Height;
                var reduced = window.GetDrivingHudSettings();
                reduced.OverlayText[OverlayComponentKeys.RelativeDrivers].Scale = 0.6;
                window.SaveDrivingHudSettings(reduced); PumpDispatcher();
                AssertTrue(label.FontSize < largerText);
                AssertEqual(originalWidth, relativePanel.Width);
                AssertEqual(originalHeight, relativePanel.Height);
            }
            finally { window.Close(); }

            var baseline = new DrivingHudSettings { TelemetryDesign = "racing" };
            var preview = typeof(DrivingNumberView).Assembly.GetType("AMS2LeagueClient.Presentation.OverlayGalleryPreview")!
                .GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            foreach (string key in OverlayComponentKeys.TextConfigurable)
            {
                BitmapSource original = (BitmapSource)preview.Invoke(null, new object[] { key, "racing", baseline })!;
                BitmapSource changed = (BitmapSource)preview.Invoke(null, new object[] { key, "racing", saved })!;
                AssertEqual(original.PixelWidth, changed.PixelWidth);
                AssertEqual(original.PixelHeight, changed.PixelHeight);
                byte[] before = new byte[original.PixelWidth * original.PixelHeight * 4];
                byte[] after = new byte[before.Length];
                original.CopyPixels(before, original.PixelWidth * 4, 0);
                changed.CopyPixels(after, changed.PixelWidth * 4, 0);
                int differing = before.Where((value, index) => value != after[index]).Take(100).Count();
                Console.WriteLine("PROOF typography " + key + " differingBytesAtLeast=" + differing);
                AssertTrue(differing > 50);
                if (_layoutCaptureDirectory != null && (key == OverlayComponentKeys.TimingTower
                    || key == OverlayComponentKeys.RaceControl || key == OverlayComponentKeys.Speed
                    || key == OverlayComponentKeys.DrivingDashboard))
                {
                    Directory.CreateDirectory(_layoutCaptureDirectory);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(changed));
                    using var file = File.Create(Path.Combine(_layoutCaptureDirectory, "typography-" + key + ".png"));
                    encoder.Save(file);
                }
            }

            var speed = new DrivingNumberView(false);
            speed.ApplyTypography(saved.TextFor(OverlayComponentKeys.Speed));
            AssertEqual("Consolas", speed.ValueText.FontFamily.Source);
            AssertEqual(36 * 1.4, speed.ValueText.FontSize);
            speed.ApplyTypography(baseline.TextFor(OverlayComponentKeys.Speed));
            AssertEqual(36.0, speed.ValueText.FontSize);
            var smaller = new DrivingHudSettings { OverlayText = new Dictionary<string, DrivingHudSettings.TextAppearance>
                { [OverlayComponentKeys.Speed] = new DrivingHudSettings.TextAppearance { Scale = 0.6 } } };
            BitmapSource normalSpeed = (BitmapSource)preview.Invoke(null, new object[] { OverlayComponentKeys.Speed, "racing", baseline })!;
            BitmapSource smallSpeed = (BitmapSource)preview.Invoke(null, new object[] { OverlayComponentKeys.Speed, "racing", smaller })!;
            byte[] normalPixels = new byte[normalSpeed.PixelWidth * normalSpeed.PixelHeight * 4];
            byte[] smallPixels = new byte[normalPixels.Length];
            normalSpeed.CopyPixels(normalPixels, normalSpeed.PixelWidth * 4, 0);
            smallSpeed.CopyPixels(smallPixels, smallSpeed.PixelWidth * 4, 0);
            AssertTrue(normalPixels.Where((value, index) => value != smallPixels[index]).Take(100).Count() > 50);
        }
    }
}
