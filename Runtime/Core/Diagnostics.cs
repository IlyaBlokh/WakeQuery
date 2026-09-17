using System;

namespace WakeQuery
{
    public enum QueryDiagnosticKind
    {
        FetchStarted,
        FetchJoined,
        FetchRetryScheduled,
        FetchSucceeded,
        FetchFailed,
        FetchCanceled,
        ObsoleteResultDiscarded,
        Invalidated,
        Removed,
        Evicted,
        MutationStarted,
        MutationSucceeded,
        MutationFailed,
        MutationCanceled
    }

    public readonly struct QueryDiagnosticEvent
    {
        internal QueryDiagnosticEvent(
            QueryDiagnosticKind kind,
            string key,
            long executionId,
            int failureCount,
            Exception exception)
        {
            Kind = kind;
            Key = key;
            ExecutionId = executionId;
            FailureCount = failureCount;
            Exception = exception;
        }

        public QueryDiagnosticKind Kind { get; }

        public string Key { get; }

        public long ExecutionId { get; }

        public int FailureCount { get; }

        public Exception Exception { get; }
    }

    public interface IQueryDiagnosticListener
    {
        void OnEvent(QueryDiagnosticEvent diagnosticEvent);
    }
}
