# Retry and polling

Both are configured on <xref:WakeQuery.QueryPolicy>:

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

## Retries

Retries are disabled by default (<xref:WakeQuery.RetryPolicy.None>). Choose a strategy with <xref:WakeQuery.RetryPolicy>:

| Factory | Delay before retry *n* |
| --- | --- |
| <xref:WakeQuery.RetryPolicy.Fixed*> | Always `delay`. |
| <xref:WakeQuery.RetryPolicy.Exponential*> | `initialDelay × 2ⁿ⁻¹`, capped at `maximumDelay`. |

- `maxAttempts` counts the first attempt, so `maxAttempts: 3` means up to two retries.
- `shouldRetry` filters which exceptions are retried. When omitted, every failure is retried.
- Delays run on WakeQuery's own clock and never use `Task.Delay`, so they pause with the PlayerLoop and are deterministic in tests.
- While waiting, the query reports `FetchActivity.RetryDelay`, and `Error` and `FailureCount` describe the last failed attempt.
- After the last attempt fails, `Status` becomes `Error`. Any previously cached data is kept.

Mutations are never retried.

## Polling

Set <xref:WakeQuery.QueryPolicy.PollEvery> to refetch on an interval. Polling:

- runs only while an observer with that policy is active;
- runs only while the application is focused, and pauses when focus is lost;
- never overlaps a running fetch: the next poll is scheduled after the current fetch settles.

## Focus and reconnect

When the application regains focus, observers whose policy has <xref:WakeQuery.QueryPolicy.RefetchOnFocus> refetch if their data is stale. Otherwise polling simply resumes.

WakeQuery does not monitor connectivity. When your own networking layer confirms a reconnect, call:

```csharp
UnityQueryRuntime.NotifyReconnected();
```

Observers whose policy has <xref:WakeQuery.QueryPolicy.RefetchOnReconnect> then refetch stale data.
