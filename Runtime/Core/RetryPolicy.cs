using System;

namespace WakeQuery
{
    /// <summary>
    /// Controls how failed query fetches are retried.
    /// </summary>
    /// <remarks>
    /// Retry delays are scheduled on the client's runtime clock, not with <c>Task.Delay</c>, so they are deterministic
    /// under <c>ManualQueryRuntime</c>. Mutations are never retried.
    /// </remarks>
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

        /// <summary>Gets a policy that makes a single attempt and never retries. This is the default.</summary>
        public static RetryPolicy None { get; } = new(
            1,
            TimeSpan.Zero,
            TimeSpan.Zero,
            null,
            DelayKind.None);

        /// <summary>Gets the maximum number of attempts, including the first one.</summary>
        public int MaxAttempts { get; }

        internal TimeSpan InitialDelay { get; }

        internal TimeSpan MaximumDelay { get; }

        internal Func<Exception, bool> ShouldRetry { get; }

        /// <summary>Creates a policy that waits the same delay before every retry.</summary>
        /// <param name="maxAttempts">The maximum number of attempts, including the first one. Must be at least 1.</param>
        /// <param name="delay">The delay before each retry. Must not be negative.</param>
        /// <param name="shouldRetry">
        /// Optional predicate that decides whether a failure is retried. When <see langword="null"/>, every failure is retried.
        /// </param>
        /// <returns>The policy.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxAttempts"/> is less than 1, or <paramref name="delay"/> is negative.
        /// </exception>
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

        /// <summary>Creates a policy whose delay doubles after each failed attempt.</summary>
        /// <param name="maxAttempts">The maximum number of attempts, including the first one. Must be at least 1.</param>
        /// <param name="initialDelay">The delay before the first retry. Must not be negative.</param>
        /// <param name="maximumDelay">The upper bound for any delay, or <see langword="null"/> for no bound.</param>
        /// <param name="shouldRetry">
        /// Optional predicate that decides whether a failure is retried. When <see langword="null"/>, every failure is retried.
        /// </param>
        /// <returns>The policy.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxAttempts"/> is less than 1, <paramref name="initialDelay"/> is negative,
        /// or <paramref name="maximumDelay"/> is less than <paramref name="initialDelay"/>.
        /// </exception>
        /// <example>
        /// With <c>initialDelay</c> = 1s and <c>maximumDelay</c> = 8s, retries wait 1s, 2s, 4s, 8s, 8s, and so on.
        /// </example>
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
