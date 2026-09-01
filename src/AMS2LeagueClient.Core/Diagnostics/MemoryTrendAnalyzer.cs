using System;
using System.Collections.Generic;

namespace AMS2LeagueClient.Core.Diagnostics
{
    public sealed class MemoryTrendPoint
    {
        public MemoryTrendPoint(TimeSpan elapsed, long workingSetBytes, long privateBytes, long gcHeapBytes)
        {
            Elapsed = elapsed;
            WorkingSetBytes = workingSetBytes;
            PrivateBytes = privateBytes;
            GcHeapBytes = gcHeapBytes;
        }

        public TimeSpan Elapsed { get; }
        public long WorkingSetBytes { get; }
        public long PrivateBytes { get; }
        public long GcHeapBytes { get; }
    }

    public sealed class MemoryTrendAssessment
    {
        public MemoryTrendAssessment(bool hasEnoughData, bool stable, double workingSetGrowthMiBPerHour, long maximumWorkingSetBytes)
        {
            HasEnoughData = hasEnoughData;
            Stable = stable;
            WorkingSetGrowthMiBPerHour = workingSetGrowthMiBPerHour;
            MaximumWorkingSetBytes = maximumWorkingSetBytes;
        }

        public bool HasEnoughData { get; }
        public bool Stable { get; }
        public double WorkingSetGrowthMiBPerHour { get; }
        public long MaximumWorkingSetBytes { get; }
    }

    public sealed class MemoryTrendAnalyzer
    {
        private const double StableGrowthLimitMiBPerHour = 20.0;
        private static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(30);

        public MemoryTrendAssessment Assess(IReadOnlyList<MemoryTrendPoint> points)
        {
            if (points.Count < 3)
            {
                return new MemoryTrendAssessment(false, false, 0, MaximumWorkingSet(points));
            }

            double durationHours = (points[points.Count - 1].Elapsed - points[0].Elapsed).TotalHours;
            if (durationHours <= 0)
            {
                return new MemoryTrendAssessment(false, false, 0, MaximumWorkingSet(points));
            }

            double growthMiB = (points[points.Count - 1].WorkingSetBytes - points[0].WorkingSetBytes) / 1048576.0;
            double growthPerHour = growthMiB / durationHours;
            bool enough = points[points.Count - 1].Elapsed - points[0].Elapsed >= MinimumDuration;
            bool stable = enough && growthPerHour <= StableGrowthLimitMiBPerHour;
            return new MemoryTrendAssessment(enough, stable, growthPerHour, MaximumWorkingSet(points));
        }

        private static long MaximumWorkingSet(IReadOnlyList<MemoryTrendPoint> points)
        {
            long maximum = 0;
            foreach (MemoryTrendPoint point in points)
            {
                maximum = Math.Max(maximum, point.WorkingSetBytes);
            }

            return maximum;
        }
    }
}
