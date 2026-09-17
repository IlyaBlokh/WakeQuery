using System;
using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery
{
    public sealed class QueryDefinition<T>
    {
        public QueryDefinition(
            QueryKey<T> key,
            Func<CancellationToken, Task<T>> fetch,
            QueryPolicy policy = null)
        {
            Key = key;
            _ = key.Identity;
            Fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            Policy = policy ?? QueryPolicy.Default;
        }

        public QueryKey<T> Key { get; }

        public Func<CancellationToken, Task<T>> Fetch { get; }

        public QueryPolicy Policy { get; }
    }
}
