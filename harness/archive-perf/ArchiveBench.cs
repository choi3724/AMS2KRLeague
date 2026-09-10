using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using AMS2LeagueClient.Core.FutureTelemetry;

internal static class ArchiveBench
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
    private static double ThreadCpuMs() { if (!GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user)) throw new System.ComponentModel.Win32Exception(); return (kernel + user) / 10000d; }
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch(Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static void Run(string[] args)
    {
        string output = Path.GetFullPath(args[0]); int chunkMs = int.Parse(args[1]) * 1000;
        string root = Path.Combine(Path.GetDirectoryName(output)!, "archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var identity = new TelemetryArchiveIdentity { SessionId="bench-session", SessionFingerprint="bench-session-fingerprint",
            WitnessId="bench-witness", AttemptId="bench-attempt", AttemptNumber=1 };
        var options = new TelemetryArchiveOptions { ChunkDurationMs=chunkMs, InputChannelCapacity=512 };
        Type compactType = typeof(LocalDurableTelemetryArchive).Assembly.GetType("AMS2LeagueClient.Core.FutureTelemetry.CompactTelemetryChunkStore")!;
        object compact = Activator.CreateInstance(compactType, root, identity, options)!;
        var encodeCommit = (Func<TelemetryChunkEnvelope, TelemetryChunkCommitOutcome>)compactType.GetMethod("Commit")!
            .CreateDelegate(typeof(Func<TelemetryChunkEnvelope, TelemetryChunkCommitOutcome>), compact);
        double commitCpu = 0, commitWall = 0; int commits = 0;
        Func<TelemetryChunkEnvelope, TelemetryChunkCommitOutcome> commit = envelope => {
            double cpu = ThreadCpuMs(); long time = Stopwatch.GetTimestamp();
            try { return encodeCommit(envelope); }
            finally { commitCpu += ThreadCpuMs() - cpu; commitWall += Stopwatch.GetElapsedTime(time).TotalMilliseconds; commits++; }
        };
        ConstructorInfo ctor = typeof(LocalDurableTelemetryArchive).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single(value => value.GetParameters().Length == 5);
        var archive = (LocalDurableTelemetryArchive)ctor.Invoke(new object?[] {root, identity, options, commit, null});
        object channel = typeof(LocalDurableTelemetryArchive).GetField("_channel", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(archive)!;
        object reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        PropertyInfo count = reader.GetType().GetProperty("Count")!;
        using Process process = Process.GetCurrentProcess();
        var timeTotal = Stopwatch.StartNew(); double processCpuStart=process.TotalProcessorTime.TotalMilliseconds;
        DateTimeOffset start = new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero);
        long peakWorkingSet = 0; int frames = 0;
        for(int ms=0; ms<3600000; ms+=50)
        {
            // Producer backpressure belongs to this accelerated fixture only.
            // It does not force early chunk flush or change the product channel.
            while((int)count.GetValue(reader)! > 256) Thread.Sleep(1);
            var rows = new List<ReplayParticipantSample>(32);
            for(int car=0; car<32; car++)
            {
                double progress = ms*.06 + car*50;
                double angle = (progress % 5000) / 5000 * Math.PI*2;
                rows.Add(new ReplayParticipantSample {ParticipantRef=car+1, Slot=car, Generation=1,
                    NameSnapshot="Fixture driver " + car, VehicleRef="fixture-gt3", VehicleClassRef="GT3",
                    Lap=(int)(progress/5000)+1, LapDistanceMeters=progress%5000, RacePosition=car+1,
                    WorldX=Math.Cos(angle)*500, WorldY=1.5, WorldZ=Math.Sin(angle)*500,
                    RaceStateRaw=2, PitStateRaw=0, HeadingRadians=angle, SpeedMetersPerSecond=60});
            }
            var frame = new TelemetryFrameSample {CapturedAtUtc=start.AddMilliseconds(ms), SessionElapsedMs=ms,
                Participants=rows, RaceStateRaw=2, LocalDriver=new DriverTelemetrySample {
                    LocalParticipantResolved=true, SourceParticipantRef=1, DriverRef=1,
                    Lap=rows[0].Lap, Sector=1, LapDistanceMeters=rows[0].LapDistanceMeters,
                    WorldX=rows[0].WorldX, WorldY=rows[0].WorldY, WorldZ=rows[0].WorldZ,
                    SpeedMetersPerSecond=60, Rpm=6500, GearRaw=4, Throttle=.7, Brake=0,
                    Clutch=0, Steering=.1, UnfilteredThrottle=.7, UnfilteredBrake=0, UnfilteredClutch=0, UnfilteredSteering=.1
                }};
            if(!archive.TryCaptureFrame(frame)) throw new InvalidOperationException("Fixture input dropped");
            frames++;
            if(frames%1000==0) { process.Refresh(); peakWorkingSet=Math.Max(peakWorkingSet,process.WorkingSet64); }
        }
        long flushStart=Stopwatch.GetTimestamp();
        archive.DisposeAsync().AsTask().GetAwaiter().GetResult();
        double flushMs=Stopwatch.GetElapsedTime(flushStart).TotalMilliseconds;
        if(archive.Counters.DroppedMessages!=0 || archive.Counters.CommitFailures!=0 || !archive.CompletionReport.FinalizeAcknowledged)
            throw new InvalidOperationException("Archive did not complete without loss");
        FileInfo[] gzip=Directory.GetFiles(root,"*.gz",SearchOption.AllDirectories).Select(path=>new FileInfo(path)).ToArray();
        process.Refresh(); peakWorkingSet=Math.Max(peakWorkingSet,process.WorkingSet64);
        var result=new {chunkSeconds=chunkMs/1000, durationSeconds=3600,participants=32,sourceFrames=frames,
            fixture="Accelerated 20Hz replay+private driver; stock replay gates/500ms world; no incidents, network or SHM",
            gzipFiles=gzip.Length,gzipBytes=gzip.Sum(file=>file.Length),averageGzipBytes=gzip.Average(file=>(double)file.Length),
            localFiles=Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length,
            commitCalls=commits,encodingAndCommitThreadCpuMs=commitCpu,encodingAndCommitWallMs=commitWall,
            totalProcessCpuMs=process.TotalProcessorTime.TotalMilliseconds-processCpuStart,wallSeconds=timeTotal.Elapsed.TotalSeconds,
            peakSampledWorkingSetBytes=peakWorkingSet,shutdownDrainMs=flushMs,
            dropped=archive.Counters.DroppedMessages,commitFailures=archive.Counters.CommitFailures,
            crashUncommittedNominalSeconds=chunkMs/1000,
            caveat="Disk retry/backlog can exceed nominal loss window. Counts are files/commits, not OS WriteFile syscall counts.",archiveRoot=root};
        File.WriteAllText(output,JsonSerializer.Serialize(result,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine(output);
    }
}
