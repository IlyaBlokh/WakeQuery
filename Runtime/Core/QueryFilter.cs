using System;

namespace WakeQuery
{
    public readonly struct QueryFilter
    {
        private readonly QueryFilterKind _kind;
        private readonly QueryKeyIdentity _identity;

        private QueryFilter(QueryFilterKind kind, QueryKeyIdentity identity)
        {
            _kind = kind;
            _identity = identity;
        }

        public static QueryFilter All => new(QueryFilterKind.All, null);

        public static QueryFilter Exact<T>(QueryKey<T> key)
        {
            return new QueryFilter(QueryFilterKind.Exact, key.Identity);
        }

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
