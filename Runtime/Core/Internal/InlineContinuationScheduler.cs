using System.Collections.Generic;
using System.Threading.Tasks;

namespace WakeQuery.Internal
{
    internal sealed class InlineContinuationScheduler : TaskScheduler
    {
        public static InlineContinuationScheduler Instance { get; } =
            new InlineContinuationScheduler();

        private InlineContinuationScheduler()
        {
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }

        protected override void QueueTask(Task task)
        {
            TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(
            Task task,
            bool taskWasPreviouslyQueued)
        {
            return TryExecuteTask(task);
        }
    }
}
