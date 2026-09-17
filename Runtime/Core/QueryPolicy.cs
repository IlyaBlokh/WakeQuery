using System;

namespace WakeQuery
{
    public sealed class QueryPolicy
    {
        public static QueryPolicy Default { get; } = new QueryPolicy();

        public QueryPolicy(
            TimeSpan? staleAfter = null,
            TimeSpan? unusedFor = null,
            RetryPolicy retry = null,
            TimeSpan? pollEvery = null,
            bool refetchOnFocus = true,
            bool refetchOnReconnect = true)
        {
            StaleAfter = staleAfter ?? TimeSpan.Zero;
            UnusedFor = unusedFor ?? TimeSpan.FromMinutes(5);
            Retry = retry ?? RetryPolicy.None;
            PollEvery = pollEvery;
            RefetchOnFocus = refetchOnFocus;
            RefetchOnReconnect = refetchOnReconnect;

            if (StaleAfter < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(staleAfter));
            }

            if (UnusedFor < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(unusedFor));
            }

            if (PollEvery.HasValue && PollEvery.Value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(pollEvery));
            }
        }

        public TimeSpan StaleAfter { get; }

        public TimeSpan UnusedFor { get; }

        public RetryPolicy Retry { get; }

        public TimeSpan? PollEvery { get; }

        public bool RefetchOnFocus { get; }

        public bool RefetchOnReconnect { get; }
    }
}
