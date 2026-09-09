using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;
using AMS2LeagueClient.Vr;
using Valve.VR;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void VrSettingsAndTransforms()
        {
            var old = JsonSerializer.Deserialize<OverlayLayoutProfile>("{\"schema\":1}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            AssertEqual(OverlayOutputMode.Monitor, old.VrHud.Normalize().Output);
            VrHudSettings s = new VrHudSettings { Output = (OverlayOutputMode)100, WidthMetres = double.NaN,
                DistanceMetres = -100, YawDegrees = 500, VerticalMetres = double.PositiveInfinity }.Normalize();
            AssertEqual(OverlayOutputMode.Monitor, s.Output);
            AssertEqual(2.0, s.WidthMetres); AssertEqual(0.5, s.DistanceMetres);
            AssertEqual(60.0, s.YawDegrees); AssertEqual(0.0, s.VerticalMetres);
            HmdMatrix34_t relative = SteamVrOverlayRuntime.RelativeTransform(new VrHudSettings());
            AssertEqual(1f, relative.m0); AssertEqual(1f, relative.m5); AssertEqual(1f, relative.m10);
            AssertEqual(-2f, relative.m11);
            HmdMatrix34_t anchor = new HmdMatrix34_t { m0 = 1, m5 = 1, m10 = 1, m3 = 3, m7 = 1, m11 = 4 };
            HmdMatrix34_t result = SteamVrOverlayRuntime.Multiply(anchor, relative);
            AssertEqual(3f, result.m3); AssertEqual(1f, result.m7); AssertEqual(2f, result.m11);
            HmdMatrix34_t rotated = SteamVrOverlayRuntime.RelativeTransform(new VrHudSettings { YawDegrees = 45, PitchDegrees = 30 });
            AssertTrue(Math.Abs(rotated.m0 * rotated.m0 + rotated.m1 * rotated.m1 + rotated.m2 * rotated.m2 - 1) < 0.00001);
            byte[] colour = { 10, 20, 30, 128, 0, 0, 0, 0 };
            OverlayWindow.ConvertBgraToRgba(colour);
            AssertTrue(colour.SequenceEqual(new byte[] { 30, 20, 10, 128, 0, 0, 0, 0 }));
            bool rejected = false;
            try { _ = new VrFrame(1281, 1, new byte[1281 * 4]); } catch (ArgumentException) { rejected = true; }
            AssertTrue(rejected);
        }

        private static void VrConnectionLifecycle()
        {
            var fake = new FakeVrRuntime();
            int captures = 0;
            var messages = new List<string>();
            var controller = new VrOverlayController(fake, () => { captures++; return new VrFrame(1, 1, new byte[4]); }, messages.Add);
            DateTimeOffset start = DateTimeOffset.UtcNow;
            controller.Tick(start, true);
            AssertEqual(0, fake.Connects);
            controller.Configure(new VrHudSettings { Output = OverlayOutputMode.Both });
            controller.Tick(start, true);
            controller.Tick(start.AddSeconds(1), true);
            AssertEqual(1, fake.Connects); AssertEqual(0, captures);
            fake.Succeed = true;
            controller.Tick(start.AddSeconds(5), true);
            AssertTrue(controller.Connected); AssertEqual(1, captures); AssertEqual(1, fake.Submits);
            for (int i = 0; i < 60; i++) controller.Tick(start.AddSeconds(5), true);
            AssertEqual(1, fake.Submits);
            fake.CanSubmit = false;
            controller.Tick(start.AddSeconds(6), true);
            AssertEqual(1, captures); // no frame allocations while native upload is pending
            fake.CanSubmit = true;
            controller.Tick(start.AddSeconds(7), false);
            AssertTrue(fake.Hides > 0); AssertEqual(1, fake.Submits);
            controller.Tick(start.AddSeconds(7.5), true, 456);
            AssertEqual(1, fake.Submits); // a different VR scene cannot receive the AMS2 HUD
            fake.ThrowSubmit = true;
            controller.Tick(start.AddSeconds(8), true);
            AssertFalse(controller.Connected);
            AssertTrue(messages.Last().Contains("재시도"));
            int connections = fake.Connects;
            controller.Tick(start.AddSeconds(9), true);
            AssertEqual(connections, fake.Connects);
            fake.ThrowSubmit = false;
            controller.Tick(start.AddSeconds(13), true);
            AssertTrue(controller.Connected);
            fake.PollAlive = false;
            controller.Tick(start.AddSeconds(14), true);
            AssertFalse(controller.Connected);
            fake.PollAlive = true;
            controller.Tick(start.AddSeconds(19), true);
            AssertTrue(controller.Connected);
            controller.Configure(new VrHudSettings { Output = OverlayOutputMode.Monitor });
            AssertFalse(controller.Connected); AssertEqual(0u, controller.SceneProcessId);
            int before = fake.Connects;
            controller.Tick(start.AddSeconds(30), true);
            AssertEqual(before, fake.Connects);
            var empty = new VrOverlayController(fake, () => null, messages.Add);
            empty.Configure(new VrHudSettings { Output = OverlayOutputMode.Vr });
            empty.Tick(start.AddSeconds(31), true);
            AssertTrue(messages.Last().Contains("표시할 패널 없음"));
            empty.Dispose();
            before = fake.Connects;
            controller.Dispose();
            controller.Tick(start.AddSeconds(40), true);
            AssertEqual(before, fake.Connects);
        }

        private sealed class FakeVrRuntime : IVrOverlayRuntime
        {
            public bool Succeed, ThrowSubmit;
            public bool PollAlive = true;
            public int Connects, Submits, Hides, Closes, Recenters;
            public uint SceneProcessId => 123;
            public bool CanSubmit { get; set; } = true;
            public bool Connect(out string status) { Connects++; status = Succeed ? "연결됨" : "연결 대기"; return Succeed; }
            public bool Poll() => PollAlive;
            public void Submit(VrFrame frame, VrHudSettings settings)
            {
                if (ThrowSubmit) throw new InvalidOperationException("synthetic upload failure");
                Submits++;
            }
            public void Hide() => Hides++;
            public void Recenter() => Recenters++;
            public void Dispose() => Closes++;
        }

        private static void VrOutputRenderingAndPersistence()
        {
            string path = Path.Combine(Path.GetTempPath(), "ams2-vr-" + Guid.NewGuid() + ".json");
            var prior = Application.Current.Windows.Cast<Window>().ToHashSet();
            var overlay = new OverlayWindow(false, path);
            try
            {
                overlay.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
                overlay.ShowDemoAt(-5000, -5000, 96);
                PumpDispatcher();
                Window[] panels = Application.Current.Windows.Cast<Window>().Where(w => !prior.Contains(w)).ToArray();
                CaptureLayout((FrameworkElement)overlay.Content, "vr-desktop-source");
                overlay.SaveVrHudSettings(new VrHudSettings { Output = OverlayOutputMode.Vr, DistanceMetres = 3, FollowHead = false });
                AssertTrue(panels.All(w => w.Opacity == 0));
                AssertTrue(overlay.GetStyleState().ClickThrough);
                VrFrame vr = overlay.CaptureVrFrame()!;
                AssertEqual(1280, vr.Width); AssertEqual(720, vr.Height);
                AssertTrue(vr.Rgba.Where((_, i) => i % 4 == 3).Any(value => value > 0));
                byte[] vrOnly = (byte[])vr.Rgba.Clone();
                if (_layoutCaptureDirectory != null)
                {
                    Directory.CreateDirectory(_layoutCaptureDirectory);
                    byte[] bgra = (byte[])vrOnly.Clone();
                    OverlayWindow.ConvertBgraToRgba(bgra);
                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(vr.Width, vr.Height, 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32, null, bgra, vr.Width * 4);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(_layoutCaptureDirectory, "vr-composite.png"));
                    encoder.Save(output);
                }
                overlay.SaveVrHudSettings(new VrHudSettings { Output = OverlayOutputMode.Both });
                PumpDispatcher();
                byte[] both = overlay.CaptureVrFrame()!.Rgba;
                int vrInk = vrOnly.Where((_, i) => i % 4 == 3).Count(value => value > 0);
                int bothInk = both.Where((_, i) => i % 4 == 3).Count(value => value > 0);
                Console.WriteLine("PROOF VR-only alphaPixels=" + vrInk + " both=" + bothInk);
                AssertTrue(vrInk > 10000 && vrInk > bothInk * 0.85 && vrInk < bothInk * 1.15);
                // Timing animations can advance; desktop opacity must not blank the VR output.
                AssertTrue(panels.All(w => w.Opacity == 1));
                var background = new GameWindowSnapshot(IntPtr.Zero, -5000, -5000, 1920, 1080, 96, false, true, 0);
                AssertTrue(ReferenceEquals(background, overlay.ResolveOutputWindow(background, false)));
                GameWindowSnapshot vrWindow = overlay.ResolveOutputWindow(background, true)!;
                AssertTrue(vrWindow.IsForeground && !vrWindow.IsMinimized);
                AssertTrue(panels.All(w => w.Opacity == 0));
                overlay.SetGameOutputState(true, true);
                overlay.BeginLayoutEdit();
                PumpDispatcher();
                byte[] withChrome = (byte[])overlay.CaptureVrFrame()!.Rgba.Clone();
                foreach (Window panel in panels) ((Grid)panel.Content).Children[1].Visibility = Visibility.Collapsed;
                byte[] withoutChrome = overlay.CaptureVrFrame()!.Rgba;
                AssertTrue(withChrome.SequenceEqual(withoutChrome));
                overlay.EndLayoutEdit(true);
                overlay.SaveVrHudSettings(new VrHudSettings { Output = OverlayOutputMode.Vr, DistanceMetres = 3, FollowHead = false });
                overlay.ResetLayout();
                using (JsonDocument saved = JsonDocument.Parse(File.ReadAllText(path)))
                    AssertEqual(3.0, saved.RootElement.GetProperty("vrHud").GetProperty("distanceMetres").GetDouble());
                var reloaded = new OverlayWindow(false, path);
                try
                {
                    AssertEqual(OverlayOutputMode.Vr, reloaded.GetVrHudSettings().Output);
                    AssertEqual(3.0, reloaded.GetVrHudSettings().DistanceMetres);
                    AssertFalse(reloaded.GetVrHudSettings().FollowHead);
                }
                finally { reloaded.Close(); }
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 15; i++) overlay.CaptureVrFrame();
                watch.Stop();
                Console.WriteLine("PROOF VR CPU composite 1280x720 meanMs=" + (watch.Elapsed.TotalMilliseconds / 15).ToString("F2")
                    + " frameBytes=" + (1280 * 720 * 4) + " cap=15fps, no native VR upload");
                overlay.HideOverlay();
                AssertNull(overlay.CaptureVrFrame());
            }
            finally { overlay.EndLayoutEdit(false); overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }

        private static void VrSettingsControlsAndSdk()
        {
            int applies = 0, centers = 0;
            VrHudSettings? selected = null;
            var window = new VrSettingsWindow(new VrHudSettings(), value => { applies++; selected = value; }, () => centers++);
            try
            {
                window.Left = -5000; window.Top = -5000; window.Show(); PumpDispatcher();
                var root = (FrameworkElement)window.Content;
                Descendants<ComboBox>(root).Single().SelectedIndex = 2;
                Descendants<Button>(root).Single(b => b.Content as string == "적용").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertEqual(1, applies); AssertEqual(OverlayOutputMode.Both, selected!.Output);
                Descendants<Button>(root).Single(b => b.Content as string == "정면 재설정").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertEqual(1, centers);
                CaptureLayout(root, "vr-settings");
            }
            finally { window.Close(); }
            string native = Path.Combine(AppContext.BaseDirectory, "openvr_api.dll");
            AssertEqual("BAB8AC6EF64E68A9CA53315B0014D131088584B2EFDFA6DB511D67EC03CFCB4A",
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))));
            AssertTrue(File.Exists(Path.Combine(AppContext.BaseDirectory, "ThirdParty", "OpenVR", "LICENSE.txt")));
            IntPtr handle = NativeLibrary.Load(native);
            try { AssertTrue(NativeLibrary.TryGetExport(handle, "VR_InitInternal2", out _)); }
            finally { NativeLibrary.Free(handle); }
            Console.WriteLine("PROOF OpenVR 2.15.6 x64 DLL loads and export exists; no headset/runtime initialization");
        }
    }
}
