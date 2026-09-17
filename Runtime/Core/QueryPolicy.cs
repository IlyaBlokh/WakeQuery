using System;

namespace WakeQuery
{
    /// <summary>
    /// Controls freshness, cache retention, retries, polling, and automatic refetching for a <see cref="QueryDefinition{T}"/>.
    /// </summary>
    /// <remarks>
    /// Freshness is evaluated per observer: two observers of the same key with different <see cref="StaleAfter"/>
    /// values can see different <see cref="QueryState{T}.IsStale"/> values for the same cached data.
    /// </remarks>
    public sealed class QueryPolicy
    {
        /// <summary>
        /// Gets the default policy: data is immediately stale, unused entries are kept for 5 minutes,
        /// no retries, no polling, and refetch on focus and reconnect.
        /// </summary>
        public static QueryPolicy Default { get; } = new();

        /// <summary>Creates a query policy.</summary>
        /// <param name="staleAfter">How long fetched data stays fresh. Defaults to <see cref="TimeSpan.Zero"/> (always stale).</param>
        /// <param name="unusedFor">How long an entry with no observers is kept before eviction. Defaults to 5 minutes.</param>
        /// <param name="retry">How failed fetches are retried. Defaults to <see cref="RetryPolicy.None"/>.</param>
        /// <param name="pollEvery">
        /// Refetch interval while an observer is active and the application is focused, or <see langword="null"/> to disable polling.
        /// </param>
        /// <param name="refetchOnFocus">Whether observers refetch stale data when the application regains focus.</param>
        /// <param name="refetchOnReconnect">Whether observers refetch stale data when a reconnect is reported.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="staleAfter"/> or <paramref name="unusedFor"/> is negative, or <paramref name="pollEvery"/> is not positive.
        /// </exception>
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

        /// <summary>Gets how long fetched data stays fresh after it is written.</summary>
        public TimeSpan StaleAfter { get; }

        /// <summary>Gets how long an entry with no observers is kept before eviction.</summary>
        /// <remarks>When several definitions share a key, the longest value is used.</remarks>
        public TimeSpan UnusedFor { get; }

        /// <summary>Gets the retry policy for failed fetches.</summary>
        public RetryPolicy Retry { get; }

        /// <summary>Gets the polling interval, or <see langword="null"/> when polling is disabled.</summary>
        public TimeSpan? PollEvery { get; }

        /// <summary>Gets whether observers refetch stale data when the application regains focus.</summary>
        public bool RefetchOnFocus { get; }

        /// <summary>Gets whether observers refetch stale data when a reconnect is reported.</summary>
        public bool RefetchOnReconnect { get; }
    }
}
