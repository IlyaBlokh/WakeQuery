using System;

namespace WakeQuery
{
    /// <summary>
    /// Selects cached queries for <see cref="QueryClient.Invalidate(QueryFilter)"/>,
    /// <see cref="QueryClient.Cancel(QueryFilter)"/>, and <see cref="QueryClient.Remove(QueryFilter)"/>.
    /// </summary>
    /// <remarks>
    /// Use <see cref="All"/>, <see cref="Exact{T}(QueryKey{T})"/>, or <see cref="Prefix(string, QueryKeyPart[])"/>.
    /// A <c>default(QueryFilter)</c> is invalid and makes client operations throw <see cref="InvalidOperationException"/>.
    /// </remarks>
    public readonly struct QueryFilter
    {
        private readonly QueryFilterKind _kind;
        private readonly QueryKeyIdentity _identity;

        private QueryFilter(QueryFilterKind kind, QueryKeyIdentity identity)
        {
            _kind = kind;
            _identity = identity;
        }

        /// <summary>Gets a filter that matches every cached query.</summary>
        public static QueryFilter All => new(QueryFilterKind.All, null);

        /// <summary>Creates a filter that matches only the given key.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="key">The key to match.</param>
        /// <returns>The filter.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="key"/> is a default (uninitialized) key.</exception>
        public static QueryFilter Exact<T>(QueryKey<T> key)
        {
            return new QueryFilter(QueryFilterKind.Exact, key.Identity);
        }

        /// <summary>
        /// Creates a filter that matches every key with the given scope whose leading parts equal <paramref name="parts"/>.
        /// </summary>
        /// <param name="scope">The key scope to match, compared ordinally.</param>
        /// <param name="parts">The leading key parts to match. Pass none to match every key in the scope.</param>
        /// <returns>The filter.</returns>
        /// <example>
        /// <c>QueryFilter.Prefix("player", QueryKeyPart.Text(id))</c> matches <c>player/{id}</c> and <c>player/{id}/inventory</c>.
        /// </example>
        /// <exception cref="ArgumentException">
        /// <paramref name="scope"/> is null or whitespace, or a part was not created with a <see cref="QueryKeyPart"/> factory method.
        /// </exception>
        public static QueryFilter Prefix(string scope, params QueryKeyPart[] parts)
        {
            return new QueryFilter(
                QueryFilterKind.Prefix,
                new QueryKeyIdentity(scope, parts));
        }

        internal bool Matches(QueryKeyIdentity key)
        {
            switch (_kind)
            {
                case QueryFilterKind.All:
                    return true;
                case QueryFilterKind.Exact:
                    return key.Equals(_identity);
                case QueryFilterKind.Prefix:
                    return key.StartsWith(_identity);
                default:
                    throw new InvalidOperationException("A default QueryFilter is not valid.");
            }
        }

        internal void Validate()
        {
            if (_kind == QueryFilterKind.Invalid)
            {
                throw new InvalidOperationException(
                    "A default QueryFilter is not valid.");
            }
        }

        private enum QueryFilterKind
        {
            Invalid,
            All,
            Exact,
            Prefix
        }
    }
}
