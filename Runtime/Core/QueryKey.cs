using System;

namespace WakeQuery
{
    public static class QueryKey
    {
        public static QueryKey<T> For<T>(string scope, params QueryKeyPart[] parts)
        {
            return new QueryKey<T>(new QueryKeyIdentity(scope, parts));
        }
    }

    public readonly struct QueryKey<T> : IEquatable<QueryKey<T>>
    {
        private readonly QueryKeyIdentity _identity;

        internal QueryKey(QueryKeyIdentity identity)
        {
            _identity = identity;
        }

        public string Scope => Identity.Scope;

        public int PartCount => Identity.PartCount;

        internal QueryKeyIdentity Identity =>
            _identity ?? throw new InvalidOperationException("A default QueryKey is not valid.");

        public bool Equals(QueryKey<T> other)
        {
            if (_identity == null || other._identity == null)
            {
                return _identity == other._identity;
            }

            return _identity.Equals(other._identity);
        }

        public override bool Equals(object obj)
        {
            return obj is QueryKey<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _identity?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return _identity?.ToString() ?? "<invalid>";
        }

        public static bool operator ==(QueryKey<T> left, QueryKey<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(QueryKey<T> left, QueryKey<T> right)
        {
            return !left.Equals(right);
        }
    }

    internal sealed class QueryKeyIdentity : IEquatable<QueryKeyIdentity>
    {
        private readonly QueryKeyPart[] _parts;
        private readonly int _hashCode;

        public QueryKeyIdentity(string scope, QueryKeyPart[] parts)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("A query key scope must not be empty.", nameof(scope));
            }

            Scope = scope;
            _parts = parts == null || parts.Length == 0
                ? Array.Empty<QueryKeyPart>()
                : (QueryKeyPart[])parts.Clone();

            for (int index = 0; index < _parts.Length; index++)
            {
                if (!_parts[index].IsValid)
                {
                    throw new ArgumentException(
                        "Query key parts must be created with a QueryKeyPart factory method.",
                        nameof(parts));
                }
            }

            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(scope);
                for (int index = 0; index < _parts.Length; index++)
                {
                    hash = (hash * 397) ^ _parts[index].GetHashCode();
                }

                _hashCode = hash;
            }
        }

        public string Scope { get; }

        public int PartCount => _parts.Length;

        public bool StartsWith(QueryKeyIdentity prefix)
        {
            if (!string.Equals(Scope, prefix.Scope, StringComparison.Ordinal) ||
                _parts.Length < prefix._parts.Length)
            {
                return false;
            }

            for (int index = 0; index < prefix._parts.Length; index++)
            {
                if (!_parts[index].Equals(prefix._parts[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public bool Equals(QueryKeyIdentity other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null ||
                _hashCode != other._hashCode ||
                !string.Equals(Scope, other.Scope, StringComparison.Ordinal) ||
                _parts.Length != other._parts.Length)
            {
                return false;
            }

            for (int index = 0; index < _parts.Length; index++)
            {
                if (!_parts[index].Equals(other._parts[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is QueryKeyIdentity other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override string ToString()
        {
            if (_parts.Length == 0)
            {
                return Scope;
            }

            return Scope + "/" + string.Join("/", _parts);
        }
    }
}
