using System;

namespace WakeQuery
{
    public enum MutationStatus
    {
        Idle,
        Pending,
        Success,
        Error
    }

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

        public MutationStatus Status { get; }

        public int PendingCount { get; }

        public bool HasData { get; }

        public T Data { get; }

        public Exception Error { get; }

        public long LastCompletedExecutionId { get; }

        public long Revision { get; }
    }
}
