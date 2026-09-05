using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Overlay;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    // Opt-in, visible desktop fixture. No AMS2 read, credentials or network.
    // Rendering callbacks/changed animation samples are not GPU presentation timings.
    internal static class OverlayMotionProbe
    {
        public static void Run()
        {
            string layout = Path.Combine(Path.GetTempPath(), "ams2-motion-" + Guid.NewGuid().ToString("N") + ".json");
            var window = new OverlayWindow(false, layout);
            window.SetViewModel(DemoSnapshotFactory.CreateShell(false), false);
            window.ShowDemoAt(40, 40, 96);
            window.UpdateLayout();
            OverlayHudViewTarget(window, null);
            OverlayHudViewTarget(window, 144);
            OverlayHudViewTarget(window, 144, rankingMotion: true);
            window.Close();
        }

        private static void OverlayHudViewTarget(Window window, int? requestedRate, bool rankingMotion = false)
        {
            var view = Descendants<UserControl>(window)
                .First(element => element is AMS2LeagueClient.Presentation.OverlayHudView);
            var transform = new TranslateTransform();
            if (!rankingMotion) view.RenderTransform = transform;
            var animation = new DoubleAnimation(-40, 40, TimeSpan.FromSeconds(2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Timeline.SetDesiredFrameRate(animation, requestedRate);
            if (!rankingMotion) transform.BeginAnimation(TranslateTransform.XProperty, animation);
            var elapsed = Stopwatch.StartNew();
            var frame = new DispatcherFrame();
            var intervals = new List<double>();
            double previousMs = 0;
            double previousX = double.NaN;
            int changed = 0;
            int moves = 0;
            double maximumStep = 0;
            bool previousSwap = false;
            double moveStartedMs = -1000;
            ItemsControl items = Descendants<ItemsControl>(view).First();
            int trackedParticipant = ((RankingRowViewModel)items.Items[0]).ParticipantIndex;
            TimeSpan previousRenderingTime = TimeSpan.MinValue;
            var dataTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            int tick = 0;
            dataTimer.Tick += (sender, args) =>
            {
                var model = DemoSnapshotFactory.CreateShell(false);
                foreach (var row in model.Timing.AllRankingRows)
                    row.CurrentTime = "1:40." + (++tick % 1000).ToString("000");
                if (rankingMotion)
                {
                    bool swapped = (int)(elapsed.Elapsed.TotalMilliseconds / 700) % 2 != 0;
                    if (swapped != previousSwap)
                    {
                        previousSwap = swapped;
                        moveStartedMs = elapsed.Elapsed.TotalMilliseconds;
                        moves++;
                    }
                    var rows = model.Timing.AllRankingRows.ToArray();
                    if (swapped)
                    {
                        string position = rows[0].Position;
                        rows[0].Position = rows[1].Position;
                        rows[1].Position = position;
                        (rows[0], rows[1]) = (rows[1], rows[0]);
                    }
                    model.Timing.AllRankingRows = rows;
                    model.Timing.RankingRows = model.Timing.RankingRows
                        .OrderBy(row => TimingTowerTransitionTracker.ParsePosition(row.Position)).ToArray();
                }
                ((OverlayWindow)window).SetViewModel(model);
            };
            EventHandler rendering = (sender, args) =>
            {
                var renderingTime = ((RenderingEventArgs)args).RenderingTime;
                if (renderingTime == previousRenderingTime) return;
                previousRenderingTime = renderingTime;
                double ms = elapsed.Elapsed.TotalMilliseconds;
                if (ms < 1000) return;
                if (rankingMotion && ms - moveStartedMs > 300)
                {
                    previousMs = 0;
                    return;
                }
                double position = transform.X;
                if (rankingMotion)
                {
                    int index = Enumerable.Range(0, items.Items.Count)
                        .First(i => ((RankingRowViewModel)items.Items[i]).ParticipantIndex == trackedParticipant);
                    var presenter = (ContentPresenter)items.ItemContainerGenerator.ContainerFromIndex(index);
                    position = presenter.TranslatePoint(new Point(), items).Y;
                }
                if (!double.IsNaN(previousX)) maximumStep = Math.Max(maximumStep, Math.Abs(position - previousX));
                if (previousMs > 0)
                {
                    intervals.Add(ms - previousMs);
                    if (Math.Abs(position - previousX) > 0.001) changed++;
                }
                previousX = position;
                previousMs = ms;
            };
            var end = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            end.Tick += (sender, args) => { end.Stop(); frame.Continue = false; };
            CompositionTarget.Rendering += rendering;
            dataTimer.Start();
            end.Start();
            Dispatcher.PushFrame(frame);
            CompositionTarget.Rendering -= rendering;
            dataTimer.Stop();
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            double seconds = intervals.Sum() / 1000;
            var sorted = intervals.OrderBy(ms => ms).ToArray();
            Console.WriteLine("MOTION_PROBE requested=" + (requestedRate?.ToString() ?? "system")
                + " target=" + (rankingMotion ? "actual-ranking-row" : "whole-tower-fixture")
                + " reorders=" + moves
                + " maxPositionStepDip=" + maximumStep.ToString("F2")
                + " tier=" + (RenderCapability.Tier >> 16)
                + " callbacks=" + intervals.Count
                + " renderCallbacksHz=" + (intervals.Count / Math.Max(0.001, seconds)).ToString("F1")
                + " changedAnimationHz=" + (changed / Math.Max(0.001, seconds)).ToString("F1")
                + " p95Ms=" + (sorted.Length > 0 ? sorted[(int)((sorted.Length - 1) * 0.95)] : 0).ToString("F2"));
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed) yield return typed;
                foreach (T nested in Descendants<T>(child)) yield return nested;
            }
        }
    }
}
