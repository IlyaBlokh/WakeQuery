using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WakeQuery.Internal;

namespace WakeQuery
{
    public sealed class Mutation<TInput, TOutput> :
        IDisposable,
        IMutationLifetime
    {
        private readonly QueryClient _client;
        private readonly MutationDefinition<TInput, TOutput> _definition;
        private readonly Dictionary<long, Execution> _executions = new();
        private readonly List<Action<MutationState<TOutput>>> _listeners = new();
        private readonly List<Action<MutationState<TOutput>>> _deferredListeners = new();
        private MutationStatus _terminalStatus = MutationStatus.Idle;
        private bool _terminalHasData;
        private TOutput _terminalData;
        private Exception _terminalError;
        private long _lastCompletedExecutionId;
        private long _nextExecutionId;
        private long _revision;
        private MutationState<TOutput> _state;
        private bool _isDispatching;
        private bool _isDisposed;

        internal Mutation(
            QueryClient client,
            MutationDefinition<TInput, TOutput> definition)
        {
            _client = client;
            _definition = definition;
            _state = BuildState();
        }

        public MutationState<TOutput> State
        {
            get
            {
                AssertAvailable();
                return _state;
            }
        }

        public IDisposable Subscribe(Action<MutationState<TOutput>> listener)
        {
            AssertAvailable();
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

            return new MutationSubscription(this, listener);
        }

        public Task<TOutput> ExecuteAsync(
            TInput input,
            CancellationToken cancellationToken = default)
        {
            AssertAvailable();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<TOutput>(cancellationToken);
            }

            long id = ++_nextExecutionId;
            CancellationTokenSource cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var execution = new Execution(id, input, cancellation);
            _executions.Add(id, execution);
            PublishState();
            _client.EmitMutationDiagnostic(
                QueryDiagnosticKind.MutationStarted,
                id,
                null);

            Task<TOutput> task;
            try
            {
                task = _definition.Execute(input, cancellation.Token);
                if (task == null)
                {
                    task = Task.FromException<TOutput>(
                        new InvalidOperationException(
                            "A WakeQuery mutation delegate returned null."));
                }
            }
            catch (Exception exception)
            {
                task = Task.FromException<TOutput>(exception);
            }

            ObserveExecution(execution, task);
            return execution.Completion.Task;
        }

        public void Cancel()
        {
            AssertAvailable();
            var executions = new List<Execution>(_executions.Values);
            foreach (var execution in executions)
            {
                _client.RequestCancellation(execution.Cancellation);
            }
        }

        public void Dispose()
        {
            _client.AssertOwnerThread();
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            foreach (Execution execution in _executions.Values)
            {
                _client.RequestCancellation(execution.Cancellation);
                execution.Completion.TrySetCanceled();
                execution.Cancellation.Dispose();
            }

            _executions.Clear();
            _listeners.Clear();
            _deferredListeners.Clear();
            _client.DetachMutation(this);
        }

        void IMutationNotification.Dispatch(Action<Exception> reportException)
        {
            if (_isDisposed)
            {
                return;
            }

            DispatchListeners(reportException);
        }

        void IMutationLifetime.OnClientDisposed()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            foreach (Execution execution in _executions.Values)
            {
                _client.RequestCancellation(execution.Cancellation);
                execution.Completion.TrySetCanceled();
                execution.Cancellation.Dispose();
            }

