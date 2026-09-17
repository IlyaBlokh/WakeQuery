using System;
using System.Collections.Generic;
using System.Threading;
using WakeQuery.Internal;

namespace WakeQuery.Testing
{
    /// <summary>
    /// A deterministic runtime for tests: you control time, frames, focus, and reconnects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing happens until you call <see cref="RunOneFrame"/> or <see cref="AdvanceBy"/>. The thread that creates
    /// the runtime owns it and every client it creates. Exceptions from listeners are rethrown from the frame
    /// unless a client sets <see cref="QueryClientOptions.UnhandledException"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using var runtime = new ManualQueryRuntime();
    /// using QueryClient client = runtime.CreateClient();
    /// var observer = client.Watch(definition);
    /// runtime.RunOneFrame();                    // starts the fetch
    /// runtime.AdvanceBy(TimeSpan.FromSeconds(30)); // moves the clock and runs a frame
    /// </code>
    /// </example>
    public sealed class ManualQueryRuntime : IDisposable, IQueryRuntimeHost
    {
        private readonly List<QueryClient> _clients = new List<QueryClient>();
        private readonly object _queueLock = new object();
        private Queue<Action> _pending = new Queue<Action>();
        private Queue<Action> _draining = new Queue<Action>();
        private bool _isDisposed;

        /// <summary>Creates a runtime owned by the current thread.</summary>
        /// <param name="startTime">The initial <see cref="UtcNow"/>. Defaults to the Unix epoch.</param>
        public ManualQueryRuntime(DateTimeOffset? startTime = null)
        {
            OwnerThreadId = Environment.CurrentManagedThreadId;
            UtcNow = startTime ?? DateTimeOffset.UnixEpoch;
        }

        int IQueryRuntimeHost.OwnerThreadId => OwnerThreadId;

        TimeSpan IQueryRuntimeHost.Elapsed => Elapsed;

        DateTimeOffset IQueryRuntimeHost.UtcNow => UtcNow;

        Action<Exception> IQueryRuntimeHost.DefaultUnhandledException =>
            exception => throw exception;

        /// <summary>Gets the managed thread id of the thread that created the runtime.</summary>
        public int OwnerThreadId { get; }

        /// <summary>Gets the monotonic time advanced by <see cref="AdvanceBy"/>. Starts at zero.</summary>
        public TimeSpan Elapsed { get; private set; }

        /// <summary>Gets the wall-clock time used for <see cref="QueryState{T}.UpdatedAt"/>. Advances with <see cref="Elapsed"/>.</summary>
        public DateTimeOffset UtcNow { get; private set; }

        /// <summary>Creates a client driven by this runtime.</summary>
        /// <param name="options">Optional client options.</param>
        /// <returns>The client.</returns>
        /// <exception cref="InvalidOperationException">Called from a thread other than the owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
        public QueryClient CreateClient(QueryClientOptions options = null)
        {
            AssertAvailable();
            return new QueryClient(this, options);
        }

        /// <summary>Runs one runtime cycle without advancing time.</summary>
        /// <remarks>
        /// Runs callbacks posted since the last frame (such as completed fetches), then, for each client, runs due
        /// deadlines (staleness, polling, retries, eviction), dispatches notifications, and completes waiting tasks.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Called from a thread other than the owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
        public void RunOneFrame()
        {
            AssertAvailable();
            lock (_queueLock)
            {
                Queue<Action> swap = _draining;
                _draining = _pending;
                _pending = swap;
            }

            while (_draining.Count > 0)
            {
                _draining.Dequeue()();
            }

            int index = 0;
            while (index < _clients.Count)
            {
                QueryClient client = _clients[index];
                client.Pump();
                if (index < _clients.Count &&
                    ReferenceEquals(_clients[index], client))
                {
                    index++;
                }
            }
        }

        /// <summary>Advances <see cref="Elapsed"/> and <see cref="UtcNow"/>, then runs one frame.</summary>
        /// <param name="elapsed">The time to advance. Must not be negative.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="elapsed"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
        public void AdvanceBy(TimeSpan elapsed)
        {
            AssertAvailable();
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            }

            Elapsed += elapsed;
            UtcNow += elapsed;
            RunOneFrame();
        }

        /// <summary>Simulates the application gaining or losing focus.</summary>
        /// <param name="isFocused">Whether the application is focused. Clients start focused.</param>
        /// <remarks>Losing focus pauses polling. Regaining focus refetches stale observed queries whose policy allows it.</remarks>
        /// <exception cref="InvalidOperationException">Called from a thread other than the owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
        public void SetFocused(bool isFocused)
        {
            AssertAvailable();
            int index = 0;
            while (index < _clients.Count)
            {
                QueryClient client = _clients[index];
                client.OnFocusChanged(isFocused);
                if (index < _clients.Count &&
                    ReferenceEquals(_clients[index], client))
                {
                    index++;
                }
            }
        }

        /// <summary>Simulates a network reconnect: refetches stale observed queries whose policy allows it.</summary>
        /// <exception cref="InvalidOperationException">Called from a thread other than the owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
        public void NotifyReconnected()
        {
            AssertAvailable();
            int index = 0;
            while (index < _clients.Count)
            {
                QueryClient client = _clients[index];
                client.OnReconnect();
                if (index < _clients.Count &&
                    ReferenceEquals(_clients[index], client))
                {
                    index++;
                }
            }
        }

        /// <summary>Disposes every client created by this runtime and drops pending callbacks.</summary>
        /// <exception cref="InvalidOperationException">Called from a thread other than the owner's thread.</exception>
        public void Dispose()
        {
            AssertOwnerThread();
            if (_isDisposed)
            {
                return;
            }

            while (_clients.Count > 0)
            {
                int finalIndex = _clients.Count - 1;
                QueryClient client = _clients[finalIndex];
                client.Dispose();
                if (finalIndex < _clients.Count &&
                    ReferenceEquals(_clients[finalIndex], client))
                {
                    _clients.RemoveAt(finalIndex);
                }
            }

            lock (_queueLock)
            {
                _pending.Clear();
                _draining.Clear();
            }

            _isDisposed = true;
        }

        void IQueryRuntimeHost.Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            lock (_queueLock)
            {
                if (!_isDisposed)
                {
                    _pending.Enqueue(action);
                }
            }
        }

        void IQueryRuntimeHost.Register(QueryClient client)
        {
            AssertAvailable();
            _clients.Add(client);
        }

        void IQueryRuntimeHost.Unregister(QueryClient client)
        {
            AssertOwnerThread();
            _clients.Remove(client);
        }

        private void AssertAvailable()
        {
            AssertOwnerThread();
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ManualQueryRuntime));
            }
        }

        private void AssertOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != OwnerThreadId)
            {
                throw new InvalidOperationException(
                    "ManualQueryRuntime must be controlled from its owning thread.");
            }
        }
    }
}
