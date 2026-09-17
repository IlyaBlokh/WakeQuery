using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WakeQuery.Internal;

namespace WakeQuery
{
    public sealed class QueryObserver<T> : IDisposable, IQueryObserverInternal
    {
        private readonly QueryClient _client;
        private readonly QueryDefinition<T> _definition;
        private readonly QueryEntry<T> _entry;
        private readonly List<Action<QueryState<T>>> _listeners =
            new List<Action<QueryState<T>>>();
        private readonly List<Action<QueryState<T>>> _deferredListeners =
            new List<Action<QueryState<T>>>();
        private QueryState<T> _state;
        private long _stateRevision;
        private long _staleDeadlineGeneration;
        private long _pollGeneration;
        private DeadlineHandle _staleDeadline;
        private DeadlineHandle _pollDeadline;
        private bool _isDispatching;
        private bool _isDisposed;

        internal QueryObserver(
            QueryClient client,
            QueryDefinition<T> definition,
            QueryEntry<T> entry,
            Action<QueryState<T>> listener)
        {
            _client = client;
            _definition = definition;
            _entry = entry;
            if (listener != null)
            {
                _listeners.Add(listener);
            }

            _state = BuildState(_stateRevision);
        }

        public QueryState<T> State
        {
            get
            {
                _client.AssertOwnerThread();
                ThrowIfDisposed();
                return _state;
            }
        }

        QueryEntry IQueryObserverInternal.Entry => _entry;

        QueryPolicy IQueryObserverInternal.Policy => _definition.Policy;

        bool IQueryObserverInternal.IsDisposed => _isDisposed;

        internal QueryDefinition<T> Definition => _definition;

        internal void Activate()
        {
            ScheduleStaleDeadline();
            SchedulePoll();
            _client.ScheduleEnsure(this);
        }

        internal void EmitInitial()
        {
            DispatchListeners(_client.ReportObserverException);
        }

        public IDisposable Subscribe(Action<QueryState<T>> listener)
        {
            _client.AssertOwnerThread();
            ThrowIfDisposed();
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (_isDispatching)
            {
                _deferredListeners.Add(listener);
            }
            else
            {
                _listeners.Add(listener);
                InvokeListener(listener);
            }

            return new ObserverSubscription(this, listener);
        }

        public Task<T> EnsureAsync(CancellationToken cancellationToken = default)
        {
            _client.AssertOwnerThread();
            ThrowIfDisposed();
            return _client.Fetch(this, force: false, cancellationToken);
        }

        public Task<T> RefetchAsync(CancellationToken cancellationToken = default)
        {
            _client.AssertOwnerThread();
            ThrowIfDisposed();
            return _client.Fetch(this, force: true, cancellationToken);
        }

        public void Invalidate()
        {
            _client.AssertOwnerThread();
            ThrowIfDisposed();
            _client.Invalidate(QueryFilter.Exact(_definition.Key));
        }

        public void CancelSharedFetch()
        {
            _client.AssertOwnerThread();
            ThrowIfDisposed();
            _client.Cancel(QueryFilter.Exact(_definition.Key));
        }

        public void Dispose()
        {
            _client.AssertOwnerThread();
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _staleDeadlineGeneration++;
            _pollGeneration++;
            _staleDeadline?.Cancel();
            _pollDeadline?.Cancel();
            _staleDeadline = null;
            _pollDeadline = null;
            _listeners.Clear();
            _deferredListeners.Clear();
            _client.DetachObserver(this);
        }

        void IQueryObserverInternal.RefreshAndQueue()
        {
            if (_isDisposed)
            {
                return;
            }

            QueryState<T> next = BuildState(_stateRevision + 1);
            if (StatesEqual(_state, next))
            {
                return;
            }

            bool dataTimestampChanged = _state.UpdatedAt != next.UpdatedAt;
            _stateRevision++;
            _state = BuildState(_stateRevision);
            if (dataTimestampChanged)
            {
                ScheduleStaleDeadline();
            }

            _client.QueueNotification(this);
        }

        void IQueryObserverInternal.Dispatch(Action<Exception> reportException)
        {
            DispatchListeners(reportException);
        }

        void IQueryObserverInternal.OnFetchSettled()
        {
            if (_isDisposed)
            {
                return;
            }

            SchedulePoll();
        }

        void IQueryObserverInternal.OnFocusChanged(bool isFocused)
        {
            if (_isDisposed)
            {
                return;
            }

            _pollGeneration++;
            _pollDeadline?.Cancel();
            _pollDeadline = null;
            if (!isFocused)
            {
                return;
            }

            if (_definition.Policy.RefetchOnFocus && CalculateIsStale())
            {
                _client.EnsureObserved(this, force: false);
            }
            else
            {
                SchedulePoll();
            }
        }

        void IQueryObserverInternal.OnReconnect()
        {
            if (!_isDisposed &&
                _definition.Policy.RefetchOnReconnect &&
                CalculateIsStale())
            {
                _client.EnsureObserved(this, force: false);
            }
        }

        void IQueryObserverInternal.Ensure(bool force)
        {
            if (!_isDisposed)
            {
                _client.EnsureObserved(this, force);
            }
        }

