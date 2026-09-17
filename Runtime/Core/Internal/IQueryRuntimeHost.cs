using System;

namespace WakeQuery.Internal
{
    internal interface IQueryRuntimeHost
    {
        int OwnerThreadId { get; }

        TimeSpan Elapsed { get; }

        DateTimeOffset UtcNow { get; }

        Action<Exception> DefaultUnhandledException { get; }

        void Post(Action action);

        void Register(QueryClient client);

        void Unregister(QueryClient client);
    }
}
