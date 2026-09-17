using System;
using System.Collections.Generic;
using System.Threading;
using WakeQuery.Internal;

namespace WakeQuery.Testing
{
    public sealed class ManualQueryRuntime : IDisposable, IQueryRuntimeHost
    {
        private readonly List<QueryClient> _clients = new List<QueryClient>();
        private readonly object _queueLock = new object();
        private Queue<Action> _pending = new Queue<Action>();
        private Queue<Action> _draining = new Queue<Action>();
        private bool _isDisposed;

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

        public int OwnerThreadId { get; }

        public TimeSpan Elapsed { get; private set; }

        public DateTimeOffset UtcNow { get; private set; }

        public QueryClient CreateClient(QueryClientOptions options = null)
        {
            AssertAvailable();
            return new QueryClient(this, options);
        }

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
