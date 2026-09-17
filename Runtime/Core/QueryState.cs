using System;

namespace WakeQuery
{
    public enum QueryStatus
    {
        Empty,
        Success,
        Error
    }

    public enum FetchActivity
    {
        Idle,
        Fetching,
        RetryDelay
    }

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

        public QueryStatus Status { get; }

        public FetchActivity FetchActivity { get; }

        public bool IsFetching => FetchActivity == FetchActivity.Fetching;

        public bool HasData { get; }

        public T Data { get; }

        public Exception Error { get; }

        public bool IsStale { get; }

        public int FailureCount { get; }

        public DateTimeOffset? UpdatedAt { get; }

        public long Revision { get; }
    }
}
