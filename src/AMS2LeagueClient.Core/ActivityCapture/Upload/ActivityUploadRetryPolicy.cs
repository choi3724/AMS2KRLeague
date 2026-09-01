using System;

namespace AMS2LeagueClient.Core.ActivityCapture.Upload
{
    public sealed class ActivityUploadRetryPolicy
    {
        private readonly TimeSpan _baseDelay;
        private readonly TimeSpan _maximumDelay;
        private readonly double _jitterRatio;
        private readonly IActivityUploadJitter _jitter;

        public ActivityUploadRetryPolicy(ActivityUploadQueueOptions options, IActivityUploadJitter? jitter = null)
        {
            ActivityUploadQueueOptions validated = (options ?? throw new ArgumentNullException(nameof(options))).ValidatedCopy();
            _baseDelay = validated.BaseRetryDelay;
            _maximumDelay = validated.MaximumRetryDelay;
            _jitterRatio = validated.RetryJitterRatio;
            _jitter = jitter ?? new RandomActivityUploadJitter();
        }

        public TimeSpan GetDelay(int failedAttemptCount)
        {
            if (failedAttemptCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(failedAttemptCount));
            }

            int exponent = Math.Min(failedAttemptCount - 1, 30);
            double delayMilliseconds = _baseDelay.TotalMilliseconds * Math.Pow(2, exponent);
            delayMilliseconds = Math.Min(delayMilliseconds, _maximumDelay.TotalMilliseconds);

            double sample = _jitter.NextDouble();
            if (double.IsNaN(sample) || double.IsInfinity(sample) || sample < 0 || sample >= 1)
            {
                throw new InvalidOperationException("Upload jitter must return a value in the range [0, 1).");
            }

            double jitterFactor = 1 + (((sample * 2) - 1) * _jitterRatio);
            delayMilliseconds = Math.Max(0, delayMilliseconds * jitterFactor);
            delayMilliseconds = Math.Min(delayMilliseconds, _maximumDelay.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(delayMilliseconds);
        }
    }
}
