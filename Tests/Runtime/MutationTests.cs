using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WakeQuery.Testing;

namespace WakeQuery.Tests
{
    public sealed class MutationTests
    {
        [Test]
        public async Task ConcurrentStateUsesHighestNonCanceledExecutionId()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var first = new TaskCompletionSource<string>();
            var second = new TaskCompletionSource<string>();
            var definition = new MutationDefinition<int, string>(
                (input, cancellationToken) => input == 1 ? first.Task : second.Task);
            using Mutation<int, string> mutation = client.CreateMutation(definition);

            Task<string> firstExecution = mutation.ExecuteAsync(1);
            Task<string> secondExecution = mutation.ExecuteAsync(2);

            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(mutation.State.PendingCount, Is.EqualTo(2));

            second.SetResult("second");
            runtime.RunOneFrame();
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Pending));
            Assert.That(mutation.State.PendingCount, Is.EqualTo(1));

            first.SetException(new InvalidOperationException("first failed"));
            runtime.RunOneFrame();

            Assert.That(await secondExecution, Is.EqualTo("second"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await firstExecution);
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Success));
            Assert.That(mutation.State.Data, Is.EqualTo("second"));
            Assert.That(mutation.State.LastCompletedExecutionId, Is.EqualTo(2));
        }

        [Test]
        public async Task SuccessEffectsPublishBeforeExecutionCompletes()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<int> walletKey = QueryKey.For<int>("wallet");
            client.SetData(walletKey, 1);
            var walletDefinition = new QueryDefinition<int>(
                walletKey,
                cancellationToken => Task.FromResult(0),
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            var states = new List<QueryState<int>>();
            using QueryObserver<int> wallet = client.Watch(walletDefinition, states.Add);
            var source = new TaskCompletionSource<int>();
            var mutationDefinition = new MutationDefinition<int, int>(
                (input, cancellationToken) => source.Task,
                success =>
                {
                    success.Set(walletKey, success.Output);
                    success.Update(walletKey, value => value + 1);
                });
            using Mutation<int, int> mutation = client.CreateMutation(mutationDefinition);

            Task<int> execution = mutation.ExecuteAsync(10);
            source.SetResult(10);
            runtime.RunOneFrame();

            Assert.That(execution.IsCompleted, Is.True);
            Assert.That(await execution, Is.EqualTo(10));
            Assert.That(wallet.State.Data, Is.EqualTo(11));
            Assert.That(states[^1].Data, Is.EqualTo(11));
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Success));
        }

        [Test]
        public void EffectFailureHasDistinctError()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => Task.FromResult(input),
                success => throw new InvalidOperationException("effect"));
            using Mutation<int, int> mutation = client.CreateMutation(definition);

            Task<int> execution = mutation.ExecuteAsync(1);
            runtime.RunOneFrame();

            Assert.ThrowsAsync<MutationEffectException>(async () => await execution);
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Error));
            Assert.That(mutation.State.Error, Is.TypeOf<MutationEffectException>());
        }

        [Test]
        public void CanceledExecutionDoesNotBecomeError()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) =>
                {
                    var completion = new TaskCompletionSource<int>();
                    cancellationToken.Register(
                        () => completion.TrySetCanceled(cancellationToken));
                    return completion.Task;
                });
            using Mutation<int, int> mutation = client.CreateMutation(definition);
            using var cancellation = new CancellationTokenSource();

            Task<int> execution = mutation.ExecuteAsync(1, cancellation.Token);
            cancellation.Cancel();
            runtime.RunOneFrame();

            Assert.That(execution.IsCanceled, Is.True);
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Idle));
            Assert.That(mutation.State.Error, Is.Null);
        }

        [Test]
        public void DisposingClientCancelsActiveMutation()
        {
            using var runtime = new ManualQueryRuntime();
            QueryClient client = runtime.CreateClient();
            CancellationToken operationToken = default;
            var source = new TaskCompletionSource<int>();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) =>
                {
                    operationToken = cancellationToken;
                    return source.Task;
                });
            Mutation<int, int> mutation = client.CreateMutation(definition);
            Task<int> execution = mutation.ExecuteAsync(1);

            client.Dispose();

            Assert.That(operationToken.IsCancellationRequested, Is.True);
            Assert.That(execution.IsCanceled, Is.True);
            Assert.Throws<ObjectDisposedException>(() => mutation.ExecuteAsync(2));
        }

        [Test]
        public void RuntimeResetCancelsActiveMutation()
        {
            using var runtime = new ManualQueryRuntime();
            QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<int>();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => source.Task);
            Mutation<int, int> mutation = client.CreateMutation(definition);
            Task<int> execution = mutation.ExecuteAsync(1);

            client.AbandonFromRuntime();

            Assert.That(execution.IsCanceled, Is.True);
            Assert.Throws<ObjectDisposedException>(() => mutation.ExecuteAsync(2));
        }

        [Test]
        public void CompletionBeforeCancellationStillPublishesSuccessEffects()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<int> key = QueryKey.For<int>("value");
            var source = new TaskCompletionSource<int>();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => source.Task,
                success => success.Set(key, success.Output));
            using Mutation<int, int> mutation = client.CreateMutation(definition);
            using var cancellation = new CancellationTokenSource();
            Task<int> execution = mutation.ExecuteAsync(1, cancellation.Token);

            source.SetResult(42);
            cancellation.Cancel();
            runtime.RunOneFrame();

            Assert.That(execution.IsCompletedSuccessfully, Is.True);
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Success));
            Assert.That(client.UpdateData(key, value => value + 1), Is.True);
        }

        [Test]
        public void FailureBeforeCancellationStillPublishesTheFailure()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<int>();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => source.Task);
            using Mutation<int, int> mutation = client.CreateMutation(definition);
            using var cancellation = new CancellationTokenSource();
            Task<int> execution = mutation.ExecuteAsync(1, cancellation.Token);
            var failure = new InvalidOperationException("failed");

            source.SetException(failure);
            cancellation.Cancel();
            runtime.RunOneFrame();

            Assert.That(execution.IsFaulted, Is.True);
            Assert.That(execution.Exception?.InnerException, Is.SameAs(failure));
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Error));
            Assert.That(mutation.State.Error, Is.SameAs(failure));
        }

        [Test]
        public void SynchronousOperationCanceledExceptionIsCancellation()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) =>
                    throw new OperationCanceledException());
            using Mutation<int, int> mutation = client.CreateMutation(definition);

            Task<int> execution = mutation.ExecuteAsync(1);
            runtime.RunOneFrame();

            Assert.That(execution.IsCanceled, Is.True);
            Assert.That(mutation.State.Status, Is.EqualTo(MutationStatus.Idle));
            Assert.That(mutation.State.Error, Is.Null);
        }

        [Test]
        public void SubscribeDuringDispatchDoesNotInvokeNestedCallback()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => Task.FromResult(input));
            using Mutation<int, int> mutation = client.CreateMutation(definition);
            int depth = 0;
            int maximumDepth = 0;
            int nestedCalls = 0;
            IDisposable nested = null;
            using IDisposable first = mutation.Subscribe(state =>
            {
                if (state.Status != MutationStatus.Success || nested != null)
                {
                    return;
                }

                depth++;
                maximumDepth = Math.Max(maximumDepth, depth);
                nested = mutation.Subscribe(nestedState =>
                {
                    depth++;
                    maximumDepth = Math.Max(maximumDepth, depth);
                    nestedCalls++;
                    depth--;
                });
                depth--;
            });

            mutation.ExecuteAsync(1);
            runtime.RunOneFrame();

            Assert.That(maximumDepth, Is.EqualTo(1));
            Assert.That(nestedCalls, Is.EqualTo(1));
            nested.Dispose();
        }

        [Test]
        public async Task DisposalDuringMutationNotificationDoesNotLoseCompletion()
        {
            using var runtime = new ManualQueryRuntime();
            QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<int>();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => source.Task);
            using Mutation<int, int> mutation = client.CreateMutation(definition);
            using IDisposable subscription = mutation.Subscribe(state =>
            {
                if (state.Status == MutationStatus.Success)
                {
                    client.Dispose();
                }
            });
            Task<int> execution = mutation.ExecuteAsync(1);

            source.SetResult(42);
            runtime.RunOneFrame();

            Assert.That(execution.IsCompletedSuccessfully, Is.True);
            Assert.That(await execution, Is.EqualTo(42));
        }

        [Test]
        public async Task RuntimeAbandonDuringNotificationDoesNotLoseCompletion()
        {
            using var runtime = new ManualQueryRuntime();
            QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<int>();
            var definition = new MutationDefinition<int, int>(
                (input, cancellationToken) => source.Task);
            using Mutation<int, int> mutation = client.CreateMutation(definition);
            using IDisposable subscription = mutation.Subscribe(state =>
            {
                if (state.Status == MutationStatus.Success)
                {
                    client.AbandonFromRuntime();
                }
            });
            Task<int> execution = mutation.ExecuteAsync(1);

            source.SetResult(42);
            runtime.RunOneFrame();

            Assert.That(execution.IsCompletedSuccessfully, Is.True);
            Assert.That(await execution, Is.EqualTo(42));
        }
    }
}
