using System;

namespace WakeQuery
{
    /// <summary>
    /// Identifies the lifecycle event reported by a <see cref="QueryDiagnosticEvent"/>.
    /// </summary>
    public enum QueryDiagnosticKind
    {
        /// <summary>A new shared fetch started for a query key.</summary>
        FetchStarted,

        /// <summary>A caller or observer joined a fetch that was already running.</summary>
        FetchJoined,

        /// <summary>A fetch attempt failed and another attempt was scheduled by the <see cref="RetryPolicy"/>.</summary>
        FetchRetryScheduled,

        /// <summary>A fetch completed and its data was written to the cache.</summary>
        FetchSucceeded,

        /// <summary>A fetch failed and no further retry will be attempted.</summary>
        FetchFailed,

        /// <summary>A shared fetch was canceled.</summary>
        FetchCanceled,

        /// <summary>A fetch result arrived after the cache entry was changed or replaced, so it was ignored.</summary>
        ObsoleteResultDiscarded,

        /// <summary>A query was marked stale by <see cref="QueryClient.Invalidate(QueryFilter)"/>.</summary>
        Invalidated,

        /// <summary>A query was removed by <see cref="QueryClient.Remove(QueryFilter)"/>.</summary>
        Removed,

        /// <summary>An unused query was removed after its <see cref="QueryPolicy.UnusedFor"/> period elapsed.</summary>
        Evicted,

        /// <summary>A mutation execution started.</summary>
        MutationStarted,

        /// <summary>A mutation execution succeeded and its success effects were applied.</summary>
        MutationSucceeded,

        /// <summary>A mutation execution failed, or its success effects threw.</summary>
        MutationFailed,

        /// <summary>A mutation execution was canceled.</summary>
        MutationCanceled
    }

    /// <summary>
    /// Describes one query or mutation lifecycle event delivered to an <see cref="IQueryDiagnosticListener"/>.
    /// </summary>
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

        /// <summary>Gets the kind of event.</summary>
        public QueryDiagnosticKind Kind { get; }

        /// <summary>
        /// Gets the query key formatted as text, for example <c>player-profile/42</c>,
        /// or <see langword="null"/> for mutation events.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets the fetch or mutation execution identifier, or <c>0</c> when the event is not tied to an execution.
        /// </summary>
        public long ExecutionId { get; }

        /// <summary>Gets the number of failed attempts of the fetch at the time of the event, or <c>0</c> when not applicable.</summary>
        public int FailureCount { get; }

        /// <summary>Gets the exception associated with the event, or <see langword="null"/>.</summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// Receives lifecycle events from a <see cref="QueryClient"/> for logging, metrics, or debugging.
    /// </summary>
    /// <remarks>
    /// Register a listener through <see cref="QueryClientOptions"/>. Events are raised on the client owner's thread.
    /// Exceptions thrown by the listener are reported to <see cref="QueryClientOptions.UnhandledException"/>.
    /// </remarks>
    public interface IQueryDiagnosticListener
    {
        /// <summary>Called for each diagnostic event raised by the client.</summary>
        /// <param name="diagnosticEvent">The event that occurred.</param>
        void OnEvent(QueryDiagnosticEvent diagnosticEvent);
    }
}
