using System;

namespace WakeQuery
{
    public enum QueryKeyPartKind
    {
        Text,
        Signed,
        Unsigned,
        Boolean,
        Guid
    }

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

        public QueryKeyPartKind Kind { get; }

        internal bool IsValid =>
            Kind != QueryKeyPartKind.Text || _text != null;

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

        public override bool Equals(object obj)
        {
            return obj is QueryKeyPart other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

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

        public static bool operator ==(QueryKeyPart left, QueryKeyPart right)
        {
            return left.Equals(right);
        }

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