            _executions.Clear();
            _listeners.Clear();
            _deferredListeners.Clear();
        }

        private void ObserveExecution(
            Execution execution,
            Task<TOutput> task)
        {
            _ = task.ContinueWith(
                async completed =>
                {
                    if (completed.IsCanceled)
                    {
                        var exception = new TaskCanceledException(completed);
                        _client.Post(
                            () => CompleteCanceled(execution, exception));
                        return;
                    }

                    if (completed.IsFaulted)
                    {
                        if (completed.Exception != null)
                        {
                            Exception exception =
                                completed.Exception.InnerException ?? completed.Exception;
                            if (exception is OperationCanceledException canceled)
                            {
                                _client.Post(
                                    () => CompleteCanceled(execution, canceled));
                            }
                            else
                            {
                                bool cancellationRequested =
                                    execution.Cancellation.IsCancellationRequested;
                                _client.Post(
                                    () => CompleteFailure(
                                        execution,
                                        exception,
                                        cancellationRequested));
                            }
                        }

                        return;
                    }

                    bool cancellationRequestedBeforeCompletion =
                        execution.Cancellation.IsCancellationRequested;
                    TOutput output = await completed.ConfigureAwait(false);
                    _client.Post(
                        () => CompleteSuccess(
                            execution,
                            output,
                            cancellationRequestedBeforeCompletion));
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                InlineContinuationScheduler.Instance);
        }

        private void CompleteSuccess(
            Execution execution,
            TOutput output,
            bool cancellationRequested)
        {
            if (!TryTakeExecution(execution))
            {
                return;
            }

            if (cancellationRequested)
            {
                FinishCanceled(execution, null);
                return;
            }

            Exception completionError = null;
            if (_definition.OnSuccess != null)
            {
                var success = new MutationSuccess<TInput, TOutput>(
                    execution.Input,
                    output);
                try
                {
                    _definition.OnSuccess(success);
                    success.Deactivate();
                    _client.ApplyMutationEffects(success.Effects);
                }
                catch (Exception exception)
                {
                    success.Deactivate();
                    completionError = new MutationEffectException(exception);
                }
            }

            if (completionError == null)
            {
                RecordTerminal(
                    execution.Id,
                    MutationStatus.Success,
                    hasData: true,
                    output,
                    null);
                _client.EmitMutationDiagnostic(
                    QueryDiagnosticKind.MutationSucceeded,
                    execution.Id,
                    null);
            }
            else
            {
                RecordTerminal(
                    execution.Id,
                    MutationStatus.Error,
                    hasData: false,
                    default,
                    completionError);
                _client.EmitMutationDiagnostic(
                    QueryDiagnosticKind.MutationFailed,
                    execution.Id,
                    completionError);
            }

            execution.Cancellation.Dispose();
            PublishState();
            _client.QueueAfterNotifications(() =>
            {
                if (completionError == null)
                {
                    execution.Completion.TrySetResult(output);
                }
                else
                {
                    execution.Completion.TrySetException(completionError);
                }
            });
        }

        private void CompleteFailure(
            Execution execution,
            Exception exception,
            bool cancellationRequested)
        {
            if (!TryTakeExecution(execution))
            {
                return;
            }

            if (cancellationRequested)
            {
                FinishCanceled(execution, exception);
                return;
            }

            RecordTerminal(
                execution.Id,
                MutationStatus.Error,
                hasData: false,
                default,
                exception);
            execution.Cancellation.Dispose();
            PublishState();
            _client.EmitMutationDiagnostic(
                QueryDiagnosticKind.MutationFailed,
                execution.Id,
                exception);
            _client.QueueAfterNotifications(
                () => execution.Completion.TrySetException(exception));
        }

        private void CompleteCanceled(
            Execution execution,
            OperationCanceledException exception)
        {
            if (TryTakeExecution(execution))
            {
                FinishCanceled(execution, exception);
            }
        }

        private void FinishCanceled(
            Execution execution,
            Exception exception)
        {
            execution.Cancellation.Dispose();
            PublishState();
            _client.EmitMutationDiagnostic(
                QueryDiagnosticKind.MutationCanceled,
                execution.Id,
                exception);
            _client.QueueAfterNotifications(
                () => execution.Completion.TrySetCanceled());
        }

        private bool TryTakeExecution(Execution execution)
        {
            return !_isDisposed &&
                   _executions.Remove(execution.Id);
        }

        private void RecordTerminal(
            long executionId,
            MutationStatus status,
            bool hasData,
            TOutput data,
            Exception error)
        {
            if (executionId < _lastCompletedExecutionId)
            {
                return;
            }

            _lastCompletedExecutionId = executionId;
            _terminalStatus = status;
            _terminalHasData = hasData;
            _terminalData = data;
            _terminalError = error;
        }

        private void PublishState()
        {
            _revision++;
            _state = BuildState();
            _client.QueueMutationNotification(this);
        }

        private MutationState<TOutput> BuildState()
        {
            return new MutationState<TOutput>(
                _executions.Count > 0
                    ? MutationStatus.Pending
                    : _terminalStatus,
                _executions.Count,
                _terminalHasData,
                _terminalData,
                _terminalError,
                _lastCompletedExecutionId,
                _revision);
        }

        private void InvokeListener(Action<MutationState<TOutput>> listener)
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
                Action<MutationState<TOutput>>[] listeners = _listeners.ToArray();
                for (int index = 0;
                     index < listeners.Length && !_isDisposed;
                     index++)
                {
                    InvokeListener(listeners[index], reportException);
                }

                while (_deferredListeners.Count > 0 && !_isDisposed)
                {
                    Action<MutationState<TOutput>> listener =
                        _deferredListeners[0];
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
            Action<MutationState<TOutput>> listener,
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

        private void Unsubscribe(Action<MutationState<TOutput>> listener)
        {
            _client.AssertOwnerThread();
            if (_isDisposed)
            {
                return;
            }

            _listeners.Remove(listener);
            _deferredListeners.Remove(listener);
        }

        private void AssertAvailable()
        {
            _client.AssertUsable();
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(Mutation<TInput, TOutput>));
            }
        }

        private sealed class Execution
        {
            public Execution(
                long id,
                TInput input,
                CancellationTokenSource cancellation)
            {
                Id = id;
                Input = input;
                Cancellation = cancellation;
                Completion = new TaskCompletionSource<TOutput>();
            }

            public long Id { get; }

            public TInput Input { get; }

            public CancellationTokenSource Cancellation { get; }

            public TaskCompletionSource<TOutput> Completion { get; }
        }

        private sealed class MutationSubscription : IDisposable
        {
            private Mutation<TInput, TOutput> _mutation;
            private Action<MutationState<TOutput>> _listener;

            public MutationSubscription(
                Mutation<TInput, TOutput> mutation,
                Action<MutationState<TOutput>> listener)
            {
                _mutation = mutation;
                _listener = listener;
            }

            public void Dispose()
            {
                Mutation<TInput, TOutput> mutation = _mutation;
                Action<MutationState<TOutput>> listener = _listener;
                if (mutation == null)
                {
                    return;
                }

                _mutation = null;
                _listener = null;
                mutation.Unsubscribe(listener);
            }
        }
    }
}
