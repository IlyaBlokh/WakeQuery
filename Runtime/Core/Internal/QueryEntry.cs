using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WakeQuery.Internal
{
    internal abstract class QueryEntry
    {
        protected QueryEntry(QueryKeyIdentity key, Type dataType)
        {
            Key = key;
            DataType = dataType;
        }

        public QueryKeyIdentity Key { get; }

        public Type DataType { get; }

        public List<IQueryObserverInternal> Observers { get; } =
            new List<IQueryObserverInternal>();

        public TimeSpan MaxUnusedFor { get; set; }

        public bool HasRetentionPolicy { get; set; }

        public long CacheGeneration { get; set; }

        public long GarbageCollectionGeneration { get; set; }

        public DeadlineHandle GarbageCollectionDeadline { get; set; }

        public abstract bool HasActiveFetch { get; }

        public abstract int WaiterCount { get; }

        public abstract bool HasActiveFetchInterest { get; }

        public bool IsUnused =>
            Observers.Count == 0 &&
            WaiterCount == 0 &&
            !HasActiveFetch;

        public abstract void ClearData();

        public abstract void MarkInvalidated();

        public abstract void CancelActive(
            QueryClient client,
            bool ensureSupersededObservers);

        public abstract void AddFetchObserverInterest(
            IQueryObserverInternal observer);

        public abstract void RemoveFetchObserverInterest(
            IQueryObserverInternal observer);

        public abstract bool SupersedeWaitingRetry(QueryClient client);

        public abstract void NotifyObservers();

        public abstract void MarkObserversDisposed();
    }

    internal sealed class QueryEntry<T> : QueryEntry
    {
        public QueryEntry(QueryKeyIdentity key)
            : base(key, typeof(T))
        {
        }

        public QueryStatus Status { get; set; }

        public bool HasData { get; set; }

        public T Data { get; set; }

        public Exception Error { get; set; }

        public int FailureCount { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public TimeSpan UpdatedElapsed { get; set; }

        public bool IsInvalidated { get; set; }

        public FetchGeneration<T> ActiveFetch { get; set; }

        public override bool HasActiveFetch => ActiveFetch != null;

        public override int WaiterCount => ActiveFetch?.Waiters.Count ?? 0;

        public override bool HasActiveFetchInterest =>
            ActiveFetch != null &&
            (ActiveFetch.Waiters.Count > 0 ||
             ActiveFetch.ObserverInterests.Count > 0);

        public FetchActivity FetchActivity =>
            ActiveFetch == null
                ? FetchActivity.Idle
                : ActiveFetch.IsWaitingToRetry
                    ? FetchActivity.RetryDelay
                    : FetchActivity.Fetching;

        public override void ClearData()
        {
            Status = QueryStatus.Empty;
            HasData = false;
            Data = default;
            Error = null;
            FailureCount = 0;
            UpdatedAt = null;
            UpdatedElapsed = TimeSpan.Zero;
            IsInvalidated = true;
        }

        public override void MarkInvalidated()
        {
            IsInvalidated = true;
        }

        public override void CancelActive(
            QueryClient client,
            bool ensureSupersededObservers)
        {
            client.CancelActiveFetch(this, ensureSupersededObservers);
        }

        public override void AddFetchObserverInterest(
            IQueryObserverInternal observer)
        {
            ActiveFetch?.ObserverInterests.Add(observer);
        }

        public override void RemoveFetchObserverInterest(
            IQueryObserverInternal observer)
        {
            ActiveFetch?.ObserverInterests.Remove(observer);
        }

        public override bool SupersedeWaitingRetry(QueryClient client)
        {
            return client.SupersedeWaitingRetry(this);
        }

        public override void NotifyObservers()
        {
            for (int index = 0; index < Observers.Count; index++)
            {
                Observers[index].RefreshAndQueue();
            }
        }

        public override void MarkObserversDisposed()
        {
            for (int index = 0; index < Observers.Count; index++)
            {
                Observers[index].OnClientDisposed();
            }

            Observers.Clear();
        }
    }

    internal sealed class FetchGeneration<T>
    {
        public FetchGeneration(
            long id,
            long cacheGeneration,
            QueryDefinition<T> definition,
            QueryStatus previousStatus,
            Exception previousError,
            int previousFailureCount)
        {
            Id = id;
            CacheGeneration = cacheGeneration;
            Definition = definition;
            PreviousStatus = previousStatus;
            PreviousError = previousError;
            PreviousFailureCount = previousFailureCount;
            Cancellation = new CancellationTokenSource();
        }

        public long Id { get; }

        public long CacheGeneration { get; }

        public QueryDefinition<T> Definition { get; }

        public QueryStatus PreviousStatus { get; }

        public Exception PreviousError { get; }

        public int PreviousFailureCount { get; }

        public CancellationTokenSource Cancellation { get; }

        public List<QueryWaiter<T>> Waiters { get; } = new List<QueryWaiter<T>>();

        public HashSet<IQueryObserverInternal> ObserverInterests { get; } =
            new HashSet<IQueryObserverInternal>();

        public int FailedAttempts { get; set; }

        public bool IsWaitingToRetry { get; set; }

        public DeadlineHandle RetryDeadline { get; set; }

        public Exception LastFailure { get; set; }
    }

    internal sealed class QueryWaiter<T>
    {
        public QueryWaiter(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<T>();
        }

        public CancellationToken CancellationToken { get; }

        public TaskCompletionSource<T> Completion { get; }

        public CancellationTokenRegistration Registration { get; set; }

        public bool IsCompleted { get; set; }
    }

    internal interface IQueryObserverInternal
    {
        QueryEntry Entry { get; }

        QueryPolicy Policy { get; }

        bool IsDisposed { get; }

        void RefreshAndQueue();

        void Dispatch(Action<Exception> reportException);

        void OnFetchSettled();

        void OnFocusChanged(bool isFocused);

        void OnReconnect();

        void Ensure(bool force);

        void OnClientDisposed();
    }
}
