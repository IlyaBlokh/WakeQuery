using System;

namespace WakeQuery
{
    public sealed class QueryClientOptions
    {
        public QueryClientOptions(
            Action<Exception> unhandledException = null,
            IQueryDiagnosticListener diagnosticListener = null)
        {
            UnhandledException = unhandledException;
            DiagnosticListener = diagnosticListener;
        }

        public Action<Exception> UnhandledException { get; }

        public IQueryDiagnosticListener DiagnosticListener { get; }
    }
}
