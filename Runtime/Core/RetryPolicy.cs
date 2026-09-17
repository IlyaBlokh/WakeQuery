using System;

namespace WakeQuery
{
    public sealed class RetryPolicy
    {
        private enum DelayKind
        {
            None,
            Fixed,
            Exponential
        }

        private RetryPolicy(
            int maxAttempts,
            TimeSpan initialDelay,
            TimeSpan maximumDelay,
            Func<Exception, bool> shouldRetry,
            DelayKind delayKind)
        {
            MaxAttempts = maxAttempts;
            InitialDelay = initialDelay;
            MaximumDelay = maximumDelay;
            ShouldRetry = shouldRetry;
            _delayKind = delayKind;
        }

        private readonly DelayKind _delayKind;

        public static RetryPolicy None { get; } = new RetryPolicy(
            1,
            TimeSpan.Zero,
            TimeSpan.Zero,
            null,
            DelayKind.None);

        public int MaxAttempts { get; }

        internal TimeSpan InitialDelay { get; }

        internal TimeSpan MaximumDelay { get; }

        internal Func<Exception, bool> ShouldRetry { get; }

        public static RetryPolicy Fixed(
            int maxAttempts,
            TimeSpan delay,
            Func<Exception, bool> shouldRetry = null)
        {
            Validate(maxAttempts, delay);
            return new RetryPolicy(
                maxAttempts,
                delay,
                delay,
                shouldRetry,
                DelayKind.Fixed);
        }

        public static RetryPolicy Exponential(
            int maxAttempts,
            TimeSpan initialDelay,
            TimeSpan? maximumDelay = null,
            Func<Exception, bool> shouldRetry = null)
        {
            Validate(maxAttempts, initialDelay);
            TimeSpan maximum = maximumDelay ?? TimeSpan.MaxValue;
            if (maximum < initialDelay)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDelay),
                    "Maximum delay must be greater than or equal to the initial delay.");
            }

            return new RetryPolicy(
                maxAttempts,
                initialDelay,
                maximum,
                shouldRetry,
                DelayKind.Exponential);
        }

        internal bool AllowsRetry(Exception exception, int failedAttempts)
        {
            return failedAttempts < MaxAttempts &&
                   (ShouldRetry == null || ShouldRetry(exception));
        }

        internal TimeSpan GetDelay(int failedAttempts)
        {
            if (_delayKind != DelayKind.Exponential || failedAttempts <= 1)
            {
                return InitialDelay;
            }

            long ticks = InitialDelay.Ticks;
            long maximumTicks = MaximumDelay.Ticks;
            if (ticks == 0 || ticks >= maximumTicks)
            {
                return TimeSpan.FromTicks(Math.Min(ticks, maximumTicks));
            }

            int doublings = failedAttempts - 1;
            while (doublings > 0 && ticks < maximumTicks)
            {
                if (ticks > maximumTicks / 2)
                {
                    ticks = maximumTicks;
                    break;
                }

                ticks *= 2;
                doublings--;
            }

            return TimeSpan.FromTicks(ticks);
        }

        private static void Validate(int maxAttempts, TimeSpan delay)
        {
            if (maxAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }
        }
    }
}
