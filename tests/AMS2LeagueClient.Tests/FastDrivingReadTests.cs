using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using AMS2LeagueClient.Core.Telemetry;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void FastDrivingReadDoesNotFeedRecording()
        {
            var fixture = new RawFixtureBuilder().SetViewedIndex(3).SetViewedVehicleTelemetry();
            var expected = Parse(fixture);
            string name = "ams2-hud-fixture-" + Guid.NewGuid().ToString("N");
            using var mapping = MemoryMappedFile.CreateNew(name, SharedMemoryLayout.RequiredBytes);
            using var writer = mapping.CreateViewAccessor();
            writer.WriteArray(0, fixture.Buffer, 0, SharedMemoryLayout.RequiredBytes);
            using var reader = new SharedMemoryReader(name);
            AssertEqual(TelemetryReadStatus.Success, reader.TryRead().Status);
            long recordingReads = reader.SuccessfulSnapshots;
            var watch = Stopwatch.StartNew();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 6000; i++)
            {
                var sample = reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw);
                AssertNotNull(sample);
                AssertEqual("260 km/h", sample!.SpeedText); AssertEqual("4", sample.GearText);
                AssertEqual(7, sample.Generation);
            }
            Console.WriteLine("PROOF display-only reads=6000 meanUs=" + (watch.Elapsed.TotalMilliseconds * 1000 / 6000).ToString("F2")
                + " bytesPerReadWithAssertions=" + ((GC.GetAllocatedBytesForCurrentThread() - allocated) / 6000));
            AssertEqual(recordingReads, reader.SuccessfulSnapshots);
            AssertEqual(0L, reader.SequenceDrops);
            byte[] after = new byte[SharedMemoryLayout.RequiredBytes];
            writer.ReadArray(0, after, 0, after.Length);
            AssertTrue(after.SequenceEqual(fixture.Buffer.Take(after.Length)));
            AssertNull(reader.TryReadDriving(2, 7, expected.GameStateRaw, expected.SessionStateRaw));
            AssertNull(reader.TryReadDriving(64, 7, expected.GameStateRaw, expected.SessionStateRaw));
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw + 1));
            writer.Write(SharedMemoryLayout.SequenceNumber, 1U);
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw));
            writer.Write(SharedMemoryLayout.SequenceNumber, 2U);
            writer.Write(SharedMemoryLayout.Brake, float.NaN);
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw)!.Pedals[0]);
            writer.Write(SharedMemoryLayout.Version, 999U);
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw));
            reader.Reset();
            AssertNull(reader.TryReadDriving(3, 7, expected.GameStateRaw, expected.SessionStateRaw));
            AssertEqual(recordingReads, reader.SuccessfulSnapshots);
            Console.WriteLine("PROOF high-rate display does not write SHM or increment recording counters; version/owner/session/sequence guards pass");
        }
    }
}
