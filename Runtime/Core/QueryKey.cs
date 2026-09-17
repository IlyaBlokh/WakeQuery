using System;

namespace WakeQuery
{
    /// <summary>
    /// Creates typed structural query keys.
    /// </summary>
    public static class QueryKey
    {
        /// <summary>Creates a key from a scope and ordered parts.</summary>
        /// <typeparam name="T">The result type cached under this key.</typeparam>
        /// <param name="scope">The resource name, for example <c>"player-profile"</c>. Compared ordinally.</param>
        /// <param name="parts">The ordered identifying parts, created with <see cref="QueryKeyPart"/> factory methods.</param>
        /// <returns>The key.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="scope"/> is null or whitespace, or a part was not created with a <see cref="QueryKeyPart"/> factory method.
        /// </exception>
        /// <example>
        /// <code>
        /// QueryKey&lt;PlayerProfile&gt; key = QueryKey.For&lt;PlayerProfile&gt;("player-profile", QueryKeyPart.Text(playerId));
        /// </code>
        /// </example>
        public static QueryKey<T> For<T>(string scope, params QueryKeyPart[] parts)
        {
            return new QueryKey<T>(new QueryKeyIdentity(scope, parts));
        }
    }

    /// <summary>
    /// A structural cache key: a scope plus ordered parts, tagged with the result type.
    /// </summary>
    /// <typeparam name="T">The result type cached under this key.</typeparam>
    /// <remarks>
    /// Two keys are equal when their scopes and parts are equal. The result type is not part of the cache identity:
    /// using the same scope and parts with a different <typeparamref name="T"/> throws <see cref="QueryTypeMismatchException"/>.
    /// Create keys with <see cref="QueryKey.For{T}"/>; a <c>default</c> key is invalid.
    /// </remarks>
    public readonly struct QueryKey<T> : IEquatable<QueryKey<T>>
    {
        private readonly QueryKeyIdentity _identity;

        internal QueryKey(QueryKeyIdentity identity)
        {
            _identity = identity;
        }

        /// <summary>Gets the key scope.</summary>
        /// <exception cref="InvalidOperationException">The key is a default (uninitialized) key.</exception>
        public string Scope => Identity.Scope;

        /// <summary>Gets the number of parts after the scope.</summary>
        /// <exception cref="InvalidOperationException">The key is a default (uninitialized) key.</exception>
        public int PartCount => Identity.PartCount;

        internal QueryKeyIdentity Identity =>
            _identity ?? throw new InvalidOperationException("A default QueryKey is not valid.");

        /// <inheritdoc/>
        public bool Equals(QueryKey<T> other)
        {
            if (_identity == null || other._identity == null)
            {
                return _identity == other._identity;
            }

            return _identity.Equals(other._identity);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is QueryKey<T> other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return _identity?.GetHashCode() ?? 0;
        }

        /// <summary>Formats the key as <c>scope/part1/part2</c>, or <c>&lt;invalid&gt;</c> for a default key.</summary>
        /// <returns>The formatted key.</returns>
        public override string ToString()
        {
            return _identity?.ToString() ?? "<invalid>";
        }

        /// <summary>Determines whether two keys are structurally equal.</summary>
        /// <param name="left">The first key.</param>
        /// <param name="right">The second key.</param>
        /// <returns><see langword="true"/> if the keys are equal.</returns>
        public static bool operator ==(QueryKey<T> left, QueryKey<T> right)
        {
            return left.Equals(right);
        }

        /// <summary>Determines whether two keys are not structurally equal.</summary>
        /// <param name="left">The first key.</param>
        /// <param name="right">The second key.</param>
        /// <returns><see langword="true"/> if the keys differ.</returns>
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
