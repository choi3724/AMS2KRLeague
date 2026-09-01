using System;

namespace AMS2LeagueClient.Core.Diagnostics
{
    public sealed class SequenceCounterSample
    {
        public SequenceCounterSample(long retries, long drops, long retryDelta, long dropDelta)
        {
            Retries = retries;
            Drops = drops;
            RetryDelta = retryDelta;
            DropDelta = dropDelta;
        }

        public long Retries { get; }
        public long Drops { get; }
        public long RetryDelta { get; }
        public long DropDelta { get; }
    }

    public sealed class SequenceCounterSampler
    {
        private readonly TimeSpan _interval;
        private DateTimeOffset? _lastEmission;
        private long _lastEmittedRetries;
        private long _lastEmittedDrops;

        public SequenceCounterSampler(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            _interval = interval;
        }

        public SequenceCounterSample? Observe(DateTimeOffset now, long retries, long drops)
        {
            if (retries == _lastEmittedRetries && drops == _lastEmittedDrops)
            {
                return null;
            }

            if (_lastEmission.HasValue && now - _lastEmission.Value < _interval)
            {
                return null;
            }

            var sample = new SequenceCounterSample(
                retries,
                drops,
                retries - _lastEmittedRetries,
                drops - _lastEmittedDrops);
            _lastEmittedRetries = retries;
            _lastEmittedDrops = drops;
            _lastEmission = now;
            return sample;
        }
    }
}
