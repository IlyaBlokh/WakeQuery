using System;
using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery
{
    public sealed class MutationDefinition<TInput, TOutput>
    {
        public MutationDefinition(
            Func<TInput, CancellationToken, Task<TOutput>> execute,
            Action<MutationSuccess<TInput, TOutput>> onSuccess = null)
        {
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            OnSuccess = onSuccess;
        }

        public Func<TInput, CancellationToken, Task<TOutput>> Execute { get; }

        public Action<MutationSuccess<TInput, TOutput>> OnSuccess { get; }
    }
}
