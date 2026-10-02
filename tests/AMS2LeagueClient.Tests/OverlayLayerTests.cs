using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Process;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        [DllImport("user32.dll", EntryPoint = "GetWindow")]
        private static extern IntPtr LayerGetWindow(IntPtr handle, uint command);

        private static bool IsAbove(Window front, Window back)
        {
            IntPtr wanted = new WindowInteropHelper(front).Handle;
            IntPtr current = new WindowInteropHelper(back).Handle;
            for (int i = 0; i < 10000 && current != IntPtr.Zero; i++)
            {
                current = LayerGetWindow(current, 3); // GW_HWNDPREV: walk toward the front.
                if (current == wanted) return true;
            }
            return false;
        }

        private static void OverlayLayerOrderPersistsAndRenders()
        {
            var legacy = JsonSerializer.Deserialize<OverlayLayoutProfile>("{\"schema\":1}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            legacy.SetDrivingPanelDefaults();
            AssertTrue(legacy.LayerOrder.SequenceEqual(OverlayComponentKeys.All));
            var normalized = OverlayLayerOrder.Normalize(new[] { "gear", "GEAR", "unknown", "speed" });
            AssertEqual(OverlayComponentKeys.All.Length, normalized.Count);
            AssertEqual(OverlayComponentKeys.Gear, normalized[0]);
            AssertEqual(OverlayComponentKeys.Speed, normalized[1]);
            var skippedHidden = OverlayLayerOrder.Move(OverlayComponentKeys.All, OverlayComponentKeys.Speed,
                OverlayLayerMove.Forward, new[] { OverlayComponentKeys.Speed, OverlayComponentKeys.RaceControl });
            AssertTrue(skippedHidden.IndexOf(OverlayComponentKeys.Speed) > skippedHidden.IndexOf(OverlayComponentKeys.RaceControl));

            string path = Path.Combine(Path.GetTempPath(), "ams2-layers-" + Guid.NewGuid().ToString("N") + ".json");
            var desktop = new GameWindowSnapshot(IntPtr.Zero, -5000, -5000, 1280, 720, 96, true, false, 0);
            var overlay = new OverlayWindow(false, path);
            try
            {
                overlay.BeginLayoutPreview(false, desktop);
                PumpDispatcher();
                Window speed = Application.Current.Windows.Cast<Window>().Single(item => item.Title == "AMS2 속도계");
                Window gear = Application.Current.Windows.Cast<Window>().Single(item => item.Title == "AMS2 기어");
                AssertTrue(speed.IsVisible && gear.IsVisible);
                AssertTrue(IsAbove(gear, speed));
                var initial = overlay.GetLayerOrder();
                var page = new OverlayComponentSettingsWindow(OverlayComponentKeys.Speed,
                    overlay.GetDrivingHudSettings(), initial, overlay.GetVisibleLayerComponents())
                    { ShowActivated = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
                page.LayerOrderChanged += (_, __) => overlay.PreviewLayerOrder(page.LayerOrder);
                page.Show(); PumpDispatcher();
                Button front = Descendants<Button>(page).Single(item => AutomationProperties.GetName(item) == "겹침 순서 맨 앞으로");
                AssertTrue(front.IsEnabled);
                front.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertEqual(OverlayComponentKeys.Speed, page.LayerOrder.Last());
                AssertTrue(IsAbove(speed, gear));
                AssertFalse(File.Exists(path)); // Preview alone never writes the layer change.
                page.Close();
                overlay.PreviewLayerOrder(initial); // Cancel restores the visible order.
                AssertTrue(IsAbove(gear, speed));

                var changed = OverlayLayerOrder.Move(initial, OverlayComponentKeys.Speed, OverlayLayerMove.Front);
                overlay.PreviewLayerOrder(changed);
                overlay.SaveComponentSettings(overlay.GetDrivingHudSettings(), changed);
                AssertEqual(OverlayComponentKeys.Speed, overlay.GetLayerOrder().Last());
                var stored = JsonSerializer.Deserialize<OverlayLayoutProfile>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                AssertEqual(OverlayComponentKeys.Speed, stored.LayerOrder.Last());
                overlay.EndLayoutEdit(true);
                overlay.ShowAt(desktop); PumpDispatcher();
                AssertTrue(IsAbove(speed, gear));
                overlay.ShowAt(new GameWindowSnapshot(IntPtr.Zero, -4800, -4900, 1280, 720, 96, true, false, 0));
                PumpDispatcher();
                AssertTrue(IsAbove(speed, gear)); // Repositioning may not reset Z order.
                overlay.Close();

                overlay = new OverlayWindow(false, path);
                overlay.ShowAt(desktop); PumpDispatcher();
                speed = Application.Current.Windows.Cast<Window>().Single(item => item.Title == "AMS2 속도계" && item.IsVisible);
                gear = Application.Current.Windows.Cast<Window>().Single(item => item.Title == "AMS2 기어" && item.IsVisible);
                AssertTrue(IsAbove(speed, gear));
                overlay.ResetLayout();
                AssertTrue(overlay.GetLayerOrder().SequenceEqual(OverlayComponentKeys.All));

                var nPage = new DrivingHudSettingsWindow(new DrivingHudSettings(), layerComponent: OverlayComponentKeys.AvanteCluster,
                    layerOrder: OverlayComponentKeys.All)
                    { ShowActivated = false, Left = -5000, Top = -5000, WindowStartupLocation = WindowStartupLocation.Manual };
                nPage.Show(); PumpDispatcher();
                Descendants<Button>(nPage).Single(item => AutomationProperties.GetName(item) == "겹침 순서 맨 뒤로")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertEqual(OverlayComponentKeys.AvanteCluster, nPage.LayerOrder[0]);
                nPage.Close();
                Console.WriteLine("PROOF Win32 topmost order changed immediately, survived live resize/reload, canceled preview restored, N page and legacy defaults verified");
            }
            finally { overlay.EndLayoutEdit(false); overlay.Close(); if (File.Exists(path)) File.Delete(path); }
        }
    }
}
