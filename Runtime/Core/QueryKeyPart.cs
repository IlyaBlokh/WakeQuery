using System;

namespace WakeQuery
{
    /// <summary>
    /// The value type stored in a <see cref="QueryKeyPart"/>.
    /// </summary>
    public enum QueryKeyPartKind
    {
        /// <summary>A string, compared ordinally.</summary>
        Text,
        /// <summary>A signed 64-bit integer.</summary>
        Signed,
        /// <summary>An unsigned 64-bit integer.</summary>
        Unsigned,
        /// <summary>A Boolean value.</summary>
        Boolean,
        /// <summary>A <see cref="System.Guid"/>.</summary>
        Guid
    }

    /// <summary>
    /// One typed segment of a <see cref="QueryKey{T}"/>.
    /// </summary>
    /// <remarks>
    /// Only strings, signed and unsigned integers, Booleans, and GUIDs are supported, so keys have stable,
    /// value-based equality. Parts of different kinds are never equal, for example <c>Signed(1)</c> and <c>Unsigned(1)</c>.
    /// Create parts with the factory methods; a <c>default</c> part is invalid.
    /// </remarks>
    public readonly struct QueryKeyPart : IEquatable<QueryKeyPart>
    {
        private readonly string _text;
        private readonly long _signed;
        private readonly ulong _unsigned;
        private readonly bool _boolean;
        private readonly Guid _guid;
        private readonly int _hashCode;

        private QueryKeyPart(
            QueryKeyPartKind kind,
            string text,
            long signed,
            ulong unsigned,
            bool boolean,
            Guid guid,
            int hashCode)
        {
            Kind = kind;
            _text = text;
            _signed = signed;
            _unsigned = unsigned;
            _boolean = boolean;
            _guid = guid;
            _hashCode = hashCode;
        }

        /// <summary>Gets the kind of value stored in this part.</summary>
        public QueryKeyPartKind Kind { get; }

        internal bool IsValid =>
            Kind != QueryKeyPartKind.Text || _text != null;

        /// <summary>Creates a string part.</summary>
        /// <param name="value">The value. Compared ordinally.</param>
        /// <returns>The part.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public static QueryKeyPart Text(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return new QueryKeyPart(
                QueryKeyPartKind.Text,
                value,
                0,
                0,
                false,
                System.Guid.Empty,
                CombineHash((int)QueryKeyPartKind.Text, StringComparer.Ordinal.GetHashCode(value)));
        }

        /// <summary>Creates a signed integer part. Smaller signed integer types convert implicitly.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The part.</returns>
        public static QueryKeyPart Signed(long value)
        {
            return new QueryKeyPart(
                QueryKeyPartKind.Signed,
                null,
                value,
                0,
                false,
                System.Guid.Empty,
                CombineHash((int)QueryKeyPartKind.Signed, value.GetHashCode()));
        }

        /// <summary>Creates an unsigned integer part. Smaller unsigned integer types convert implicitly.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The part.</returns>
        public static QueryKeyPart Unsigned(ulong value)
        {
            return new QueryKeyPart(
                QueryKeyPartKind.Unsigned,
                null,
                0,
                value,
                false,
                System.Guid.Empty,
                CombineHash((int)QueryKeyPartKind.Unsigned, value.GetHashCode()));
        }

        /// <summary>Creates a Boolean part.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The part.</returns>
        public static QueryKeyPart Boolean(bool value)
        {
            return new QueryKeyPart(
                QueryKeyPartKind.Boolean,
                null,
                0,
                0,
                value,
                System.Guid.Empty,
                CombineHash((int)QueryKeyPartKind.Boolean, value.GetHashCode()));
        }

        /// <summary>Creates a GUID part.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The part.</returns>
        public static QueryKeyPart Guid(Guid value)
        {
            return new QueryKeyPart(
                QueryKeyPartKind.Guid,
                null,
                0,
                0,
                false,
                value,
                CombineHash((int)QueryKeyPartKind.Guid, value.GetHashCode()));
        }

        /// <inheritdoc/>
        public bool Equals(QueryKeyPart other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            switch (Kind)
            {
                case QueryKeyPartKind.Text:
                    return string.Equals(_text, other._text, StringComparison.Ordinal);
                case QueryKeyPartKind.Signed:
                    return _signed == other._signed;
                case QueryKeyPartKind.Unsigned:
                    return _unsigned == other._unsigned;
                case QueryKeyPartKind.Boolean:
                    return _boolean == other._boolean;
                case QueryKeyPartKind.Guid:
                    return _guid.Equals(other._guid);
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is QueryKeyPart other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return _hashCode;
        }

        /// <summary>
        /// Formats the value: the text itself, the integer digits, <c>true</c> or <c>false</c>, or the GUID in <c>D</c> format.
        /// </summary>
        /// <returns>The formatted value.</returns>
        public override string ToString()
        {
            switch (Kind)
            {
                case QueryKeyPartKind.Text:
                    return _text;
                case QueryKeyPartKind.Signed:
                    return _signed.ToString();
                case QueryKeyPartKind.Unsigned:
                    return _unsigned.ToString();
                case QueryKeyPartKind.Boolean:
                    return _boolean ? "true" : "false";
                case QueryKeyPartKind.Guid:
                    return _guid.ToString("D");
                default:
                    return string.Empty;
            }
        }

        /// <summary>Determines whether two parts have the same kind and value.</summary>
        /// <param name="left">The first part.</param>
        /// <param name="right">The second part.</param>
        /// <returns><see langword="true"/> if the parts are equal.</returns>
        public static bool operator ==(QueryKeyPart left, QueryKeyPart right)
        {
            return left.Equals(right);
        }

        /// <summary>Determines whether two parts differ in kind or value.</summary>
        /// <param name="left">The first part.</param>
        /// <param name="right">The second part.</param>
        /// <returns><see langword="true"/> if the parts differ.</returns>
        public static bool operator !=(QueryKeyPart left, QueryKeyPart right)
        {
            return !left.Equals(right);
        }

        private static int CombineHash(int first, int second)
        {
            unchecked
            {
                return (first * 397) ^ second;
            }
        }
    }
}
