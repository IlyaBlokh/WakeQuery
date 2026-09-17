using System;

namespace WakeQuery.Internal
{
    internal sealed class DeadlineHandle
    {
        internal DeadlineHandle(
            DeadlineQueue queue,
            TimeSpan dueAt,
            long sequence,
            Action callback,
            int index)
        {
            Queue = queue;
            DueAt = dueAt;
            Sequence = sequence;
            Callback = callback;
            Index = index;
        }

        internal DeadlineQueue Queue { get; private set; }

        internal TimeSpan DueAt { get; }

        internal long Sequence { get; }

        internal Action Callback { get; private set; }

        internal int Index { get; set; }

        public void Cancel()
        {
            DeadlineQueue queue = Queue;
            queue?.Cancel(this);
        }

        internal void Deactivate()
        {
            Queue = null;
            Callback = null;
            Index = -1;
        }
    }
}
