using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WakeQuery.Testing;

namespace WakeQuery.Tests
{
    public sealed class QueryLifecycleTests
    {
        [Test]
        public void WatchEmitsSynchronouslyButFetchStartsOnRuntimeCycle()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<string>();
            int fetchCount = 0;
            var states = new List<QueryState<string>>();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("profile"),
                cancellationToken =>
                {
                    fetchCount++;
                    return source.Task;
                });

            using QueryObserver<string> observer = client.Watch(definition, states.Add);

            Assert.That(states, Has.Count.EqualTo(1));
            Assert.That(states[0].Status, Is.EqualTo(QueryStatus.Empty));
            Assert.That(states[0].FetchActivity, Is.EqualTo(FetchActivity.Idle));
            Assert.That(fetchCount, Is.Zero);

            runtime.RunOneFrame();

            Assert.That(fetchCount, Is.EqualTo(1));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));
        }

        [Test]
        public async Task SharedKeyUsesOneAuthoritativeFetch()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<string>();
            int fetchCount = 0;
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("profile"),
                cancellationToken =>
                {
                    fetchCount++;
                    return source.Task;
                });
            using QueryObserver<string> first = client.Watch(definition);
            using QueryObserver<string> second = client.Watch(definition);

            Task<string> firstWait = first.EnsureAsync();
            Task<string> secondWait = second.EnsureAsync();
            runtime.RunOneFrame();

            Assert.That(fetchCount, Is.EqualTo(1));

            source.SetResult("Ada");
            runtime.RunOneFrame();

            Assert.That(await firstWait, Is.EqualTo("Ada"));
            Assert.That(await secondWait, Is.EqualTo("Ada"));
            Assert.That(first.State.Data, Is.EqualTo("Ada"));
            Assert.That(second.State.Data, Is.EqualTo("Ada"));
        }

        [Test]
        public async Task CancelingOneWaiterDoesNotCancelSharedFetch()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<string>();
            CancellationToken sharedToken = default;
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("profile"),
                cancellationToken =>
                {
                    sharedToken = cancellationToken;
                    return source.Task;
                });
            using QueryObserver<string> observer = client.Watch(definition);
            using var callerCancellation = new CancellationTokenSource();

            Task<string> canceledWait = observer.EnsureAsync(callerCancellation.Token);
            Task<string> survivingWait = observer.EnsureAsync();
            runtime.RunOneFrame();

            callerCancellation.Cancel();
            runtime.RunOneFrame();

            Assert.That(canceledWait.IsCanceled, Is.True);
            Assert.That(sharedToken.IsCancellationRequested, Is.False);

            source.SetResult("Ada");
            runtime.RunOneFrame();

            Assert.That(await survivingWait, Is.EqualTo("Ada"));
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
        }

        [Test]
        public void ImperativeRefetchCancelsWithoutARequiringObserver()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            CancellationToken sharedToken = default;
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    sharedToken = cancellationToken;
                    return source.Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            using var callerCancellation = new CancellationTokenSource();
            Task<string> result = client.RefetchAsync(
                definition,
                callerCancellation.Token);

            callerCancellation.Cancel();
            runtime.RunOneFrame();

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(sharedToken.IsCancellationRequested, Is.True);
            Assert.That(observer.State.Data, Is.EqualTo("cached"));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Idle));
        }

        [Test]
        public void ObservedGenerationSurvivesImperativeWaiterCancellation()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            CancellationToken sharedToken = default;
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("profile"),
                cancellationToken =>
                {
                    sharedToken = cancellationToken;
                    return source.Task;
                });
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();
            using var callerCancellation = new CancellationTokenSource();
            Task<string> result = client.EnsureAsync(
                definition,
                callerCancellation.Token);

            callerCancellation.Cancel();
            runtime.RunOneFrame();

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(sharedToken.IsCancellationRequested, Is.False);
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));
        }

        [Test]
        public void NewStaleObserverClaimsAnActiveImperativeGenerationImmediately()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            CancellationToken sharedToken = default;
            int fetchCount = 0;
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("profile"),
                cancellationToken =>
                {
                    fetchCount++;
                    sharedToken = cancellationToken;
                    return source.Task;
                });
            using var callerCancellation = new CancellationTokenSource();
            Task<string> result = client.EnsureAsync(
                definition,
                callerCancellation.Token);
            callerCancellation.Cancel();
            using QueryObserver<string> observer = client.Watch(definition);

            runtime.RunOneFrame();

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(fetchCount, Is.EqualTo(1));
            Assert.That(sharedToken.IsCancellationRequested, Is.False);
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));
        }

        [Test]
        public void CancelingSupersededImperativeFetchSchedulesObservedEnsure()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            var first = new TaskCompletionSource<string>();
            var second = new TaskCompletionSource<string>();
            CancellationToken firstToken = default;
            int fetchCount = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    if (fetchCount == 1)
                    {
                        firstToken = cancellationToken;
                        return first.Task;
                    }

                    return second.Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();
            using var callerCancellation = new CancellationTokenSource();
            Task<string> result = client.RefetchAsync(
                definition,
                callerCancellation.Token);

            client.Invalidate(QueryFilter.Exact(key));
            callerCancellation.Cancel();
            runtime.RunOneFrame();
            runtime.RunOneFrame();

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(firstToken.IsCancellationRequested, Is.True);
            Assert.That(fetchCount, Is.EqualTo(2));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));
        }

        [Test]
        public async Task RefetchKeepsSuccessfulDataVisible()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => source.Task,
                new QueryPolicy(staleAfter: TimeSpan.FromMinutes(1)));
            using QueryObserver<string> observer = client.Watch(definition);

            Task<string> refetch = observer.RefetchAsync();

            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
            Assert.That(observer.State.HasData, Is.True);
            Assert.That(observer.State.Data, Is.EqualTo("cached"));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));

            source.SetResult("fresh");
            runtime.RunOneFrame();

            Assert.That(await refetch, Is.EqualTo("fresh"));
        }

        [Test]
        public async Task RetryUsesDeterministicDeadlineAndKeepsOutcome()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            int attempts = 0;
            var secondAttempt = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("retry"),
                cancellationToken =>
                {
                    attempts++;
                    return attempts == 1
                        ? Task.FromException<string>(new TimeoutException("first"))
                        : secondAttempt.Task;
                },
                new QueryPolicy(
                    retry: RetryPolicy.Fixed(
                        maxAttempts: 2,
                        delay: TimeSpan.FromSeconds(3),
                        shouldRetry: exception => exception is TimeoutException)));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.EnsureAsync();

            runtime.RunOneFrame();
            runtime.RunOneFrame();

            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Empty));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.RetryDelay));
            Assert.That(observer.State.FailureCount, Is.EqualTo(1));
            Assert.That(attempts, Is.EqualTo(1));

            runtime.AdvanceBy(TimeSpan.FromSeconds(2));
            Assert.That(attempts, Is.EqualTo(1));

            runtime.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.That(attempts, Is.EqualTo(2));

            secondAttempt.SetResult("ok");
            runtime.RunOneFrame();

            Assert.That(await result, Is.EqualTo("ok"));
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
        }

        [Test]
        public void SupersededRetryDeadlineDoesNotStartAnotherFetch()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("retry-supersession");
            var failure = new TimeoutException("first");
            int attempts = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    attempts++;
                    return Task.FromException<string>(failure);
                },
                new QueryPolicy(
                    retry: RetryPolicy.Fixed(
                        2,
                        TimeSpan.FromSeconds(1))));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.EnsureAsync();
            runtime.RunOneFrame();
            client.SetData(key, "new");

            Assert.That(result.IsFaulted, Is.True);
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Idle));
            runtime.AdvanceBy(TimeSpan.FromSeconds(1));

            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(result.Exception?.InnerException, Is.SameAs(failure));
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
            Assert.That(observer.State.Data, Is.EqualTo("new"));
            Assert.That(observer.State.Error, Is.Null);
        }

        [Test]
        public void InvalidationDuringRetryStartsANewGenerationNextCycle()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("retry-invalidation");
            var firstFailure = new TimeoutException("first");
            var second = new TaskCompletionSource<string>();
            int attempts = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    attempts++;
                    return attempts == 1
                        ? Task.FromException<string>(firstFailure)
                        : second.Task;
                },
                new QueryPolicy(
                    retry: RetryPolicy.Fixed(
                        2,
                        TimeSpan.FromHours(1))));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> original = observer.EnsureAsync();
            runtime.RunOneFrame();

            client.Invalidate(QueryFilter.Exact(key));

            Assert.That(original.IsFaulted, Is.True);
            Assert.That(original.Exception?.InnerException, Is.SameAs(firstFailure));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Idle));
            runtime.RunOneFrame();
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));
        }

        [Test]
        public void CancelingARefetchRestoresThePreviousErrorOutcome()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            var pending = new TaskCompletionSource<string>();
            int fetchCount = 0;
            var originalError = new InvalidOperationException("refresh failed");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return fetchCount == 1
                        ? Task.FromException<string>(originalError)
                        : pending.Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromMinutes(1)));
            using QueryObserver<string> observer = client.Watch(definition);

            Task<string> failedRefetch = observer.RefetchAsync();
            runtime.RunOneFrame();

            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Error));
            Assert.That(observer.State.Error, Is.SameAs(originalError));
            Assert.That(observer.State.Data, Is.EqualTo("cached"));
            Assert.That(failedRefetch.Exception?.InnerException, Is.SameAs(originalError));

            Task<string> canceledRefetch = observer.RefetchAsync();
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Error));
            Assert.That(observer.State.Error, Is.SameAs(originalError));
            observer.CancelSharedFetch();

            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Error));
            Assert.That(observer.State.Error, Is.SameAs(originalError));
            Assert.That(observer.State.Data, Is.EqualTo("cached"));
            Assert.That(canceledRefetch.IsCanceled, Is.True);
        }

        [Test]
        public async Task InvalidationDuringFetchDiscardsOldCacheWrite()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var firstSource = new TaskCompletionSource<string>();
            var secondSource = new TaskCompletionSource<string>();
            int fetchCount = 0;
            QueryKey<string> key = QueryKey.For<string>("profile");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => ++fetchCount == 1 ? firstSource.Task : secondSource.Task);
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> originalWait = observer.EnsureAsync();
            runtime.RunOneFrame();

            client.Invalidate(QueryFilter.Exact(key));
            firstSource.SetResult("obsolete");
            runtime.RunOneFrame();

            Assert.That(await originalWait, Is.EqualTo("obsolete"));
            Assert.That(observer.State.HasData, Is.False);

            runtime.RunOneFrame();
            Assert.That(fetchCount, Is.EqualTo(2));

            secondSource.SetResult("current");
            runtime.RunOneFrame();
            Assert.That(observer.State.Data, Is.EqualTo("current"));
        }

        [Test]
        public void ObsoleteFailureDoesNotCorruptNewerCacheMetadata()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "old");
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => source.Task,
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> obsolete = observer.RefetchAsync();

            client.SetData(key, "new");
            source.SetException(new InvalidOperationException("obsolete"));
            runtime.RunOneFrame();

            Assert.That(obsolete.IsFaulted, Is.True);
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
            Assert.That(observer.State.Data, Is.EqualTo("new"));
            Assert.That(observer.State.Error, Is.Null);
            Assert.That(observer.State.FailureCount, Is.Zero);
        }

        [Test]
        public void FreshnessIsObserverSpecificWhileFetchingIsShared()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            var source = new TaskCompletionSource<string>();
            int fetchCount = 0;
            var longDefinition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return source.Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromMinutes(1)));
            var shortDefinition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return source.Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromSeconds(10)));
            using QueryObserver<string> longObserver = client.Watch(longDefinition);
            using QueryObserver<string> shortObserver = client.Watch(shortDefinition);

            runtime.RunOneFrame();
            runtime.AdvanceBy(TimeSpan.FromSeconds(11));

            Assert.That(fetchCount, Is.Zero);
            Assert.That(shortObserver.State.IsStale, Is.True);
            Assert.That(longObserver.State.IsStale, Is.False);

            runtime.SetFocused(false);
            runtime.SetFocused(true);

            Assert.That(fetchCount, Is.EqualTo(1));
            Assert.That(longObserver.State.FetchActivity, Is.EqualTo(FetchActivity.Fetching));
        }

        [Test]
        public void PollingPausesWhileUnfocusedAndRestartsFromFocus()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            int fetchCount = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return new TaskCompletionSource<string>().Task;
                },
                new QueryPolicy(
                    staleAfter: TimeSpan.FromHours(1),
                    pollEvery: TimeSpan.FromSeconds(5)));
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();

            runtime.SetFocused(false);
            runtime.AdvanceBy(TimeSpan.FromSeconds(10));
            Assert.That(fetchCount, Is.Zero);

            runtime.SetFocused(true);
            runtime.AdvanceBy(TimeSpan.FromSeconds(4));
            Assert.That(fetchCount, Is.Zero);

            runtime.AdvanceBy(TimeSpan.FromSeconds(1));
            Assert.That(fetchCount, Is.EqualTo(1));
        }

        [Test]
        public void UnusedEntryIsEvictedAtItsDeadline()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            int fetchCount = 0;
            QueryKey<string> key = QueryKey.For<string>("profile");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return Task.FromResult("value-" + fetchCount);
                },
                new QueryPolicy(
                    staleAfter: TimeSpan.FromHours(1),
                    unusedFor: TimeSpan.FromSeconds(5)));
            QueryObserver<string> first = client.Watch(definition);
            runtime.RunOneFrame();
            runtime.RunOneFrame();
            Assert.That(first.State.Data, Is.EqualTo("value-1"));
            first.Dispose();

            runtime.AdvanceBy(TimeSpan.FromSeconds(5));
            using QueryObserver<string> second = client.Watch(definition);
            Assert.That(second.State.Status, Is.EqualTo(QueryStatus.Empty));

            runtime.RunOneFrame();
            runtime.RunOneFrame();
            Assert.That(second.State.Data, Is.EqualTo("value-2"));
        }

        [Test]
        public async Task SetDataOnlyEntryUsesDefaultUnusedRetention()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            int fetchCount = 0;
            client.SetData(key, "cached");

            runtime.AdvanceBy(TimeSpan.FromMinutes(5));
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return Task.FromResult("fetched");
                },
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            Task<string> result = client.EnsureAsync(definition);
            runtime.RunOneFrame();

            Assert.That(await result, Is.EqualTo("fetched"));
            Assert.That(fetchCount, Is.EqualTo(1));
        }

        [Test]
        public void RemovingObservedDataReturnsToEmptyAndEnsuresAgain()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            client.SetData(key, "cached");
            int fetchCount = 0;
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    fetchCount++;
                    return source.Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();

            client.Remove(QueryFilter.Exact(key));

            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Empty));
            Assert.That(observer.State.HasData, Is.False);

            runtime.RunOneFrame();
            Assert.That(fetchCount, Is.EqualTo(1));
        }

        [Test]
        public void PrefixInvalidationMatchesOnlyTheStructuralPrefix()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> first = QueryKey.For<string>(
                "player",
                QueryKeyPart.Text("42"),
                QueryKeyPart.Text("profile"));
            QueryKey<string> second = QueryKey.For<string>(
                "player",
                QueryKeyPart.Text("42"),
                QueryKeyPart.Text("wallet"));
            QueryKey<string> unrelated = QueryKey.For<string>(
                "player",
                QueryKeyPart.Text("7"),
                QueryKeyPart.Text("profile"));
            client.SetData(first, "first");
            client.SetData(second, "second");
            client.SetData(unrelated, "unrelated");
            var policy = new QueryPolicy(staleAfter: TimeSpan.FromHours(1));
            using QueryObserver<string> firstObserver = client.Watch(
                new QueryDefinition<string>(first, _ => Task.FromResult(""), policy));
            using QueryObserver<string> secondObserver = client.Watch(
                new QueryDefinition<string>(second, _ => Task.FromResult(""), policy));
            using QueryObserver<string> unrelatedObserver = client.Watch(
                new QueryDefinition<string>(unrelated, _ => Task.FromResult(""), policy));
            runtime.RunOneFrame();

            int count = client.Invalidate(QueryFilter.Prefix(
                "player",
                QueryKeyPart.Text("42")));

            Assert.That(count, Is.EqualTo(2));
            Assert.That(firstObserver.State.IsStale, Is.True);
            Assert.That(secondObserver.State.IsStale, Is.True);
            Assert.That(unrelatedObserver.State.IsStale, Is.False);
        }

        [Test]
        public void ObserverCallbacksAreNotReentrant()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            int depth = 0;
            int maximumDepth = 0;
            int successCalls = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition, state =>
            {
                depth++;
                maximumDepth = Math.Max(maximumDepth, depth);
                if (state.Status == QueryStatus.Success && successCalls++ == 0)
                {
                    client.SetData(key, "second");
                    runtime.RunOneFrame();
                }

                depth--;
            });

            client.SetData(key, "first");
            runtime.RunOneFrame();
            runtime.RunOneFrame();

            Assert.That(maximumDepth, Is.EqualTo(1));
            Assert.That(successCalls, Is.EqualTo(2));
            Assert.That(observer.State.Data, Is.EqualTo("second"));
        }

        [Test]
        public void ThrowingObserverDoesNotBlockOtherObservers()
        {
            var callbackErrors = new List<Exception>();
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient(
                new QueryClientOptions(unhandledException: callbackErrors.Add));
            QueryKey<string> key = QueryKey.For<string>("profile");
            int successfulObserverCalls = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition, state =>
            {
                if (state.HasData)
                {
                    throw new InvalidOperationException("observer");
                }
            });
            using IDisposable subscription = observer.Subscribe(state =>
            {
                if (state.HasData)
                {
                    successfulObserverCalls++;
                }
            });

            client.SetData(key, "value");
            runtime.RunOneFrame();

            Assert.That(successfulObserverCalls, Is.EqualTo(1));
            Assert.That(callbackErrors, Has.Count.EqualTo(1));
        }

        [Test]
        public void ListenerCanUnsubscribeWithoutSkippingTheNextListener()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            IDisposable firstSubscription = null;
            int secondCalls = 0;
            firstSubscription = observer.Subscribe(state =>
            {
                if (state.HasData)
                {
                    firstSubscription.Dispose();
                }
            });
            using IDisposable secondSubscription = observer.Subscribe(state =>
            {
                if (state.HasData)
                {
                    secondCalls++;
                }
            });

            client.SetData(key, "value");
            runtime.RunOneFrame();

            Assert.That(secondCalls, Is.EqualTo(1));
        }

        [Test]
        public void QueryObserversAreNotifiedInRegistrationOrder()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            var order = new List<int>();
            using QueryObserver<string> first = client.Watch(definition, state =>
            {
                if (state.HasData)
                {
                    order.Add(1);
                }
            });
            using QueryObserver<string> second = client.Watch(definition, state =>
            {
                if (state.HasData)
                {
                    order.Add(2);
                }
            });

            client.SetData(key, "value");
            runtime.RunOneFrame();

            Assert.That(order, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void SubscribeDuringDispatchDoesNotInvokeNestedCallback()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(staleAfter: TimeSpan.FromHours(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            int depth = 0;
            int maximumDepth = 0;
            int nestedCalls = 0;
            IDisposable nested = null;
            using IDisposable first = observer.Subscribe(state =>
            {
                if (!state.HasData || nested != null)
                {
                    return;
                }

                depth++;
                maximumDepth = Math.Max(maximumDepth, depth);
                nested = observer.Subscribe(nestedState =>
                {
                    depth++;
                    maximumDepth = Math.Max(maximumDepth, depth);
                    nestedCalls++;
                    depth--;
                });
                depth--;
            });

            client.SetData(key, "value");
            runtime.RunOneFrame();

            Assert.That(maximumDepth, Is.EqualTo(1));
            Assert.That(nestedCalls, Is.EqualTo(1));
            nested.Dispose();
        }

        [Test]
        public void DisposedObserverIsNotRetainedByLongDeadlines()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            WeakReference observer = CreateDisposedObserver(runtime, client);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(observer.IsAlive, Is.False);
        }

        [Test]
        public void CanceledGenerationDoesNotRetainDisposedObserver()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<string>();
            WeakReference observer = CreateCanceledFetchingObserver(
                runtime,
                client,
                source);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.That(observer.IsAlive, Is.False);
            GC.KeepAlive(source);
        }

        [Test]
        public void SharedCancellationMayReenterCacheOperations()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> activeKey = QueryKey.For<string>("active");
            QueryKey<string> callbackKey = QueryKey.For<string>("callback");
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                activeKey,
                cancellationToken =>
                {
                    cancellationToken.Register(
                        () => client.SetData(callbackKey, "written"));
                    return source.Task;
                });
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();

            Assert.DoesNotThrow(() => client.Cancel(QueryFilter.All));
            Assert.That(
                client.UpdateData(callbackKey, value => value + "!"),
                Is.True);
        }

        [Test]
        public void ThrowingCancellationCallbackDoesNotCorruptSharedState()
        {
            var callbackErrors = new List<Exception>();
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient(
                new QueryClientOptions(unhandledException: callbackErrors.Add));
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("throwing-cancellation"),
                cancellationToken =>
                {
                    cancellationToken.Register(
                        () => throw new InvalidOperationException("cancel callback"));
                    return source.Task;
                });
            Task<string> result = client.EnsureAsync(definition);

            Assert.DoesNotThrow(() => client.Cancel(QueryFilter.All));
            runtime.RunOneFrame();

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(callbackErrors, Has.Count.EqualTo(1));
        }

        [Test]
        public void FaultedOperationCanceledExceptionIsStillCancellation()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var cancellation = new OperationCanceledException();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("faulted-cancellation"),
                cancellationToken => Task.FromException<string>(cancellation));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.EnsureAsync();

            runtime.RunOneFrame();

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Empty));
            Assert.That(observer.State.Error, Is.Null);
        }

        [Test]
        public void SynchronousOperationCanceledExceptionIsNotRetried()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            int attempts = 0;
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("synchronous-cancellation"),
                cancellationToken =>
                {
                    attempts++;
                    throw new OperationCanceledException();
                },
                new QueryPolicy(
                    retry: RetryPolicy.Fixed(
                        3,
                        TimeSpan.FromSeconds(1))));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.EnsureAsync();

            runtime.RunOneFrame();

            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(result.IsCanceled, Is.True);
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Empty));
            Assert.That(observer.State.Error, Is.Null);
        }

        [Test]
        public void ThrowingRetryPredicateTerminatesTheGeneration()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var predicateError = new InvalidOperationException("predicate");
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("retry-predicate"),
                cancellationToken =>
                    Task.FromException<string>(new TimeoutException()),
                new QueryPolicy(
                    retry: RetryPolicy.Fixed(
                        2,
                        TimeSpan.FromSeconds(1),
                        exception => throw predicateError)));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.EnsureAsync();

            runtime.RunOneFrame();

            Assert.That(result.IsFaulted, Is.True);
            Assert.That(result.Exception?.InnerException, Is.SameAs(predicateError));
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Error));
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Idle));
        }

        [Test]
        public void RetryPredicateCacheWriteSupersedesTheFailedGeneration()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("retry-reentrancy");
            client.SetData(key, "old");
            int attempts = 0;
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken =>
                {
                    attempts++;
                    return Task.FromException<string>(new TimeoutException());
                },
                new QueryPolicy(
                    staleAfter: TimeSpan.FromHours(1),
                    retry: RetryPolicy.Fixed(
                        2,
                        TimeSpan.FromSeconds(1),
                        exception =>
                        {
                            client.SetData(key, "new");
                            return true;
                        })));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.RefetchAsync();

            runtime.RunOneFrame();
            runtime.AdvanceBy(TimeSpan.FromSeconds(1));

            Assert.That(result.IsFaulted, Is.True);
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
            Assert.That(observer.State.Data, Is.EqualTo("new"));
            Assert.That(observer.State.Error, Is.Null);
            Assert.That(observer.State.FailureCount, Is.Zero);
            Assert.That(observer.State.FetchActivity, Is.EqualTo(FetchActivity.Idle));
        }

        [Test]
        public void MaximumDurationsDoNotOverflowDeadlines()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            runtime.AdvanceBy(TimeSpan.FromSeconds(1));
            QueryKey<string> key = QueryKey.For<string>("maximum-duration");
            client.SetData(key, "value");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(
                    staleAfter: TimeSpan.MaxValue,
                    unusedFor: TimeSpan.MaxValue,
                    pollEvery: TimeSpan.MaxValue));

            Assert.DoesNotThrow(() =>
            {
                using QueryObserver<string> observer = client.Watch(definition);
            });
        }

        [Test]
        public void MaximumRetryDelaySaturatesWithoutOverflow()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            runtime.AdvanceBy(TimeSpan.FromSeconds(1));
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("maximum-retry"),
                cancellationToken =>
                    Task.FromException<string>(new TimeoutException()),
                new QueryPolicy(
                    retry: RetryPolicy.Fixed(2, TimeSpan.MaxValue)));
            using QueryObserver<string> observer = client.Watch(definition);
            Task<string> result = observer.EnsureAsync();

            Assert.DoesNotThrow(runtime.RunOneFrame);
            Assert.That(result.IsCompleted, Is.False);
            Assert.That(
                observer.State.FetchActivity,
                Is.EqualTo(FetchActivity.RetryDelay));
        }

        [Test]
        public void ExponentialRetryDelaySaturatesForLargeAttemptCounts()
        {
            RetryPolicy retry = RetryPolicy.Exponential(
                int.MaxValue,
                TimeSpan.FromTicks(1));

            Assert.That(
                retry.GetDelay(int.MaxValue),
                Is.EqualTo(TimeSpan.MaxValue));
        }

        [Test]
        public void DefaultFilterIsRejectedEvenWhenCacheIsEmpty()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();

            Assert.Throws<InvalidOperationException>(
                () => client.Invalidate(default(QueryFilter)));
            Assert.Throws<InvalidOperationException>(
                () => client.Cancel(default(QueryFilter)));
            Assert.Throws<InvalidOperationException>(
                () => client.Remove(default(QueryFilter)));
        }

        [Test]
        public void FetchDelegateMayCancelItsOwnSharedGeneration()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("self-cancel"),
                cancellationToken =>
                {
                    client.Cancel(QueryFilter.All);
                    return new TaskCompletionSource<string>().Task;
                });

            Task<string> result = client.EnsureAsync(definition);

            Assert.That(result.IsCanceled, Is.True);
        }

        [Test]
        public void FocusRefreshMayReenterCacheOperations()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> activeKey = QueryKey.For<string>("active");
            QueryKey<string> callbackKey = QueryKey.For<string>("callback");
            client.SetData(activeKey, "cached");
            var definition = new QueryDefinition<string>(
                activeKey,
                cancellationToken =>
                {
                    client.SetData(callbackKey, "written");
                    return new TaskCompletionSource<string>().Task;
                },
                new QueryPolicy(staleAfter: TimeSpan.FromSeconds(1)));
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();
            runtime.AdvanceBy(TimeSpan.FromSeconds(2));
            runtime.SetFocused(false);

            Assert.DoesNotThrow(() => runtime.SetFocused(true));
            Assert.That(
                client.UpdateData(callbackKey, value => value + "!"),
                Is.True);
        }

        [Test]
        public void PublicOperationsRejectAnotherThread()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            QueryKey<string> key = QueryKey.For<string>("profile");
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    client.SetData(key, "value");
                }
                catch (Exception exception)
                {
                    error = exception;
                }
            });

            thread.Start();
            thread.Join();

            Assert.That(error, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OffThreadTaskCompletionIsAppliedOnTheOwnerRuntime()
        {
            using var runtime = new ManualQueryRuntime();
            using QueryClient client = runtime.CreateClient();
            var source = new TaskCompletionSource<string>();
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("profile"),
                cancellationToken => source.Task);
            using QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();
            var thread = new Thread(() => source.SetResult("value"));

            thread.Start();
            thread.Join();

            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Empty));
            runtime.RunOneFrame();
            Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Success));
            Assert.That(observer.State.Data, Is.EqualTo("value"));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateDisposedObserver(
            ManualQueryRuntime runtime,
            QueryClient client)
        {
            QueryKey<string> key = QueryKey.For<string>("deadline-retention");
            client.SetData(key, "value");
            var definition = new QueryDefinition<string>(
                key,
                cancellationToken => Task.FromResult("unused"),
                new QueryPolicy(
                    staleAfter: TimeSpan.FromDays(1),
                    pollEvery: TimeSpan.FromDays(1)));
            QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();
            var reference = new WeakReference(observer);
            observer.Dispose();
            return reference;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateCanceledFetchingObserver(
            ManualQueryRuntime runtime,
            QueryClient client,
            TaskCompletionSource<string> source)
        {
            var definition = new QueryDefinition<string>(
                QueryKey.For<string>("fetch-retention"),
                cancellationToken => source.Task);
            QueryObserver<string> observer = client.Watch(definition);
            runtime.RunOneFrame();
            var reference = new WeakReference(observer);
            client.Cancel(QueryFilter.All);
            observer.Dispose();
            runtime.RunOneFrame();
            return reference;
        }
    }
}
