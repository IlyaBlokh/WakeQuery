using System;
using System.Collections.Generic;

namespace WakeQuery
{
    public sealed class MutationSuccess<TInput, TOutput>
    {
        private readonly List<Action<QueryClient>> _effects =
            new List<Action<QueryClient>>();
        private bool _isActive = true;

        internal MutationSuccess(TInput input, TOutput output)
        {
            Input = input;
            Output = output;
        }

        public TInput Input { get; }

        public TOutput Output { get; }

        internal IReadOnlyList<Action<QueryClient>> Effects => _effects;

        public void Set<T>(QueryKey<T> key, T data)
        {
            Stage(client => client.SetData(key, data));
        }

        public void Update<T>(QueryKey<T> key, Func<T, T> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            Stage(client => client.UpdateData(key, update));
        }

        public void Invalidate<T>(QueryKey<T> key)
        {
            Invalidate(QueryFilter.Exact(key));
        }

        public void Invalidate(QueryFilter filter)
        {
            Stage(client => client.Invalidate(filter));
        }

        public void Remove(QueryFilter filter)
        {
            Stage(client => client.Remove(filter));
        }

        internal void Deactivate()
        {
            _isActive = false;
        }

        private void Stage(Action<QueryClient> effect)
        {
            if (!_isActive)
            {
                throw new InvalidOperationException(
                    "MutationSuccess cache effects are valid only during OnSuccess.");
            }

            _effects.Add(effect);
        }
    }
}
