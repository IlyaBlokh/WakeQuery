using System;

namespace WakeQuery
{
    /// <summary>
    /// The cached outcome of a query, independent of whether a fetch is currently running.
    /// </summary>
    public enum QueryStatus
    {
        /// <summary>No fetch has completed and no data has been set.</summary>
        Empty,
        /// <summary>The latest fetch succeeded, or data was written with <see cref="QueryClient.SetData{T}"/>.</summary>
        Success,
        /// <summary>The latest fetch failed after all retries. Previously cached data, if any, is kept.</summary>
        Error
    }

    /// <summary>
    /// What the shared fetch for a query is doing right now.
    /// </summary>
    public enum FetchActivity
    {
        /// <summary>No fetch is running.</summary>
        Idle,
        /// <summary>A fetch attempt is running.</summary>
        Fetching,
        /// <summary>A fetch attempt failed and the next attempt is waiting for its retry delay.</summary>
        RetryDelay
    }

    /// <summary>
    /// An immutable snapshot of a query as seen by one <see cref="QueryObserver{T}"/>.
    /// </summary>
    /// <typeparam name="T">The query result type.</typeparam>
    /// <remarks>
    /// <see cref="Status"/> and <see cref="FetchActivity"/> are independent. A background refresh can report
    /// <see cref="QueryStatus.Success"/>, <see cref="HasData"/> = <see langword="true"/>, and
    /// <see cref="WakeQuery.FetchActivity.Fetching"/> at the same time.
    /// </remarks>
    public readonly struct QueryState<T>
    {
        internal QueryState(
            QueryStatus status,
            FetchActivity fetchActivity,
            bool hasData,
            T data,
            Exception error,
            bool isStale,
            int failureCount,
            DateTimeOffset? updatedAt,
            long revision)
        {
            Status = status;
            FetchActivity = fetchActivity;
            HasData = hasData;
            Data = data;
            Error = error;
            IsStale = isStale;
            FailureCount = failureCount;
            UpdatedAt = updatedAt;
            Revision = revision;
        }

        /// <summary>Gets the cached outcome.</summary>
        public QueryStatus Status { get; }

        /// <summary>Gets the current fetch activity.</summary>
        public FetchActivity FetchActivity { get; }

        /// <summary>Gets whether a fetch attempt is running. Equivalent to <c>FetchActivity == FetchActivity.Fetching</c>.</summary>
        public bool IsFetching => FetchActivity == FetchActivity.Fetching;

        /// <summary>Gets whether the cache holds data for this query. Data is kept when a later fetch fails.</summary>
        public bool HasData { get; }

        /// <summary>Gets the cached data, or <c>default</c> when <see cref="HasData"/> is <see langword="false"/>.</summary>
        public T Data { get; }

        /// <summary>Gets the error from the most recent failed fetch attempt, or <see langword="null"/>. Cleared when data is written.</summary>
        public Exception Error { get; }

        /// <summary>
        /// Gets whether the data is stale for this observer: there is no data, the query was invalidated or its
        /// latest fetch failed, or the observer's <see cref="QueryPolicy.StaleAfter"/> has elapsed since the data was written.
        /// </summary>
        public bool IsStale { get; }

        /// <summary>Gets the number of failed attempts of the current or latest fetch. Reset to <c>0</c> when data is written.</summary>
        public int FailureCount { get; }

        /// <summary>Gets the wall-clock time the data was last written, or <see langword="null"/> if never.</summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>Gets a number that increases every time the observer publishes a new state.</summary>
        public long Revision { get; }
    }
}