        void IQueryObserverInternal.OnClientDisposed()
        {
            _isDisposed = true;
            _staleDeadlineGeneration++;
            _pollGeneration++;
            _staleDeadline?.Cancel();
            _pollDeadline?.Cancel();
            _staleDeadline = null;
            _pollDeadline = null;
            _listeners.Clear();
            _deferredListeners.Clear();
        }

        private void ScheduleStaleDeadline()
        {
            _staleDeadlineGeneration++;
            _staleDeadline?.Cancel();
            _staleDeadline = null;
            if (_isDisposed ||
                !_entry.HasData ||
                _definition.Policy.StaleAfter == TimeSpan.Zero ||
                _entry.IsInvalidated)
            {
                return;
            }

            long generation = _staleDeadlineGeneration;
            TimeSpan dueAt = _client.AddDelay(
                _entry.UpdatedElapsed,
                _definition.Policy.StaleAfter);
            DeadlineHandle handle = null;
            handle = _client.ScheduleDeadline(dueAt, () =>
            {
                if (ReferenceEquals(_staleDeadline, handle))
                {
                    _staleDeadline = null;
                }

                if (!_isDisposed && generation == _staleDeadlineGeneration)
                {
                    ((IQueryObserverInternal)this).RefreshAndQueue();
                }
            });
            _staleDeadline = handle;
        }

        private void SchedulePoll()
        {
            _pollGeneration++;
            _pollDeadline?.Cancel();
            _pollDeadline = null;
            if (_isDisposed ||
                !_definition.Policy.PollEvery.HasValue ||
                !_client.IsFocused ||
                _entry.HasActiveFetch)
            {
                return;
            }

            long generation = _pollGeneration;
            TimeSpan dueAt = _client.AddDelay(
                _client.Elapsed,
                _definition.Policy.PollEvery.Value);
            DeadlineHandle handle = null;
            handle = _client.ScheduleDeadline(dueAt, () =>
            {
                if (ReferenceEquals(_pollDeadline, handle))
                {
                    _pollDeadline = null;
                }

                if (_isDisposed ||
                    generation != _pollGeneration ||
                    !_client.IsFocused)
                {
                    return;
                }

                _client.EnsureObserved(this, force: true);
            });
            _pollDeadline = handle;
        }

        private QueryState<T> BuildState(long revision)
        {
            return new QueryState<T>(
                _entry.Status,
                _entry.FetchActivity,
                _entry.HasData,
                _entry.Data,
                _entry.Error,
                CalculateIsStale(),
                _entry.FailureCount,
                _entry.UpdatedAt,
                revision);
        }

        private bool CalculateIsStale()
        {
            return !_entry.HasData ||
                   _entry.IsInvalidated ||
                   _client.Elapsed - _entry.UpdatedElapsed >=
                   _definition.Policy.StaleAfter;
        }

        private static bool StatesEqual(QueryState<T> first, QueryState<T> second)
        {
            return first.Status == second.Status &&
                   first.FetchActivity == second.FetchActivity &&
                   first.HasData == second.HasData &&
                   EqualityComparer<T>.Default.Equals(first.Data, second.Data) &&
                   ReferenceEquals(first.Error, second.Error) &&
                   first.IsStale == second.IsStale &&
                   first.FailureCount == second.FailureCount &&
                   first.UpdatedAt == second.UpdatedAt;
        }

        private void InvokeListener(Action<QueryState<T>> listener)
        {
            try
            {
                listener(_state);
            }
            catch (Exception exception)
            {
                _client.ReportObserverException(exception);
            }
        }

        private void DispatchListeners(Action<Exception> reportException)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDispatching = true;
            try
            {
                Action<QueryState<T>>[] listeners = _listeners.ToArray();
                for (int index = 0;
                     index < listeners.Length && !_isDisposed;
                     index++)
                {
                    InvokeListener(listeners[index], reportException);
                }

                while (_deferredListeners.Count > 0 && !_isDisposed)
                {
                    Action<QueryState<T>> listener = _deferredListeners[0];
                    _deferredListeners.RemoveAt(0);
                    _listeners.Add(listener);
                    InvokeListener(listener, reportException);
                }
            }
            finally
            {
                _isDispatching = false;
            }
        }

        private void InvokeListener(
            Action<QueryState<T>> listener,
            Action<Exception> reportException)
        {
            try
            {
                listener(_state);
            }
            catch (Exception exception)
            {
                reportException(exception);
            }
        }

        private void Unsubscribe(Action<QueryState<T>> listener)
        {
            _client.AssertOwnerThread();
            if (_isDisposed)
            {
                return;
            }

            _listeners.Remove(listener);
            _deferredListeners.Remove(listener);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(QueryObserver<T>));
            }
        }

        private sealed class ObserverSubscription : IDisposable
        {
            private QueryObserver<T> _observer;
            private Action<QueryState<T>> _listener;

            public ObserverSubscription(
                QueryObserver<T> observer,
                Action<QueryState<T>> listener)
            {
                _observer = observer;
                _listener = listener;
            }

            public void Dispose()
            {
                QueryObserver<T> observer = _observer;
                Action<QueryState<T>> listener = _listener;
                if (observer == null)
                {
                    return;
                }

                _observer = null;
                _listener = null;
                observer.Unsubscribe(listener);
            }
        }
    }
}
