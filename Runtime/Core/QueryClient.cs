using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WakeQuery.Internal;

namespace WakeQuery
{
    /// <summary>
    /// Owns an in-memory query cache, its shared fetches, and its mutations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Create a client with <c>UnityQueryRuntime.CreateClient</c> in a Unity application, or with
    /// <c>ManualQueryRuntime.CreateClient</c> in tests, and dispose it when its application scope ends.
    /// Each client has an independent cache.
    /// </para>
    /// <para>
    /// All public operations must run on the thread that owns the client (the Unity main thread).
    /// Fetch and mutation delegates may complete on any thread; their results, cache changes, and
    /// observer notifications are applied on the owner's next runtime cycle.
    /// </para>
    /// </remarks>
    public sealed class QueryClient : IDisposable
    {
        private readonly IQueryRuntimeHost _runtime;
        private readonly Dictionary<QueryKeyIdentity, QueryEntry> _entries = new();
        private readonly DeadlineQueue _deadlines = new();
        private readonly List<IQueryObserverInternal> _pendingNotifications = new();
        private readonly HashSet<IQueryObserverInternal> _pendingNotificationSet = new();
        private readonly List<IQueryObserverInternal> _dispatchNotifications = new();
        private readonly List<IMutationNotification> _pendingMutationNotifications = new();
        private readonly HashSet<IMutationNotification> _pendingMutationNotificationSet = new();
        private readonly List<IMutationNotification> _dispatchMutationNotifications = new();
        private readonly HashSet<IMutationLifetime> _mutations = new();
        private readonly List<Exception> _observerErrors = new();
        private readonly List<Action> _afterNotifications = new();
        private readonly List<Action> _dispatchAfterNotifications = new();
        private readonly Action<Exception> _unhandledException;
        private readonly IQueryDiagnosticListener _diagnostics;
        private long _fetchGeneration;
        private bool _isDisposed;
        private bool _isFocused = true;
        private bool _isPumping;

        internal QueryClient(
            IQueryRuntimeHost runtime,
            QueryClientOptions options)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            options ??= new QueryClientOptions();
            _unhandledException =
                options.UnhandledException ??
                runtime.DefaultUnhandledException ??
                (_ => { });
            _diagnostics = options.DiagnosticListener;
            runtime.Register(this);
        }

        internal bool IsFocused => _isFocused;

        internal TimeSpan Elapsed => _runtime.Elapsed;

