using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WakeQuery.Internal;

namespace WakeQuery.Tests
{
    public sealed class DeadlineQueueTests
    {
        [Test]
        public void CanceledDeadlinesAreRemovedWithoutChangingStableOrder()
        {
            var queue = new DeadlineQueue();
            var actual = new List<int>();
            var expected = new List<ScheduledDeadline>();
            var handles = new List<DeadlineHandle>();

            for (int index = 0; index < 200; index++)
            {
                int captured = index;
                TimeSpan dueAt = TimeSpan.FromTicks((index * 37) % 17);
                handles.Add(queue.Schedule(dueAt, () => actual.Add(captured)));
                expected.Add(new ScheduledDeadline(captured, dueAt));
            }

            for (int index = 0; index < handles.Count; index += 3)
            {
                handles[index].Cancel();
                handles[index].Cancel();
                expected[index] = expected[index].Cancel();
            }

            queue.RunDue(TimeSpan.MaxValue);

            int[] ordered = expected
                .Where(item => !item.IsCanceled)
                .OrderBy(item => item.DueAt)
                .ThenBy(item => item.Id)
                .Select(item => item.Id)
                .ToArray();
            Assert.That(actual, Is.EqualTo(ordered));
        }

        [Test]
        public void CallbackMayCancelAndScheduleDeadlines()
        {
            var queue = new DeadlineQueue();
            var actual = new List<int>();
            DeadlineHandle canceled = queue.Schedule(
                TimeSpan.FromTicks(1),
                () => actual.Add(2));
            queue.Schedule(TimeSpan.Zero, () =>
            {
                actual.Add(1);
                canceled.Cancel();
                queue.Schedule(TimeSpan.Zero, () => actual.Add(3));
            });

            queue.RunDue(TimeSpan.MaxValue);

            Assert.That(actual, Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void ClearDeactivatesEveryHandle()
        {
            var queue = new DeadlineQueue();
            DeadlineHandle first = queue.Schedule(TimeSpan.Zero, () => { });
            DeadlineHandle second = queue.Schedule(TimeSpan.Zero, () => { });

            queue.Clear();

            Assert.DoesNotThrow(first.Cancel);
            Assert.DoesNotThrow(second.Cancel);
            Assert.DoesNotThrow(() => queue.RunDue(TimeSpan.MaxValue));
        }

        private readonly struct ScheduledDeadline
        {
            public ScheduledDeadline(int id, TimeSpan dueAt, bool isCanceled = false)
            {
                Id = id;
                DueAt = dueAt;
                IsCanceled = isCanceled;
            }

            public int Id { get; }

            public TimeSpan DueAt { get; }

            public bool IsCanceled { get; }

            public ScheduledDeadline Cancel()
            {
                return new ScheduledDeadline(Id, DueAt, isCanceled: true);
            }
        }
    }
}
