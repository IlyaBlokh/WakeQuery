using System;

namespace WakeQuery
{
    /// <summary>
    /// Thrown when a structural query key is used with a result type different from the one already cached for it.
    /// </summary>
    /// <remarks>
    /// The result type is not part of a key's structural identity, so <c>QueryKey.For&lt;A&gt;("x")</c> and
    /// <c>QueryKey.For&lt;B&gt;("x")</c> refer to the same cache entry and cannot both be used.
    /// </remarks>
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

        /// <summary>Gets the conflicting query key formatted as text.</summary>
        public string Key { get; }

        /// <summary>Gets the result type already associated with the key.</summary>
        public Type ExistingType { get; }

        /// <summary>Gets the result type that was requested.</summary>
        public Type RequestedType { get; }
    }

    /// <summary>
    /// Thrown by <see cref="Mutation{TInput,TOutput}.ExecuteAsync"/> when the remote operation succeeded
    /// but its local success effects failed.
    /// </summary>
    /// <remarks>
    /// The remote change has already happened. The original exception is available through
    /// <see cref="Exception.InnerException"/>. If the <see cref="MutationDefinition{TInput,TOutput}.OnSuccess"/>
    /// callback itself throws, none of its staged effects are applied. If applying a staged effect throws,
    /// effects applied before it remain in the cache.
    /// </remarks>
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
