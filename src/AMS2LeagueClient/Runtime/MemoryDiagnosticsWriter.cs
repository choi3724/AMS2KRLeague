using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace AMS2LeagueClient.Runtime
{
    public sealed class MemoryDiagnosticsWriter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly StreamWriter _writer;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private readonly TimeSpan _interval;
        private Timer? _timer;

        public MemoryDiagnosticsWriter(string path, TimeSpan interval)
        {
            string absolutePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? AppContext.BaseDirectory);
            bool writeHeader = !File.Exists(absolutePath) || new FileInfo(absolutePath).Length == 0;
            _writer = new StreamWriter(new FileStream(absolutePath, FileMode.Append, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false));
            _writer.AutoFlush = true;
            _interval = interval;
            if (writeHeader)
            {
                _writer.WriteLine("timestampUtc,elapsedSeconds,privateBytes,workingSetBytes,gcHeapBytes,totalAllocatedBytes,gen0,gen1,gen2,handleCount,threadCount");
            }
        }

        public void Start()
        {
            _timer = new Timer(Sample, null, TimeSpan.Zero, _interval);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
            lock (_gate)
            {
                _writer.Dispose();
            }
        }

        private void Sample(object? state)
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                process.Refresh();
                GCMemoryInfo gc = GC.GetGCMemoryInfo();
                int threadCount;
                ProcessThreadCollection threads = process.Threads;
                try
                {
                    threadCount = threads.Count;
                }
                finally
                {
                    foreach (ProcessThread thread in threads)
                    {
                        thread.Dispose();
                    }
                }
                string line = string.Join(",", new[]
                {
                    DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    _elapsed.Elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),
                    process.PrivateMemorySize64.ToString(CultureInfo.InvariantCulture),
                    process.WorkingSet64.ToString(CultureInfo.InvariantCulture),
                    gc.HeapSizeBytes.ToString(CultureInfo.InvariantCulture),
                    GC.GetTotalAllocatedBytes(false).ToString(CultureInfo.InvariantCulture),
                    GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture),
                    GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture),
                    GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture),
                    process.HandleCount.ToString(CultureInfo.InvariantCulture),
                    threadCount.ToString(CultureInfo.InvariantCulture)
                });
                lock (_gate)
                {
                    _writer.WriteLine(line);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
