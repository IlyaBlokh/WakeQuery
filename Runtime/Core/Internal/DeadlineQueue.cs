using System;
using System.Collections.Generic;

namespace WakeQuery.Internal
{
    internal sealed class DeadlineQueue
    {
        private readonly List<DeadlineHandle> _items =
            new List<DeadlineHandle>();
        private long _sequence;

        public DeadlineHandle Schedule(TimeSpan dueAt, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var handle = new DeadlineHandle(
                this,
                dueAt,
                _sequence++,
                callback,
                _items.Count);
            _items.Add(handle);
            MoveUp(handle.Index);
            return handle;
        }

        public void RunDue(TimeSpan now)
        {
            while (_items.Count > 0 && _items[0].DueAt <= now)
            {
                DeadlineHandle handle = _items[0];
                Action callback = handle.Callback;
                RemoveAt(0);
                callback();
            }
        }

        public void Clear()
        {
            for (int index = 0; index < _items.Count; index++)
            {
                _items[index].Deactivate();
            }

            _items.Clear();
        }

        internal void Cancel(DeadlineHandle handle)
        {
            if (!ReferenceEquals(handle.Queue, this))
            {
                return;
            }

            RemoveAt(handle.Index);
        }

        private void RemoveAt(int index)
        {
            DeadlineHandle removed = _items[index];
            int finalIndex = _items.Count - 1;
            DeadlineHandle final = _items[finalIndex];
            _items.RemoveAt(finalIndex);
            if (index < _items.Count)
            {
                _items[index] = final;
                final.Index = index;
                int parent = (index - 1) / 2;
                if (index > 0 && ComesBefore(final, _items[parent]))
                {
                    MoveUp(index);
                }
                else
                {
                    MoveDown(index);
                }
            }

            removed.Deactivate();
        }

        private void MoveUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!ComesBefore(_items[index], _items[parent]))
                {
                    return;
                }

                Swap(index, parent);
                index = parent;
            }
        }

        private void MoveDown(int index)
        {
            while (true)
            {
                int left = (index * 2) + 1;
                if (left >= _items.Count)
                {
                    return;
                }

                int right = left + 1;
                int child = right < _items.Count &&
                            ComesBefore(_items[right], _items[left])
                    ? right
                    : left;

                if (!ComesBefore(_items[child], _items[index]))
                {
                    return;
                }

                Swap(index, child);
                index = child;
            }
        }

        private void Swap(int first, int second)
        {
            DeadlineHandle value = _items[first];
            _items[first] = _items[second];
            _items[second] = value;
            _items[first].Index = first;
            _items[second].Index = second;
        }

        private static bool ComesBefore(
            DeadlineHandle first,
            DeadlineHandle second)
        {
            return first.DueAt < second.DueAt ||
                   (first.DueAt == second.DueAt && first.Sequence < second.Sequence);
        }
    }
}
