using System;
using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery
{
    /// <summary>
    /// Describes a remote write operation and the cache effects to apply when it succeeds.
    /// </summary>
    /// <typeparam name="TInput">The input passed to each execution.</typeparam>
    /// <typeparam name="TOutput">The result produced by a successful execution.</typeparam>
    /// <remarks>
    /// Pass a definition to <see cref="QueryClient.CreateMutation{TInput,TOutput}"/> to get a
    /// <see cref="Mutation{TInput,TOutput}"/> you can execute.
    /// </remarks>
    public sealed class MutationDefinition<TInput, TOutput>
    {
        /// <summary>Creates a mutation definition.</summary>
        /// <param name="execute">
        /// The remote operation. It receives the execution input and a cancellation token, and may complete on any thread.
        /// </param>
        /// <param name="onSuccess">
        /// Optional callback that stages cache effects through <see cref="MutationSuccess{TInput,TOutput}"/>.
        /// It runs on the client owner's thread before <see cref="Mutation{TInput,TOutput}.ExecuteAsync"/> completes.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> is <see langword="null"/>.</exception>
        public MutationDefinition(
            Func<TInput, CancellationToken, Task<TOutput>> execute,
            Action<MutationSuccess<TInput, TOutput>> onSuccess = null)
        {
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            OnSuccess = onSuccess;
        }

        /// <summary>Gets the remote operation.</summary>
        public Func<TInput, CancellationToken, Task<TOutput>> Execute { get; }

        /// <summary>Gets the success callback, or <see langword="null"/> if none was provided.</summary>
        public Action<MutationSuccess<TInput, TOutput>> OnSuccess { get; }
    }
}
