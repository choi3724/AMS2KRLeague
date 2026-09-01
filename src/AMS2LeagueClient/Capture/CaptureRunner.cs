using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Events;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Capture
{
    public static class CaptureRunner
    {
        public static IReadOnlyList<string> Run(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            var files = new List<string>();

            var waiting = new ClientStatusViewModel();
            waiting.SetWaiting();
            files.Add(CaptureStatus(outputDirectory, "00_client_waiting.png", waiting, "REAL CLIENT RENDER • AMS2 NOT RUNNING"));

            files.Add(CaptureOverlay(outputDirectory, "01_DEMO_position_gained.png", false, 1920, 1080, OverlayEventType.PositionGained));
            files.Add(CaptureOverlay(outputDirectory, "02_DEMO_personal_best.png", false, 1920, 1080, OverlayEventType.PersonalBest));
            files.Add(CaptureOverlay(outputDirectory, "03_DEMO_final_lap.png", false, 1920, 1080, OverlayEventType.FinalLap));
            files.Add(CaptureOverlay(outputDirectory, "04_DEMO_finish.png", false, 1920, 1080, OverlayEventType.Finish));
            files.Add(CaptureOverlay(outputDirectory, "DEMO_overlay_basic.png", false, 1920, 1080, null));
            files.Add(CaptureOverlay(outputDirectory, "DEMO_overlay_diagnostic.png", true, 1920, 1080, null));
            files.Add(CaptureOverlay(outputDirectory, "DEMO_overlay_2560x1440.png", false, 2560, 1440, OverlayEventType.RaceFastestLap));
            files.Add(CaptureOverlay(outputDirectory, "DEMO_overlay_3440x1440.png", false, 3440, 1440, OverlayEventType.PositionGained));
            files.AddRange(CaptureBroadcastFixtures(outputDirectory));

            var unavailable = new ClientStatusViewModel();
            unavailable.SetSharedMemoryUnavailable(4242);
            unavailable.WindowText = "Game window: detected • HUD hidden until valid telemetry";
            files.Add(CaptureStatus(outputDirectory, "10_client_shared_memory_unavailable.png", unavailable, "REAL CLIENT ERROR RENDER • NO GAME SETTING CHANGED"));

            string manifest = Path.Combine(outputDirectory, "capture-manifest.txt");
            File.WriteAllLines(manifest, files);
            files.Add(manifest);
            return files;
        }

        private static string CaptureStatus(string outputDirectory, string fileName, ClientStatusViewModel viewModel, string evidenceLabel)
        {
            const int width = 1200;
            const int height = 700;
            var root = CreateSurface(width, height, evidenceLabel, "1200 × 700 WPF capture surface");
            var status = new ClientStatusView { DataContext = viewModel, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            root.Children.Add(status);
            string path = Path.Combine(outputDirectory, fileName);
            SavePng(root, width, height, path);
            return path;
        }

        private static string CaptureOverlay(string outputDirectory, string fileName, bool diagnostic, int width, int height, OverlayEventType? eventType)
        {
            var root = CreateSurface(
                width,
                height,
                "DEMO / SIMULATION • NOT REAL AMS2",
                width + " × " + height + " • transparent external overlay placement test");

            var overlay = new OverlayShellView { Width = width, Height = height };
            overlay.SetLayout(width, height);
            overlay.SetViewModel(DemoSnapshotFactory.CreateShell(diagnostic, eventType), false);
            root.Children.Add(overlay);

            string path = Path.Combine(outputDirectory, fileName);
            SavePng(root, width, height, path);
            return path;
        }

        private static IReadOnlyList<string> CaptureBroadcastFixtures(string outputDirectory)
        {
            var files = new List<string>
            {
                CaptureBroadcastState(outputDirectory, "FIXTURE_drive_through.png", "DT", "#FFB454", "드라이브스루", "드라이브스루 수행 필요", "페널티"),
                CaptureBroadcastState(outputDirectory, "FIXTURE_stop_go.png", "SG", "#FF7777", "스톱 앤 고", "스톱 앤 고 수행 필요", "페널티"),
                CaptureBroadcastState(outputDirectory, "FIXTURE_red_flag.png", string.Empty, "#FF5C5C", "적색기", "세션 중단 상태", "적색기")
            };
            files.AddRange(CaptureTowerReorder(outputDirectory));
            return files;
        }

        private static string CaptureBroadcastState(
            string outputDirectory,
            string fileName,
            string towerStatus,
            string accent,
            string title,
            string message,
            string stateLabel)
        {
            OverlayShellViewModel shell = DemoSnapshotFactory.CreateShell(false);
            shell.Timing.EnvironmentLabel = "FIXTURE · 상태 UI 검증";
            RankingRowViewModel? player = shell.Timing.RankingRows.FirstOrDefault(row => row.IsPlayer);
            if (player != null && !string.IsNullOrEmpty(towerStatus))
            {
                player.Status = towerStatus;
                player.StatusColor = accent;
            }
            shell.RaceControl = new RaceControlViewModel
            {
                IsVisible = true,
                IsExpanded = true,
                EventId = "FIXTURE:" + towerStatus + ":" + stateLabel,
                Title = title,
                DriverLine = string.IsNullOrEmpty(towerStatus) ? string.Empty : "P29  ENG-IceBlasT",
                Message = message,
                StateLabel = stateLabel,
                HistoryText = "FIXTURE 데이터 · 실제 AMS2 검출 아님",
                CountText = "1",
                Accent = accent
            };
            return CaptureShell(outputDirectory, fileName, shell, "FIXTURE / SIMULATION • NOT REAL AMS2", title + " 상태 표시 검증");
        }

        private static IReadOnlyList<string> CaptureTowerReorder(string outputDirectory)
        {
            const int width = 3440;
            const int height = 1440;
            var files = new List<string>();
            Grid root = CreateSurface(width, height, "FIXTURE / SIMULATION • NOT REAL AMS2", "타이밍 타워 재정렬 340ms 전환 검증");
            var overlay = new OverlayShellView { Width = width, Height = height };
            overlay.SetLayout(width, height);
            OverlayShellViewModel before = DemoSnapshotFactory.CreateShell(false);
            before.Timing.EnvironmentLabel = "FIXTURE · 재정렬 전";
            overlay.SetViewModel(before, false);
            root.Children.Add(overlay);

            string beforePath = Path.Combine(outputDirectory, "FIXTURE_tower_reorder_before.png");
            SavePng(root, width, height, beforePath);
            files.Add(beforePath);

            OverlayShellViewModel after = DemoSnapshotFactory.CreateShell(false);
            after.Timing.EnvironmentLabel = "FIXTURE · 재정렬 진행";
            List<RankingRowViewModel> rows = after.Timing.RankingRows.Select(CloneRow).ToList();
            if (rows.Count >= 4)
            {
                RankingRowViewModel first = rows[2];
                RankingRowViewModel second = rows[3];
                string firstPosition = first.Position;
                first.Position = second.Position;
                second.Position = firstPosition;
                rows[2] = second;
                rows[3] = first;
            }
            after.Timing.RankingRows = rows;
            overlay.SetViewModel(after, true);
            PumpDispatcher(TimeSpan.FromMilliseconds(170));
            string midPath = Path.Combine(outputDirectory, "FIXTURE_tower_reorder_mid.png");
            SavePng(root, width, height, midPath);
            files.Add(midPath);

            after.Timing.EnvironmentLabel = "FIXTURE · 재정렬 완료";
            PumpDispatcher(TimeSpan.FromMilliseconds(300));
            string afterPath = Path.Combine(outputDirectory, "FIXTURE_tower_reorder_after.png");
            SavePng(root, width, height, afterPath);
            files.Add(afterPath);
            return files;
        }

        private static RankingRowViewModel CloneRow(RankingRowViewModel row)
        {
            return new RankingRowViewModel
            {
                ParticipantIndex = row.ParticipantIndex,
                Position = row.Position,
                Name = row.Name,
                Class = row.Class,
                Lap = row.Lap,
                IsPlayer = row.IsPlayer,
                Background = row.Background,
                Accent = row.Accent,
                Foreground = row.Foreground,
                Status = row.Status,
                StatusColor = row.StatusColor
            };
        }

        private static string CaptureShell(string outputDirectory, string fileName, OverlayShellViewModel shell, string evidenceLabel, string detail)
        {
            const int width = 3440;
            const int height = 1440;
            Grid root = CreateSurface(width, height, evidenceLabel, detail);
            var overlay = new OverlayShellView { Width = width, Height = height };
            overlay.SetLayout(width, height);
            overlay.SetViewModel(shell, false);
            root.Children.Add(overlay);
            string path = Path.Combine(outputDirectory, fileName);
            SavePng(root, width, height, path);
            return path;
        }

        private static void PumpDispatcher(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = duration
            };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static Grid CreateSurface(int width, int height, string evidenceLabel, string detail)
        {
            var root = new Grid { Width = width, Height = height };
            root.Background = new LinearGradientBrush(
                Color.FromRgb(15, 29, 42),
                Color.FromRgb(4, 10, 16),
                35.0);

            var lineCanvas = new Canvas { IsHitTestVisible = false, Opacity = 0.26 };
            for (int x = 0; x < width; x += 96)
            {
                lineCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(68, 103, 126)),
                    StrokeThickness = 1
                });
            }

            for (int y = 0; y < height; y += 96)
            {
                lineCanvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = 0,
                    X2 = width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(68, 103, 126)),
                    StrokeThickness = 1
                });
            }
            root.Children.Add(lineCanvas);

            var banner = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 99, 64, 147)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = evidenceLabel,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13
                }
            };
            root.Children.Add(banner);

            root.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = new SolidColorBrush(Color.FromRgb(105, 129, 149)),
                FontFamily = new FontFamily(HudTypography.FamilyChain),
                FontSize = 14,
                Margin = new Thickness(26),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            });
            return root;
        }

        private static void SavePng(FrameworkElement element, int width, int height, string path)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }
    }
}
