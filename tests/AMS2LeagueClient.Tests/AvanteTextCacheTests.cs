using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AvanteTextCachePreservesRecentReadouts()
        {
            var view = new AvanteClusterView();
            var cache = (IDictionary)typeof(AvanteClusterView).GetField("_glyphs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            var start = DateTimeOffset.UtcNow;
            int sample = 0;
            void Speed(int speed) => view.SetSample(new DrivingTelemetrySample(start.AddMilliseconds(sample++ * 7),
                1, 0, 0, 0, 0, 0, speed / 3.6, 3, rpm: 6000, maxRpm: 8000));
            for (int speed = 0; speed <= 200; speed++) Speed(speed);
            var key = ("200", 108.0, true, false, true);
            var saved = ((Geometry, Rect, Geometry?, Geometry?))cache[key]!;
            for (int speed = 201; speed <= 350; speed++) Speed(speed);
            AssertTrue(cache.Contains(key));
            AssertTrue(ReferenceEquals(saved.Item1, (((Geometry, Rect, Geometry?, Geometry?))cache[key]!).Item1));
            Speed(200);
            AssertEqual("200", view.SpeedText);
            AssertTrue(ReferenceEquals(saved.Item1, (((Geometry, Rect, Geometry?, Geometry?))cache[key]!).Item1));
            for (int speed = 351; speed <= 1500; speed++) Speed(speed);
            AssertTrue(cache.Count <= 256);
            Speed(200);
            AssertEqual("200", view.SpeedText);
            AssertTrue(cache.Count <= 256);
        }
    }
}
