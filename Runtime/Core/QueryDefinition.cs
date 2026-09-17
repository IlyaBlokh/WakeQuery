using System;
using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery
{
    /// <summary>
    /// Describes a cacheable resource: its key, how to fetch it, and its freshness policy.
    /// </summary>
    /// <typeparam name="T">The query result type.</typeparam>
    /// <remarks>
    /// Create definitions in one central factory so every consumer of a key uses the same fetch and policy.
    /// Definitions that share a key share one cache entry and one in-flight fetch.
    /// </remarks>
    public sealed class QueryDefinition<T>
    {
        /// <summary>Creates a query definition.</summary>
        /// <param name="key">The structural cache key.</param>
        /// <param name="fetch">
        /// Loads the resource. It receives a cancellation token and may complete on any thread;
        /// results are applied on the client owner's next runtime cycle.
        /// </param>
        /// <param name="policy">The freshness, retention, retry, and polling policy. Defaults to <see cref="QueryPolicy.Default"/>.</param>
        /// <exception cref="InvalidOperationException"><paramref name="key"/> is a default (uninitialized) key.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="fetch"/> is <see langword="null"/>.</exception>
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

        /// <summary>Gets the cache key.</summary>
        public QueryKey<T> Key { get; }

        /// <summary>Gets the fetch operation.</summary>
        public Func<CancellationToken, Task<T>> Fetch { get; }

        /// <summary>Gets the query policy.</summary>
        public QueryPolicy Policy { get; }
    }
}
