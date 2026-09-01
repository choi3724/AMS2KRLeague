using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AMS2LeagueClient.Core.ActivityCapture.Upload;

namespace AMS2LeagueActivity.Tests
{
    internal sealed class TestCase
    {
        public TestCase(string name, Action test)
        {
            Name = name;
            Test = test;
        }

        public string Name { get; }
        public Action Test { get; }
    }

    internal static class AssertEx
    {
        public static void True(bool value, string message = "Expected true.")
        {
            if (!value) throw new InvalidOperationException(message);
        }

        public static void False(bool value, string message = "Expected false.")
        {
            if (value) throw new InvalidOperationException(message);
        }

        public static void Null(object? value, string message = "Expected null.")
        {
            if (value != null) throw new InvalidOperationException(message);
        }

        public static void NotNull(object? value, string message = "Expected non-null.")
        {
            if (value == null) throw new InvalidOperationException(message);
        }

        public static void Equal<T>(T expected, T actual, string? message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message ?? ("Expected " + expected + ", got " + actual + "."));
            }
        }

        public static void NotEqual<T>(T unexpected, T actual, string? message = null)
        {
            if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            {
                throw new InvalidOperationException(message ?? ("Did not expect " + actual + "."));
            }
        }

        public static T Single<T>(IEnumerable<T> values)
        {
            T[] array = values.ToArray();
            if (array.Length != 1) throw new InvalidOperationException("Expected one item, got " + array.Length + ".");
            return array[0];
        }
    }

    internal sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string purpose)
        {
            Root = Path.Combine(Path.GetTempPath(), "ams2krleague-player-tests", purpose, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    internal sealed class MutableClock : IActivityUploadClock
    {
        public MutableClock(DateTimeOffset value)
        {
            UtcNow = value;
        }

        public DateTimeOffset UtcNow { get; set; }
    }

    internal sealed class FixedJitter : IActivityUploadJitter
    {
        private readonly double _value;

        public FixedJitter(double value)
        {
            _value = value;
        }

        public double NextDouble() => _value;
    }

    internal sealed class SequenceTransport : IActivityUploadTransport
    {
        private readonly Queue<ActivityUploadTransportResult> _results;

        public SequenceTransport(params ActivityUploadTransportResult[] results)
        {
            _results = new Queue<ActivityUploadTransportResult>(results);
        }

        public int Calls { get; private set; }

        public Task<ActivityUploadTransportResult> SendAsync(ActivityUploadItem item, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (_results.Count == 0) throw new InvalidOperationException("No transport result was configured.");
            return Task.FromResult(_results.Dequeue());
        }
    }

    internal sealed class BlockingTransport : IActivityUploadTransport
    {
        private readonly TaskCompletionSource<bool> _entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _release.TrySetResult(true);

        public async Task<ActivityUploadTransportResult> SendAsync(ActivityUploadItem item, CancellationToken cancellationToken)
        {
            _entered.TrySetResult(true);
            using (cancellationToken.Register(() => _release.TrySetCanceled(cancellationToken)))
            {
                await _release.Task.ConfigureAwait(false);
            }
            return ActivityUploadTransportResult.Http(201);
        }
    }

    internal static class FileHash
    {
        public static string Sha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
