using System;
using System.Collections.Generic;

namespace WakeQuery
{
    /// <summary>
    /// Passed to <see cref="MutationDefinition{TInput,TOutput}.OnSuccess"/> to stage cache effects for a successful execution.
    /// </summary>
    /// <typeparam name="TInput">The mutation input type.</typeparam>
    /// <typeparam name="TOutput">The mutation output type.</typeparam>
    /// <remarks>
    /// Effects are recorded, not applied immediately. After the callback returns they are applied in order and
    /// observers are notified in one batch, before <see cref="Mutation{TInput,TOutput}.ExecuteAsync"/> completes.
    /// The staging methods are valid only while the callback runs; calling them later throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public sealed class MutationSuccess<TInput, TOutput>
    {
        private readonly List<Action<QueryClient>> _effects = new();
        private bool _isActive = true;

        internal MutationSuccess(TInput input, TOutput output)
        {
            Input = input;
            Output = output;
        }

        /// <summary>Gets the input the execution was started with.</summary>
        public TInput Input { get; }

        /// <summary>Gets the output returned by the remote operation.</summary>
        public TOutput Output { get; }

        internal IReadOnlyList<Action<QueryClient>> Effects => _effects;

        /// <summary>Stages a <see cref="QueryClient.SetData{T}"/> call.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="key">The key to write.</param>
        /// <param name="data">The data to store.</param>
        /// <exception cref="InvalidOperationException">Called after the success callback returned.</exception>
        public void Set<T>(QueryKey<T> key, T data)
        {
            Stage(client => client.SetData(key, data));
        }

        /// <summary>Stages a <see cref="QueryClient.UpdateData{T}"/> call.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="key">The key to update.</param>
        /// <param name="update">Produces the new data from the cached data. Skipped if the key has no data.</param>
        /// <exception cref="ArgumentNullException"><paramref name="update"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Called after the success callback returned.</exception>
        public void Update<T>(QueryKey<T> key, Func<T, T> update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            Stage(client => client.UpdateData(key, update));
        }

        /// <summary>Stages invalidation of a single key.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="key">The key to invalidate.</param>
        /// <exception cref="InvalidOperationException">Called after the success callback returned.</exception>
        public void Invalidate<T>(QueryKey<T> key)
        {
            Invalidate(QueryFilter.Exact(key));
        }

        /// <summary>Stages a <see cref="QueryClient.Invalidate(QueryFilter)"/> call.</summary>
        /// <param name="filter">Selects the queries to invalidate.</param>
        /// <exception cref="InvalidOperationException">Called after the success callback returned.</exception>
        public void Invalidate(QueryFilter filter)
        {
            Stage(client => client.Invalidate(filter));
        }

        /// <summary>Stages a <see cref="QueryClient.Remove(QueryFilter)"/> call.</summary>
        /// <param name="filter">Selects the queries to remove.</param>
        /// <exception cref="InvalidOperationException">Called after the success callback returned.</exception>
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
