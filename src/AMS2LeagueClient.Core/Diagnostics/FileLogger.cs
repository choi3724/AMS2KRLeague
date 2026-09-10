using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AMS2LeagueClient.Core.Diagnostics
{
    public sealed class FileLogger : IDisposable, IAsyncDisposable
    {
        private readonly Channel<string> _lines;
        private readonly string _directory;
        private readonly Task _writer;
        private long _dropped, _written, _failures;
        public FileLogger(string directory, int capacity = 2048)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _directory = Path.GetFullPath(directory);
            FilePath = Path.Combine(_directory, "client-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".log");
            _lines = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
            { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait });
            _writer = Task.Run(WriteLoopAsync);
        }
        public string FilePath { get; }
        public long DroppedLines => Interlocked.Read(ref _dropped);
        public long WrittenLines => Interlocked.Read(ref _written);
        public long WriteFailures => Interlocked.Read(ref _failures);
        public void Info(string eventName, string details) => Write("INFO", eventName, details);
        public void Warning(string eventName, string details) => Write("WARN", eventName, details);
        // Exception text can contain a remote response or credentials. Log codes
        // and correlation separately at the call site, never arbitrary messages.
        public void Error(string eventName, Exception exception) => Write("ERROR", eventName, exception.GetType().Name);
        private void Write(string level, string eventName, string details)
        {
            string Clean(string value, int limit) => (value ?? "").Substring(0, Math.Min(value?.Length ?? 0, limit)).Replace('\r', ' ').Replace('\n', ' ');
            string line = DateTimeOffset.Now.ToString("O") + " [" + level + "] " + Clean(eventName, 128) + " " + Clean(details, 4096);
            // Drop newest on overflow. Accepted lines retain channel order; no
            // producer waits for a disk write or a logger-owned monitor lock.
            if (!_lines.Writer.TryWrite(line)) Interlocked.Increment(ref _dropped);
        }
        private async Task WriteLoopAsync()
        {
            StreamWriter? file = null;
            try
            {
                await foreach (string line in _lines.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    try
                    {
                        if (file == null)
                        {
                            Directory.CreateDirectory(_directory);
                            file = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write,
                                FileShare.ReadWrite | FileShare.Delete, 16384), new UTF8Encoding(false));
                        }
                        await file.WriteLineAsync(line).ConfigureAwait(false);
                        Interlocked.Increment(ref _written);
                        if (!_lines.Reader.TryPeek(out _)) await file.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        Interlocked.Increment(ref _failures);
                        try { file?.Dispose(); } catch (IOException) { Interlocked.Increment(ref _failures); }
                        file = null;
                    }
                }
                if (file != null)
                {
                    if (DroppedLines > 0 || WriteFailures > 0)
                        await file.WriteLineAsync("LOG_DRAIN written=" + WrittenLines + " dropped=" + DroppedLines + " ioFailures=" + WriteFailures).ConfigureAwait(false);
                    await file.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Interlocked.Increment(ref _failures); }
            finally { try { file?.Dispose(); } catch (IOException) { Interlocked.Increment(ref _failures); } }
        }
        public ValueTask DisposeAsync() { _lines.Writer.TryComplete(); return new ValueTask(_writer); }
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
