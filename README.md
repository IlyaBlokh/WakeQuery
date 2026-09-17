# WakeQuery

WakeQuery is a transport-neutral server-state cache and query lifecycle engine for Unity 2022.3 and newer.

It provides the core lifecycle ideas behind tools such as TanStack Query—structural cache keys, request deduplication, freshness, retries, polling, invalidation, cache retention, observation, and mutation state—without choosing your HTTP client, serializer, dependency-injection container, or UI framework.

WakeQuery has no runtime package dependencies. Fetch and mutation operations are ordinary cancellation-aware `Task<T>` delegates.

## Platform support

- Unity 2022.3+
- Desktop, iOS, Android, and WebGL
- Mono and IL2CPP
- .NET Standard 2.1

The core assembly has no `UnityEngine` reference. The included Unity adapter uses an explicit process-level PlayerLoop callback: no `MonoBehaviour`, `GameObject`, scene object, prefab, or `DontDestroyOnLoad` object is created.

## Installation

For local development, open Unity's Package Manager, choose **Add package from disk**, and select this repository's `package.json`.

You can also add a local path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wakequery.core": "file:../../WakeQuery"
  }
}
```

## Create and own a client

Create the client in your application's composition root and dispose it when that application scope ends:

```csharp
using WakeQuery;
using WakeQuery.Unity;

QueryClient queryClient = UnityQueryRuntime.CreateClient();

// At application-scope shutdown:
queryClient.Dispose();
```

The first client installs one WakeQuery marker into Unity's current PlayerLoop. Additional clients share the runtime but keep independent caches. Disposing the final client removes the marker.

## Define and observe a query

Keep definitions in centralized factories so every consumer of a structural key describes the same resource:

```csharp
QueryKey<PlayerProfile> key = QueryKey.For<PlayerProfile>(
    "player-profile",
    QueryKeyPart.Text(playerId));

var definition = new QueryDefinition<PlayerProfile>(
    key,
    cancellationToken => api.GetProfileAsync(playerId, cancellationToken),
    new QueryPolicy(staleAfter: TimeSpan.FromSeconds(30)));

QueryObserver<PlayerProfile> observer =
    queryClient.Watch(definition, RenderProfile);
```

`Watch` emits the current snapshot synchronously, then schedules its first ensure for the next runtime cycle. Dispose the observer to release its interest:

```csharp
observer.Dispose();
```

`QueryState<T>` keeps the cached outcome separate from current activity. A background refresh can therefore expose `Status == QueryStatus.Success`, `HasData == true`, and `FetchActivity == FetchActivity.Fetching` at the same time.

## Imperative fetching and cancellation

```csharp
PlayerProfile cachedOrFetched =
    await queryClient.EnsureAsync(definition, cancellationToken);

PlayerProfile refreshed =
    await queryClient.RefetchAsync(definition, cancellationToken);
```

`EnsureAsync` returns fresh cached data or joins/starts a fetch. `RefetchAsync` ignores freshness, but still joins an active fetch for the key.

Canceling a caller token cancels only that caller's wait. It does not cancel work still needed by another waiter or observer. To explicitly cancel shared work, use `observer.CancelSharedFetch()` or `queryClient.Cancel(filter)`. Disposing the final interested observer also cancels work when no imperative waiter remains.

## Cache operations

```csharp
queryClient.SetData(profileKey, profile);
queryClient.UpdateData(profileKey, current => current.WithName("Ada"));

queryClient.Invalidate(QueryFilter.Exact(profileKey));
queryClient.Invalidate(QueryFilter.Prefix(
    "player",
    QueryKeyPart.Text(playerId)));

queryClient.Cancel(QueryFilter.All);
queryClient.Remove(QueryFilter.All);
```

Invalidation keeps data, marks it stale, and refreshes observed matches. Removal cancels and clears matches. An observed removed query returns to `Empty` and ensures again on a later runtime cycle.

Keys accept only string, signed integer, unsigned integer, Boolean, and `Guid` parts. Result type is not part of structural identity: reusing one structural key for two result types throws `QueryTypeMismatchException`.

## Retry and polling

Retries are disabled by default and never use `Task.Delay`:

```csharp
var policy = new QueryPolicy(
    staleAfter: TimeSpan.FromMinutes(1),
    unusedFor: TimeSpan.FromMinutes(5),
    retry: RetryPolicy.Exponential(
        maxAttempts: 3,
        initialDelay: TimeSpan.FromSeconds(1),
        maximumDelay: TimeSpan.FromSeconds(8),
        shouldRetry: exception => exception is TimeoutException),
    pollEvery: TimeSpan.FromSeconds(30));
```

Polling runs only while its observer is active and the application is focused. It never overlaps an active fetch. Call `UnityQueryRuntime.NotifyReconnected()` when your own connectivity layer confirms a reconnect.

## Mutations

Mutation executions are independent, are not deduplicated, and are never retried automatically:

```csharp
Mutation<RenameRequest, PlayerProfile> rename =
    queryClient.CreateMutation(
        new MutationDefinition<RenameRequest, PlayerProfile>(
            (request, cancellationToken) =>
                api.RenameAsync(request, cancellationToken),
            success =>
            {
                success.Set(profileKey, success.Output);
                success.Invalidate(QueryFilter.Prefix("leaderboard"));
            }));

PlayerProfile updated = await rename.ExecuteAsync(request, cancellationToken);
```

Success effects are staged, published as one observer-notification batch, and run before `ExecuteAsync` completes. If the remote operation succeeds but a local effect fails, the task throws `MutationEffectException`.

Dispose mutations when their application scope ends. Disposing the owning client also cancels every active mutation.

## Testing without Unity objects

Use `ManualQueryRuntime` for deterministic time and lifecycle tests:

```csharp
using var runtime = new ManualQueryRuntime();
using QueryClient client = runtime.CreateClient();

runtime.RunOneFrame();
runtime.AdvanceBy(TimeSpan.FromSeconds(30));
runtime.SetFocused(false);
runtime.SetFocused(true);
runtime.NotifyReconnected();
```

Task delegates can complete on any thread. Cache state, effects, and observer notifications are applied on the client owner's next runtime cycle. All public client and observer operations are owner-thread-affine.

## Scope

WakeQuery v1 is intentionally in-memory and framework-neutral. HTTP, serialization, authentication, persistence/hydration, UI bindings, reactive adapters, optimistic rollback, offline mutation queues, mutation retry, dependent queries, and arbitrary cache predicates are not included.

See the importable **Basic Usage** sample for more detail.

## License

MIT. See [LICENSE.md](LICENSE.md).
