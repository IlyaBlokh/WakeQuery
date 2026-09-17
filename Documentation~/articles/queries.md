# Queries and keys

## Structural keys

A <xref:WakeQuery.QueryKey%601> is a scope plus ordered parts. Create keys with <xref:WakeQuery.QueryKey.For*> and parts with the <xref:WakeQuery.QueryKeyPart> factory methods:

```csharp
QueryKey<PlayerProfile> profile =
    QueryKey.For<PlayerProfile>("player", QueryKeyPart.Text(playerId));

QueryKey<Inventory> inventory =
    QueryKey.For<Inventory>("player", QueryKeyPart.Text(playerId), QueryKeyPart.Text("inventory"));
```

Keys are compared by value. Parts accept only strings, signed integers, unsigned integers, Booleans, and `Guid` values, so equality is always stable.

The result type is **not** part of a key's identity. Reusing one structural key for two result types throws <xref:WakeQuery.QueryTypeMismatchException>.

Because keys are hierarchical, a prefix filter can target a whole group. For example, `QueryFilter.Prefix("player", QueryKeyPart.Text(playerId))` matches both keys above. See [Cache operations](cache-operations.md).

## Freshness

<xref:WakeQuery.QueryPolicy.StaleAfter> sets how long data stays fresh after it is written. The default is zero, so data is stale immediately and every new observer triggers a background refresh.

Freshness is evaluated **per observer**. Two observers of the same key with different policies can disagree about `IsStale`, and each refetches according to its own policy.

## Deduplication

Each key has at most one fetch running at a time. Observers and callers that need the same key while a fetch is running join it instead of starting another.

## Imperative fetching

Use the task-based methods when you need a value in async code:

```csharp
PlayerProfile cachedOrFetched = await queryClient.EnsureAsync(definition, cancellationToken);
PlayerProfile refreshed = await queryClient.RefetchAsync(definition, cancellationToken);
```

- <xref:WakeQuery.QueryClient.EnsureAsync*> returns fresh cached data, or joins or starts a fetch.
- <xref:WakeQuery.QueryClient.RefetchAsync*> ignores freshness, but still joins a fetch that is already running.

<xref:WakeQuery.QueryObserver%601> has the same two methods, which use that observer's policy.

## Cancellation

A caller's cancellation token cancels only that caller's wait. It does not cancel work that another caller or observer still needs.

To cancel the shared fetch itself, use <xref:WakeQuery.QueryObserver%601.CancelSharedFetch> or <xref:WakeQuery.QueryClient.Cancel(WakeQuery.QueryFilter)>. Disposing the last interested observer also cancels the fetch when no caller is still waiting on it.

## Retention

A query with no observers is kept for <xref:WakeQuery.QueryPolicy.UnusedFor> (5 minutes by default) and then evicted. Watching it again before that cancels the eviction. When several definitions share a key, the longest `UnusedFor` wins.

## Automatic refetching

While observed, a stale query is refetched when:

- the application regains focus (<xref:WakeQuery.QueryPolicy.RefetchOnFocus>);
- your code reports a reconnect (<xref:WakeQuery.QueryPolicy.RefetchOnReconnect>) with <xref:WakeQuery.Unity.UnityQueryRuntime.NotifyReconnected>;
- it is invalidated.

Polling is covered in [Retry and polling](retry-and-polling.md).
