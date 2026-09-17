using System;

namespace WakeQuery
{
    /// <summary>
    /// Optional settings for a <see cref="QueryClient"/>.
    /// </summary>
    public sealed class QueryClientOptions
    {
        /// <summary>Creates client options.</summary>
        /// <param name="unhandledException">
        /// Receives exceptions thrown by observer listeners, mutation listeners, and diagnostic listeners.
        /// When <see langword="null"/>, the runtime default is used: <c>Debug.LogException</c> in Unity,
        /// or rethrowing in <c>ManualQueryRuntime</c>.
        /// </param>
        /// <param name="diagnosticListener">Optional listener for query and mutation lifecycle events.</param>
        public QueryClientOptions(
            Action<Exception> unhandledException = null,
            IQueryDiagnosticListener diagnosticListener = null)
        {
            UnhandledException = unhandledException;
            DiagnosticListener = diagnosticListener;
        }

        /// <summary>Gets the handler for exceptions thrown by listeners, or <see langword="null"/> to use the runtime default.</summary>
        public Action<Exception> UnhandledException { get; }

        /// <summary>Gets the diagnostic listener, or <see langword="null"/>.</summary>
        public IQueryDiagnosticListener DiagnosticListener { get; }
    }
}
