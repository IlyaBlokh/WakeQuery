using System;

namespace WakeQuery
{
    /// <summary>
    /// The overall status of a <see cref="Mutation{TInput,TOutput}"/>.
    /// </summary>
    public enum MutationStatus
    {
        /// <summary>No execution is running, and none has succeeded or failed yet.</summary>
        Idle,

        /// <summary>At least one execution is running.</summary>
        Pending,

        /// <summary>No execution is running and the most recently started completed execution succeeded.</summary>
        Success,

        /// <summary>No execution is running and the most recently started completed execution failed.</summary>
        Error
    }

    /// <summary>
    /// An immutable snapshot of a <see cref="Mutation{TInput,TOutput}"/>.
    /// </summary>
    /// <typeparam name="T">The mutation output type.</typeparam>
    /// <remarks>
    /// The terminal fields (<see cref="HasData"/>, <see cref="Data"/>, <see cref="Error"/>) describe the completed
    /// execution with the highest execution id, so a slow older execution never overwrites a newer result.
    /// Canceled executions do not change the terminal fields.
    /// </remarks>
    public readonly struct MutationState<T>
    {
        internal MutationState(
            MutationStatus status,
            int pendingCount,
            bool hasData,
            T data,
            Exception error,
            long lastCompletedExecutionId,
            long revision)
        {
            Status = status;
            PendingCount = pendingCount;
            HasData = hasData;
            Data = data;
            Error = error;
            LastCompletedExecutionId = lastCompletedExecutionId;
            Revision = revision;
        }

        /// <summary>
        /// Gets the status. <see cref="MutationStatus.Pending"/> while any execution runs; otherwise the terminal status.
        /// </summary>
        public MutationStatus Status { get; }

        /// <summary>Gets the number of executions currently running.</summary>
        public int PendingCount { get; }

        /// <summary>Gets a value indicating whether <see cref="Data"/> holds the output of a successful execution.</summary>
        public bool HasData { get; }

        /// <summary>Gets the output of the latest successful execution, or <c>default</c> when <see cref="HasData"/> is <see langword="false"/>.</summary>
        public T Data { get; }

        /// <summary>Gets the error of the latest failed execution, or <see langword="null"/>.</summary>
        public Exception Error { get; }

        /// <summary>Gets the id of the execution that set the terminal fields, or <c>0</c> if none has.</summary>
        public long LastCompletedExecutionId { get; }

        /// <summary>Gets a number that increases every time the mutation publishes a new state.</summary>
        public long Revision { get; }
    }
}
