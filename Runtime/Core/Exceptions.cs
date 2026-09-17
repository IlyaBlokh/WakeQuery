using System;

namespace WakeQuery
{
    public sealed class QueryTypeMismatchException : InvalidOperationException
    {
        internal QueryTypeMismatchException(
            string key,
            Type existingType,
            Type requestedType)
            : base(
                "Query key '" + key + "' is already associated with " +
                existingType.FullName + " and cannot be used as " +
                requestedType.FullName + ".")
        {
            Key = key;
            ExistingType = existingType;
            RequestedType = requestedType;
        }

        public string Key { get; }

        public Type ExistingType { get; }

        public Type RequestedType { get; }
    }

    public sealed class MutationEffectException : Exception
    {
        internal MutationEffectException(Exception innerException)
            : base(
                "The mutation operation succeeded, but its local cache effect failed.",
                innerException)
        {
        }
    }
}