        /// <summary>Starts observing a query.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="definition">The query to observe.</param>
        /// <param name="observer">
        /// Optional listener. It receives the current snapshot synchronously before this method returns,
        /// then every later state change.
        /// </param>
        /// <returns>An observer. Dispose it to release its interest in the query.</returns>
        /// <remarks>
        /// If the data is stale for this observer, a fetch is started or joined on the next runtime cycle.
        /// While observed, the query also refetches according to its <see cref="QueryPolicy"/>
        /// (polling, focus, reconnect, invalidation).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
        /// <exception cref="QueryTypeMismatchException">The key is already cached with a different result type.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public QueryObserver<T> Watch<T>(
            QueryDefinition<T> definition,
            Action<QueryState<T>> observer = null)
        {
            AssertAvailable();
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            QueryEntry<T> entry = GetOrCreateEntry(definition.Key);
            entry.MaxUnusedFor = Max(entry.MaxUnusedFor, definition.Policy.UnusedFor);
            entry.HasRetentionPolicy = true;
            entry.GarbageCollectionGeneration++;
            entry.GarbageCollectionDeadline?.Cancel();
            entry.GarbageCollectionDeadline = null;
            var queryObserver = new QueryObserver<T>(
                this,
                definition,
                entry,
                observer);
            entry.Observers.Add(queryObserver);
            if (entry.HasActiveFetch && queryObserver.State.IsStale)
            {
                entry.AddFetchObserverInterest(queryObserver);
            }

            queryObserver.EmitInitial();
            queryObserver.Activate();
            return queryObserver;
        }

        /// <summary>Returns fresh cached data, or joins or starts a fetch.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="definition">The query to load.</param>
        /// <param name="cancellationToken">
        /// Cancels only this caller's wait. The shared fetch continues while another caller or observer needs it.
        /// </param>
        /// <returns>A task that completes with the data, or faults with the fetch error after all retries.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
        /// <exception cref="QueryTypeMismatchException">The key is already cached with a different result type.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public Task<T> EnsureAsync<T>(
            QueryDefinition<T> definition,
            CancellationToken cancellationToken = default)
        {
            AssertAvailable();
            return definition == null 
                ? throw new ArgumentNullException(nameof(definition))
                : FetchImperative(definition, force: false, cancellationToken);
        }

        /// <summary>Fetches the query even if cached data is fresh.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="definition">The query to load.</param>
        /// <param name="cancellationToken">
        /// Cancels only this caller's wait. The shared fetch continues while another caller or observer needs it.
        /// </param>
        /// <returns>A task that completes with the fetched data, or faults with the fetch error after all retries.</returns>
        /// <remarks>If a fetch for the key is already running, this call joins it instead of starting another.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
        /// <exception cref="QueryTypeMismatchException">The key is already cached with a different result type.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public Task<T> RefetchAsync<T>(
            QueryDefinition<T> definition,
            CancellationToken cancellationToken = default)
        {
            AssertAvailable();
            return definition == null 
                ? throw new ArgumentNullException(nameof(definition))
                : FetchImperative(definition, force: true, cancellationToken);
        }

        /// <summary>Writes data to the cache as a successful, fresh result.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="key">The key to write. The entry is created if it does not exist.</param>
        /// <param name="data">The data to store.</param>
        /// <remarks>
        /// Clears any error and invalidation, and notifies observers. An entry with no observers is evicted
        /// after its <see cref="QueryPolicy.UnusedFor"/> period (5 minutes if no definition has used the key).
        /// </remarks>
        /// <exception cref="QueryTypeMismatchException">The key is already cached with a different result type.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public void SetData<T>(QueryKey<T> key, T data)
        {
            AssertAvailable();
            QueryEntry<T> entry = GetOrCreateEntry(key);
            entry.CacheGeneration++;
            entry.Status = QueryStatus.Success;
            entry.HasData = true;
            entry.Data = data;
            entry.Error = null;
            entry.FailureCount = 0;
            entry.UpdatedElapsed = Elapsed;
            entry.UpdatedAt = _runtime.UtcNow;
            entry.IsInvalidated = false;
            bool settledRetry = SupersedeWaitingRetry(entry);
            if (!settledRetry)
            {
                entry.NotifyObservers();
            }

            if (entry.IsUnused)
            {
                if (!entry.HasRetentionPolicy)
                {
                    entry.MaxUnusedFor = QueryPolicy.Default.UnusedFor;
                    entry.HasRetentionPolicy = true;
                }

                ScheduleGarbageCollection(entry);
            }
        }

        /// <summary>Replaces cached data with a value computed from the current data.</summary>
        /// <typeparam name="T">The query result type.</typeparam>
        /// <param name="key">The key to update.</param>
        /// <param name="update">Produces the new data from the cached data.</param>
        /// <returns>
        /// <see langword="true"/> if the key had data and was updated; <see langword="false"/> if it had no data,
        /// in which case <paramref name="update"/> is not called.
        /// </returns>
        /// <remarks>The new value is written as with <see cref="SetData{T}"/>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="update"/> is <see langword="null"/>.</exception>
        /// <exception cref="QueryTypeMismatchException">The key is already cached with a different result type.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public bool UpdateData<T>(QueryKey<T> key, Func<T, T> update)
        {
            AssertAvailable();
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            QueryEntry<T> entry = GetExistingEntry(key);
            if (entry is not { HasData: true })
            {
                return false;
            }

            SetData(key, update(entry.Data));
            return true;
        }

        /// <summary>Marks matching queries stale and refetches the ones that are observed.</summary>
        /// <param name="filter">Selects the queries.</param>
        /// <returns>The number of matching queries.</returns>
        /// <remarks>
        /// Cached data is kept, so observers continue to see it while the refetch runs. A fetch that is already
        /// running still completes for its callers, but its result is not cached; observed queries fetch again afterwards.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="filter"/> is <c>default</c>, or the method is called from a thread other than the owner's.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public int Invalidate(QueryFilter filter)
        {
            AssertAvailable();
            int count = 0;
            List<QueryEntry> entries = FindMatches(filter);
            foreach (var entry in entries)
            {
                if (!IsCurrentEntry(entry))
                    continue;

                entry.CacheGeneration++;
                entry.MarkInvalidated();
                bool settledRetry = entry.SupersedeWaitingRetry(this);
                if (!settledRetry) 
                    entry.NotifyObservers();

                if (!entry.HasActiveFetch) 
                    ScheduleObservedEnsures(entry);

                EmitDiagnostic(
                    QueryDiagnosticKind.Invalidated,
                    entry.Key,
                    0,
                    0,
                    null);
                count++;
            }

            return count;
        }

        /// <summary>Cancels the shared fetches of matching queries.</summary>
        /// <param name="filter">Selects the queries.</param>
        /// <returns>The number of fetches that were canceled.</returns>
        /// <remarks>
        /// The fetch delegate's cancellation token is canceled and every caller waiting on the fetch sees a canceled task.
        /// Cached data is not changed.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="filter"/> is <c>default</c>, or the method is called from a thread other than the owner's.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public int Cancel(QueryFilter filter)
        {
            AssertAvailable();
            int count = 0;
            List<QueryEntry> entries = FindMatches(filter);
            foreach (var entry in entries)
            {
                if (!IsCurrentEntry(entry) || !entry.HasActiveFetch)
                    continue;

                entry.CancelActive(this, ensureSupersededObservers: false);
                count++;
            }

            return count;
        }

        /// <summary>Cancels fetches for matching queries and clears their cached data.</summary>
        /// <param name="filter">Selects the queries.</param>
        /// <returns>The number of matching queries.</returns>
        /// <remarks>
        /// Unobserved entries are deleted. Observed entries return to <see cref="QueryStatus.Empty"/>
        /// and are fetched again on a later runtime cycle.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="filter"/> is <c>default</c>, or the method is called from a thread other than the owner's.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public int Remove(QueryFilter filter)
        {
            AssertAvailable();
            int count = 0;
            List<QueryEntry> entries = FindMatches(filter);
            foreach (var entry in entries)
            {
                if (!IsCurrentEntry(entry))
                    continue;

                entry.CacheGeneration++;
                entry.CancelActive(this, ensureSupersededObservers: false);
                entry.ClearData();
                entry.NotifyObservers();
                if (entry.Observers.Count > 0)
                {
                    ScheduleObservedEnsures(entry);
                }
                else
                {
                    entry.GarbageCollectionDeadline?.Cancel();
                    entry.GarbageCollectionDeadline = null;
                    _entries.Remove(entry.Key);
                }

                EmitDiagnostic(
                    QueryDiagnosticKind.Removed,
                    entry.Key,
                    0,
                    0,
                    null);
                count++;
            }

            return count;
        }

        /// <summary>Creates a mutation that runs remote writes and applies their cache effects.</summary>
        /// <typeparam name="TInput">The mutation input type.</typeparam>
        /// <typeparam name="TOutput">The mutation output type.</typeparam>
        /// <param name="definition">The mutation to create.</param>
        /// <returns>A mutation. Dispose it when its scope ends; disposing the client also disposes it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
        public Mutation<TInput, TOutput> CreateMutation<TInput, TOutput>(
            MutationDefinition<TInput, TOutput> definition)
        {
            AssertAvailable();
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var mutation = new Mutation<TInput, TOutput>(this, definition);
            _mutations.Add(mutation);
            return mutation;
        }

        /// <summary>
        /// Cancels all fetches and mutation executions, disposes all observers and mutations, and clears the cache.
        /// </summary>
        /// <remarks>
        /// Pending mutation tasks complete as canceled. Calling <see cref="Dispose"/> more than once has no effect.
        /// If the unhandled-exception handler throws while reporting queued listener errors, the first such exception is rethrown.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Called from a thread other than the client owner's thread.</exception>
        public void Dispose()
        {
            AssertOwnerThread();
            if (_isDisposed)
            {
                return;
            }

            Action[] pendingCompletions = _afterNotifications.ToArray();
            _afterNotifications.Clear();
            _isDisposed = true;
            foreach (IMutationLifetime mutation in _mutations)
            {
                mutation.OnClientDisposed();
            }

            _mutations.Clear();
            foreach (QueryEntry entry in _entries.Values)
            {
                entry.CancelActive(this, ensureSupersededObservers: false);
                entry.MarkObserversDisposed();
            }

            _entries.Clear();
            _pendingNotifications.Clear();
            _pendingNotificationSet.Clear();
            _dispatchNotifications.Clear();
            _pendingMutationNotifications.Clear();
            _pendingMutationNotificationSet.Clear();
            _dispatchMutationNotifications.Clear();
            _deadlines.Clear();
            Exception handlerFailure = ReportObserverErrors();
            _runtime.Unregister(this);
            for (int index = 0; index < pendingCompletions.Length; index++)
            {
                pendingCompletions[index]();
            }

            if (handlerFailure != null)
            {
                throw handlerFailure;
            }
        }

        internal void AssertOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _runtime.OwnerThreadId)
            {
                throw new InvalidOperationException(
                    "WakeQuery public operations must run on the thread that owns the QueryClient.");
            }
        }

        internal void AssertUsable()
        {
            AssertAvailable();
        }

        internal void Post(Action callback)
        {
            _runtime.Post(callback);
        }

        internal void ScheduleEnsure<T>(QueryObserver<T> observer)
        {
            _runtime.Post(() =>
            {
                if (!_isDisposed && !((IQueryObserverInternal)observer).IsDisposed)
                {
                    EnsureObserved(observer, force: false);
                }
            });
        }

        internal void EnsureObserved<T>(QueryObserver<T> observer, bool force)
        {
            AssertAvailable();
            QueryEntry<T> entry = (QueryEntry<T>)((IQueryObserverInternal)observer).Entry;
            if (!force && !observer.State.IsStale)
            {
                return;
            }

            StartOrJoin(
                entry,
                observer.Definition,
                null,
                observer);
        }

        internal Task<T> Fetch<T>(
            QueryObserver<T> observer,
            bool force,
            CancellationToken cancellationToken)
        {
            AssertAvailable();
            QueryEntry<T> entry = (QueryEntry<T>)((IQueryObserverInternal)observer).Entry;
            if (!force && !observer.State.IsStale && entry.HasData)
            {
                return FromCached(entry.Data, cancellationToken);
            }

            return StartOrJoin(
                entry,
                observer.Definition,
                cancellationToken,
                observer);
        }

        internal void DetachObserver(IQueryObserverInternal observer)
        {
            if (_isDisposed)
            {
                return;
            }

            QueryEntry entry = observer.Entry;
            entry.Observers.Remove(observer);
            entry.RemoveFetchObserverInterest(observer);
            if (entry.HasActiveFetch && !entry.HasActiveFetchInterest)
            {
                entry.CancelActive(this, ensureSupersededObservers: true);
                return;
            }

            if (entry.IsUnused)
            {
                ScheduleGarbageCollection(entry);
            }
        }

        internal void QueueNotification(IQueryObserverInternal observer)
        {
            if (!_isDisposed && _pendingNotificationSet.Add(observer))
            {
                _pendingNotifications.Add(observer);
            }
        }

        internal void QueueMutationNotification(IMutationNotification mutation)
        {
            if (!_isDisposed && _pendingMutationNotificationSet.Add(mutation))
            {
                _pendingMutationNotifications.Add(mutation);
            }
        }

        internal void DetachMutation(IMutationLifetime mutation)
        {
            if (!_isDisposed)
            {
                _mutations.Remove(mutation);
            }
        }

        internal void QueueAfterNotifications(Action callback)
        {
            if (_isDisposed)
            {
                callback();
            }
            else
            {
                _afterNotifications.Add(callback);
            }
        }

        internal DeadlineHandle ScheduleDeadline(TimeSpan dueAt, Action callback)
        {
            return _deadlines.Schedule(dueAt, callback);
        }

        internal TimeSpan AddDelay(TimeSpan origin, TimeSpan delay)
        {
            return delay > TimeSpan.MaxValue - origin
                ? TimeSpan.MaxValue
                : origin + delay;
        }

        internal void ReportObserverException(Exception exception)
        {
            _observerErrors.Add(exception);
        }

        internal void CancelActiveFetch<T>(
            QueryEntry<T> entry,
            bool ensureSupersededObservers)
        {
            FetchGeneration<T> generation = entry.ActiveFetch;
            if (generation == null)
            {
                return;
            }

            entry.ActiveFetch = null;
            generation.ObserverInterests.Clear();
            generation.RetryDeadline?.Cancel();
            generation.RetryDeadline = null;
            RequestCancellation(generation.Cancellation);
            RestorePreviousOutcome(entry, generation);
            CompleteWaitersCanceled(generation);
            generation.Cancellation.Dispose();
            entry.NotifyObservers();
            NotifyFetchSettled(entry);
            EmitDiagnostic(
                QueryDiagnosticKind.FetchCanceled,
                entry.Key,
                generation.Id,
                entry.FailureCount,
                null);
            if (ensureSupersededObservers &&
                generation.CacheGeneration != entry.CacheGeneration &&
                entry.Observers.Count > 0)
            {
                ScheduleObservedEnsures(entry);
            }
            else if (entry.IsUnused)
            {
                ScheduleGarbageCollection(entry);
            }
        }

        internal void ApplyMutationEffects(IReadOnlyList<Action<QueryClient>> effects)
        {
            foreach (var actionClient in effects) 
                actionClient(this);
        }

        internal void RequestCancellation(CancellationTokenSource cancellation)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (Exception exception)
            {
                ReportObserverException(exception);
            }
        }

        internal void EmitMutationDiagnostic(
            QueryDiagnosticKind kind,
            long executionId,
            Exception exception)
        {
            EmitDiagnostic(kind, null, executionId, 0, exception);
        }

        internal void Pump()
        {
            if (_isDisposed || _isPumping)
            {
                return;
            }

            AssertOwnerThread();
            _isPumping = true;
            try
            {
                _deadlines.RunDue(Elapsed);
                DispatchNotifications();
                RunAfterNotifications();
                Exception handlerFailure = ReportObserverErrors();
                if (handlerFailure != null)
                {
                    throw handlerFailure;
                }
            }
            finally
            {
                _isPumping = false;
            }
        }

        internal void OnFocusChanged(bool isFocused)
        {
            AssertAvailable();
            if (_isFocused == isFocused)
            {
                return;
            }

            _isFocused = isFocused;
            List<IQueryObserverInternal> observers = GetObserversSnapshot();
            foreach (var observer in observers) 
                observer.OnFocusChanged(isFocused);
        }

        internal void OnReconnect()
        {
            AssertAvailable();
            List<IQueryObserverInternal> observers = GetObserversSnapshot();
            foreach (var observer in observers)
            {
                observer.OnReconnect();
            }
        }

        internal void AbandonFromRuntime()
        {
            if (_isDisposed)
                return;

            Action[] pendingCompletions = _afterNotifications.ToArray();
            _afterNotifications.Clear();
            _isDisposed = true;
            foreach (IMutationLifetime mutation in _mutations) 
                mutation.OnClientDisposed();

            _mutations.Clear();
            foreach (QueryEntry entry in _entries.Values)
            {
                entry.CancelActive(this, ensureSupersededObservers: false);
                entry.MarkObserversDisposed();
            }

            _entries.Clear();
            _pendingNotifications.Clear();
            _pendingNotificationSet.Clear();
            _dispatchNotifications.Clear();
            _pendingMutationNotifications.Clear();
            _pendingMutationNotificationSet.Clear();
            _dispatchMutationNotifications.Clear();
            _observerErrors.Clear();
            _deadlines.Clear();
            foreach (var pendingCompletion in pendingCompletions) 
                pendingCompletion();
        }

        private Task<T> FetchImperative<T>(
            QueryDefinition<T> definition,
            bool force,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            QueryEntry<T> entry = GetOrCreateEntry(definition.Key);
            entry.MaxUnusedFor = Max(entry.MaxUnusedFor, definition.Policy.UnusedFor);
            entry.HasRetentionPolicy = true;
            if (!force && IsFresh(entry, definition.Policy))
            {
                return FromCached(entry.Data, cancellationToken);
            }

            return StartOrJoin(
                entry,
                definition,
                cancellationToken,
                null);
        }

        private Task<T> StartOrJoin<T>(
            QueryEntry<T> entry,
            QueryDefinition<T> definition,
            CancellationToken? callerCancellation,
            IQueryObserverInternal observerInterest)
        {
            if (callerCancellation.HasValue &&
                callerCancellation.Value.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(callerCancellation.Value);
            }

            entry.GarbageCollectionGeneration++;
            entry.GarbageCollectionDeadline?.Cancel();
            entry.GarbageCollectionDeadline = null;

            FetchGeneration<T> generation = entry.ActiveFetch;
            bool startsGeneration = generation == null;
            if (startsGeneration)
            {
                generation = new FetchGeneration<T>(
                    ++_fetchGeneration,
                    entry.CacheGeneration,
                    definition,
                    entry.Status,
                    entry.Error,
                    entry.FailureCount);
                entry.ActiveFetch = generation;
            }

            if (observerInterest != null)
            {
                generation.ObserverInterests.Add(observerInterest);
            }

            QueryWaiter<T> waiter = null;
            if (callerCancellation.HasValue)
            {
                waiter = new QueryWaiter<T>(callerCancellation.Value);
                generation.Waiters.Add(waiter);
                if (callerCancellation.Value.CanBeCanceled)
                {
                    waiter.Registration = callerCancellation.Value.Register(
                        () => _runtime.Post(
                            () => CancelWaiter(entry, generation, waiter)));
                }
            }

            if (startsGeneration)
            {
                entry.NotifyObservers();
                EmitDiagnostic(
                    QueryDiagnosticKind.FetchStarted,
                    entry.Key,
                    generation.Id,
                    0,
                    null);
                if (IsCurrent(entry, generation))
                {
                    InvokeFetch(entry, generation);
                }
            }
            else
            {
                EmitDiagnostic(
                    QueryDiagnosticKind.FetchJoined,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    null);
            }

            return waiter?.Completion.Task;
        }

        private void InvokeFetch<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation)
        {
            generation.IsWaitingToRetry = false;
            Task<T> task;
            try
            {
                task = generation.Definition.Fetch(generation.Cancellation.Token);
                if (task == null)
                {
                    task = Task.FromException<T>(
                        new InvalidOperationException(
                            "A WakeQuery fetch delegate returned null."));
                }
            }
            catch (Exception exception)
            {
                task = Task.FromException<T>(exception);
            }

            ObserveFetch(entry, generation, task);
        }

        private void ObserveFetch<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation,
            Task<T> task)
        {
            _ = task.ContinueWith(
                async completed =>
                {
                    if (completed.IsCanceled)
                    {
                        var exception = new TaskCanceledException(completed);
                        _runtime.Post(
                            () => CompleteFetchCanceled(
                                entry,
                                generation,
                                exception));
                        return;
                    }

                    if (completed.IsFaulted)
                    {
                        if (completed.Exception == null)
                            return;
                        
                        Exception exception =
                            completed.Exception.InnerException ?? completed.Exception;
                        if (exception is OperationCanceledException canceled)
                        {
                            _runtime.Post(
                                () => CompleteFetchCanceled(
                                    entry,
                                    generation,
                                    canceled));
                        }
                        else
                        {
                            _runtime.Post(
                                () => CompleteFetchFailure(
                                    entry,
                                    generation,
                                    exception));
                        }

                        return;
                    }

                    T result = await completed.ConfigureAwait(false);
                    _runtime.Post(
                        () => CompleteFetchSuccess(entry, generation, result));
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                InlineContinuationScheduler.Instance);
        }

        private void CompleteFetchSuccess<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation,
            T result)
        {
            if (!IsCurrent(entry, generation))
            {
                EmitDiagnostic(
                    QueryDiagnosticKind.ObsoleteResultDiscarded,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    null);
                CompleteWaitersSuccess(generation, result);
                generation.RetryDeadline?.Cancel();
                generation.RetryDeadline = null;
                generation.Cancellation.Dispose();
                return;
            }

            entry.ActiveFetch = null;
            generation.ObserverInterests.Clear();
            generation.RetryDeadline?.Cancel();
            generation.RetryDeadline = null;
            bool canWrite = generation.CacheGeneration == entry.CacheGeneration;
            if (canWrite)
            {
                entry.Status = QueryStatus.Success;
                entry.HasData = true;
                entry.Data = result;
                entry.Error = null;
                entry.FailureCount = 0;
                entry.UpdatedElapsed = Elapsed;
                entry.UpdatedAt = _runtime.UtcNow;
                entry.IsInvalidated = false;
                entry.CacheGeneration++;
                EmitDiagnostic(
                    QueryDiagnosticKind.FetchSucceeded,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    null);
            }
            else
            {
                EmitDiagnostic(
                    QueryDiagnosticKind.ObsoleteResultDiscarded,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    null);
            }

            entry.NotifyObservers();
            NotifyFetchSettled(entry);
            CompleteWaitersSuccess(generation, result);
            generation.Cancellation.Dispose();

            if (!canWrite && entry.Observers.Count > 0)
            {
                ScheduleObservedEnsures(entry);
            }
            else if (entry.IsUnused)
            {
                ScheduleGarbageCollection(entry);
            }
        }

        private void CompleteFetchFailure<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation,
            Exception exception)
        {
            if (!IsCurrent(entry, generation))
            {
                CompleteWaitersFailure(generation, exception);
                generation.RetryDeadline?.Cancel();
                generation.RetryDeadline = null;
                generation.Cancellation.Dispose();
                return;
            }

            generation.FailedAttempts++;
            generation.LastFailure = exception;
            bool canWrite = generation.CacheGeneration == entry.CacheGeneration;
            RetryPolicy retry = generation.Definition.Policy.Retry;
            bool shouldRetry = false;
            if (canWrite && HasInterest(entry))
            {
                try
                {
                    shouldRetry = retry.AllowsRetry(
                        exception,
                        generation.FailedAttempts);
                }
                catch (Exception predicateException)
                {
                    exception = predicateException;
                    generation.LastFailure = predicateException;
                }
            }

            if (!IsCurrent(entry, generation))
            {
                return;
            }

            canWrite = generation.CacheGeneration == entry.CacheGeneration;
            if (canWrite)
            {
                entry.FailureCount = generation.FailedAttempts;
                entry.Error = exception;
            }
            else
            {
                shouldRetry = false;
            }

            if (shouldRetry)
            {
                generation.IsWaitingToRetry = true;
                entry.NotifyObservers();
                TimeSpan delay = retry.GetDelay(generation.FailedAttempts);
                EmitDiagnostic(
                    QueryDiagnosticKind.FetchRetryScheduled,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    exception);
                DeadlineHandle handle = null;
                handle = ScheduleDeadline(AddDelay(Elapsed, delay), () =>
                {
                    if (ReferenceEquals(generation.RetryDeadline, handle))
                    {
                        generation.RetryDeadline = null;
                    }

                    if (IsCurrent(entry, generation))
                    {
                        if (generation.CacheGeneration != entry.CacheGeneration)
                        {
                            CompleteSupersededRetry(
                                entry,
                                generation,
                                exception);
                        }
                        else if (HasInterest(entry))
                        {
                            InvokeFetch(entry, generation);
                            entry.NotifyObservers();
                        }
                        else
                        {
                            CancelActiveFetch(
                                entry,
                                ensureSupersededObservers: true);
                        }
                    }
                });
                generation.RetryDeadline = handle;
                return;
            }

            entry.ActiveFetch = null;
            generation.ObserverInterests.Clear();
            generation.RetryDeadline?.Cancel();
            generation.RetryDeadline = null;
            if (canWrite)
            {
                entry.Status = QueryStatus.Error;
                entry.IsInvalidated = true;
                EmitDiagnostic(
                    QueryDiagnosticKind.FetchFailed,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    exception);
            }
            else
            {
                EmitDiagnostic(
                    QueryDiagnosticKind.ObsoleteResultDiscarded,
                    entry.Key,
                    generation.Id,
                    generation.FailedAttempts,
                    exception);
            }

            entry.NotifyObservers();
            NotifyFetchSettled(entry);
            CompleteWaitersFailure(generation, exception);
            generation.Cancellation.Dispose();
            if (!canWrite && entry.Observers.Count > 0)
            {
                ScheduleObservedEnsures(entry);
            }
            else if (entry.IsUnused)
            {
                ScheduleGarbageCollection(entry);
            }
        }

        private void CompleteSupersededRetry<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation,
            Exception exception)
        {
            if (!IsCurrent(entry, generation))
            {
                return;
            }

            entry.ActiveFetch = null;
            generation.IsWaitingToRetry = false;
            generation.ObserverInterests.Clear();
            entry.NotifyObservers();
            NotifyFetchSettled(entry);
            CompleteWaitersFailure(generation, exception);
            generation.Cancellation.Dispose();
            EmitDiagnostic(
                QueryDiagnosticKind.ObsoleteResultDiscarded,
                entry.Key,
                generation.Id,
                generation.FailedAttempts,
                exception);
            if (entry.Observers.Count > 0)
            {
                ScheduleObservedEnsures(entry);
            }
            else if (entry.IsUnused)
            {
                ScheduleGarbageCollection(entry);
            }
        }

        internal bool SupersedeWaitingRetry<T>(QueryEntry<T> entry)
        {
            FetchGeneration<T> generation = entry.ActiveFetch;
            if (generation == null || !generation.IsWaitingToRetry)
            {
                return false;
            }

            entry.ActiveFetch = null;
            generation.IsWaitingToRetry = false;
            generation.ObserverInterests.Clear();
            generation.RetryDeadline?.Cancel();
            generation.RetryDeadline = null;
            entry.NotifyObservers();
            NotifyFetchSettled(entry);
            EmitDiagnostic(
                QueryDiagnosticKind.ObsoleteResultDiscarded,
                entry.Key,
                generation.Id,
                generation.FailedAttempts,
                generation.LastFailure);
            CompleteWaitersFailure(generation, generation.LastFailure);
            generation.Cancellation.Dispose();
            return true;
        }

        private void CompleteFetchCanceled<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation,
            OperationCanceledException exception)
        {
            if (!IsCurrent(entry, generation))
            {
                return;
            }

            entry.ActiveFetch = null;
            generation.ObserverInterests.Clear();
            generation.RetryDeadline?.Cancel();
            generation.RetryDeadline = null;
            RestorePreviousOutcome(entry, generation);
            entry.NotifyObservers();
            NotifyFetchSettled(entry);
            CompleteWaitersCanceled(generation);
            generation.Cancellation.Dispose();
            EmitDiagnostic(
                QueryDiagnosticKind.FetchCanceled,
                entry.Key,
                generation.Id,
                0,
                exception);
            if (entry.IsUnused)
            {
                ScheduleGarbageCollection(entry);
            }
        }

        private void CancelWaiter<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation,
            QueryWaiter<T> waiter)
        {
            if (waiter.IsCompleted)
            {
                return;
            }

            waiter.IsCompleted = true;
            waiter.Registration.Dispose();
            generation.Waiters.Remove(waiter);
            waiter.Completion.TrySetCanceled(waiter.CancellationToken);
            if (IsCurrent(entry, generation) && !HasInterest(entry))
            {
                CancelActiveFetch(
                    entry,
                    ensureSupersededObservers: true);
            }
        }

        private static void CompleteWaitersSuccess<T>(
            FetchGeneration<T> generation,
            T result)
        {
            foreach (var waiter in generation.Waiters)
            {
                if (waiter.IsCompleted)
                    continue;

                waiter.IsCompleted = true;
                waiter.Registration.Dispose();
                waiter.Completion.TrySetResult(result);
            }

            generation.Waiters.Clear();
        }

        private static void CompleteWaitersFailure<T>(
            FetchGeneration<T> generation,
            Exception exception)
        {
            foreach (var waiter in generation.Waiters)
            {
                if (waiter.IsCompleted)
                    continue;

                waiter.IsCompleted = true;
                waiter.Registration.Dispose();
                waiter.Completion.TrySetException(exception);
            }

            generation.Waiters.Clear();
        }

        private static void CompleteWaitersCanceled<T>(
            FetchGeneration<T> generation)
        {
            foreach (var waiter in generation.Waiters)
            {
                if (waiter.IsCompleted)
                    continue;

                waiter.IsCompleted = true;
                waiter.Registration.Dispose();
                waiter.Completion.TrySetCanceled();
            }

            generation.Waiters.Clear();
        }

        private QueryEntry<T> GetOrCreateEntry<T>(QueryKey<T> key)
        {
            QueryKeyIdentity identity = key.Identity;
            if (_entries.TryGetValue(identity, out QueryEntry existing))
            {
                if (existing.DataType != typeof(T))
                {
                    throw new QueryTypeMismatchException(
                        identity.ToString(),
                        existing.DataType,
                        typeof(T));
                }

                return (QueryEntry<T>)existing;
            }

            var entry = new QueryEntry<T>(identity);
            _entries.Add(identity, entry);
            return entry;
        }

        private QueryEntry<T> GetExistingEntry<T>(QueryKey<T> key)
        {
            QueryKeyIdentity identity = key.Identity;
            if (!_entries.TryGetValue(identity, out QueryEntry existing))
                return null;

            if (existing.DataType != typeof(T))
                throw new QueryTypeMismatchException(
                    identity.ToString(),
                    existing.DataType,
                    typeof(T));

            return (QueryEntry<T>)existing;
        }

        private void ScheduleGarbageCollection(QueryEntry entry)
        {
            entry.GarbageCollectionDeadline?.Cancel();
            entry.GarbageCollectionDeadline = null;
            if (!entry.IsUnused)
                return;

            long generation = ++entry.GarbageCollectionGeneration;
            DeadlineHandle handle = null;
            handle = ScheduleDeadline(AddDelay(Elapsed, entry.MaxUnusedFor), () =>
            {
                if (ReferenceEquals(entry.GarbageCollectionDeadline, handle))
                {
                    entry.GarbageCollectionDeadline = null;
                }

                if (entry.GarbageCollectionGeneration != generation ||
                    !entry.IsUnused ||
                    !_entries.TryGetValue(entry.Key, out QueryEntry current) ||
                    !ReferenceEquals(current, entry))
                {
                    return;
                }

                _entries.Remove(entry.Key);
                EmitDiagnostic(
                    QueryDiagnosticKind.Evicted,
                    entry.Key,
                    0,
                    0,
                    null);
            });
            entry.GarbageCollectionDeadline = handle;
        }

        private void ScheduleObservedEnsures(QueryEntry entry)
        {
            foreach (var observer in entry.Observers) 
                ScheduleUntypedEnsure(observer);
        }

        private void ScheduleUntypedEnsure(IQueryObserverInternal observer)
        {
            _runtime.Post(() =>
            {
                if (_isDisposed || observer.IsDisposed)
                {
                    return;
                }

                EnsureObserverUntyped(observer);
            });
        }

        private void EnsureObserverUntyped(IQueryObserverInternal observer)
        {
            observer.Ensure(force: false);
        }

        private void NotifyFetchSettled(QueryEntry entry)
        {
            foreach (var observer in entry.Observers) 
                observer.OnFetchSettled();
        }

        private void DispatchNotifications()
        {
            _dispatchNotifications.AddRange(_pendingNotifications);
            _pendingNotifications.Clear();
            _pendingNotificationSet.Clear();
            foreach (var observer in _dispatchNotifications)
            {
                if (!observer.IsDisposed) 
                    observer.Dispatch(ReportObserverException);
            }

            _dispatchNotifications.Clear();

            _dispatchMutationNotifications.AddRange(_pendingMutationNotifications);
            _pendingMutationNotifications.Clear();
            _pendingMutationNotificationSet.Clear();
            foreach (var mutation in _dispatchMutationNotifications) 
                mutation.Dispatch(ReportObserverException);

            _dispatchMutationNotifications.Clear();
        }

        private void RunAfterNotifications()
        {
            _dispatchAfterNotifications.AddRange(_afterNotifications);
            _afterNotifications.Clear();
            foreach (var dispatchAfterNotification in _dispatchAfterNotifications) 
                dispatchAfterNotification();

            _dispatchAfterNotifications.Clear();
        }

        private Exception ReportObserverErrors()
        {
            Exception handlerFailure = null;
            foreach (var observerError in _observerErrors)
            {
                try
                {
                    _unhandledException(observerError);
                }
                catch (Exception exception)
                {
                    handlerFailure ??= exception;
                }
            }

            _observerErrors.Clear();
            return handlerFailure;
        }

        private void EmitDiagnostic(
            QueryDiagnosticKind kind,
            QueryKeyIdentity key,
            long executionId,
            int failureCount,
            Exception exception)
        {
            if (_diagnostics == null)
            {
                return;
            }

            try
            {
                _diagnostics.OnEvent(new QueryDiagnosticEvent(
                    kind,
                    key?.ToString(),
                    executionId,
                    failureCount,
                    exception));
            }
            catch (Exception diagnosticException)
            {
                ReportObserverException(diagnosticException);
            }
        }

        private void AssertAvailable()
        {
            AssertOwnerThread();
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(QueryClient));
            }
        }

        private bool IsCurrent<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation)
        {
            return !_isDisposed &&
                   _entries.TryGetValue(entry.Key, out QueryEntry current) &&
                   ReferenceEquals(current, entry) &&
                   ReferenceEquals(entry.ActiveFetch, generation);
        }

        private bool IsFresh<T>(
            QueryEntry<T> entry,
            QueryPolicy policy)
        {
            return entry.HasData &&
                   !entry.IsInvalidated &&
                   Elapsed - entry.UpdatedElapsed < policy.StaleAfter;
        }

        private bool HasInterest(QueryEntry entry)
        {
            return entry.HasActiveFetchInterest;
        }

        private List<QueryEntry> FindMatches(QueryFilter filter)
        {
            filter.Validate();
            var matches = new List<QueryEntry>();
            foreach (KeyValuePair<QueryKeyIdentity, QueryEntry> pair in _entries)
            {
                if (filter.Matches(pair.Key)) 
                    matches.Add(pair.Value);
            }

            return matches;
        }

        private List<IQueryObserverInternal> GetObserversSnapshot()
        {
            var observers = new List<IQueryObserverInternal>();
            foreach (QueryEntry entry in _entries.Values)
            {
                observers.AddRange(entry.Observers);
            }

            return observers;
        }

        private bool IsCurrentEntry(QueryEntry entry)
        {
            return _entries.TryGetValue(entry.Key, out QueryEntry current) &&
                   ReferenceEquals(current, entry);
        }

        private static void RestorePreviousOutcome<T>(
            QueryEntry<T> entry,
            FetchGeneration<T> generation)
        {
            if (generation.CacheGeneration != entry.CacheGeneration)
            {
                return;
            }

            entry.Status = generation.PreviousStatus;
            entry.Error = generation.PreviousError;
            entry.FailureCount = generation.PreviousFailureCount;
        }

        private static Task<T> FromCached<T>(
            T data,
            CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<T>(cancellationToken)
                : Task.FromResult(data);
        }

        private static TimeSpan Max(TimeSpan first, TimeSpan second)
        {
            return first >= second ? first : second;
        }

    }

    internal interface IMutationNotification
    {
        void Dispatch(Action<Exception> reportException);
    }

    internal interface IMutationLifetime : IMutationNotification
    {
        void OnClientDisposed();
    }
}
