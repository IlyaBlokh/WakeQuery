# Cache operations

<xref:WakeQuery.QueryClient> lets you write to the cache and act on groups of queries selected by a <xref:WakeQuery.QueryFilter>.

## Writing data

```csharp
queryClient.SetData(profileKey, profile);
bool updated = queryClient.UpdateData(profileKey, current => current.WithName("Ada"));
```

- <xref:WakeQuery.QueryClient.SetData*> stores data as a fresh, successful result, creating the entry if needed.
- <xref:WakeQuery.QueryClient.UpdateData*> computes the new value from the cached one. It returns `false`, without calling your function, when the key has no data.

Both notify observers. An entry written this way with no observers is evicted after its retention period.

## Filters

| Filter | Matches |
| --- | --- |
| `QueryFilter.All` | Every cached query. |
| `QueryFilter.Exact(key)` | Only `key`. |
| `QueryFilter.Prefix("player", QueryKeyPart.Text(id))` | Every `player` key whose first part is `id`, including longer keys. |

A `default(QueryFilter)` is invalid and makes the operations below throw.

## Invalidate, cancel, remove

```csharp
queryClient.Invalidate(QueryFilter.Exact(profileKey));
queryClient.Invalidate(QueryFilter.Prefix("player", QueryKeyPart.Text(playerId)));

queryClient.Cancel(QueryFilter.All);
queryClient.Remove(QueryFilter.All);
```

| Operation | Data | Running fetch | Observed queries |
| --- | --- | --- | --- |
| <xref:WakeQuery.QueryClient.Invalidate(WakeQuery.QueryFilter)> | Kept, marked stale | Finishes for its callers, but its result is not cached | Refetched (after the running fetch finishes) |
| <xref:WakeQuery.QueryClient.Cancel(WakeQuery.QueryFilter)> | Kept | Canceled for all waiters | Not refetched |
| <xref:WakeQuery.QueryClient.Remove(WakeQuery.QueryFilter)> | Cleared | Canceled | Return to `Empty` and fetch again on a later frame |

Each method returns the number of queries it affected. `Remove` deletes unobserved entries entirely.

The same rule applies to `SetData` and `UpdateData`: a fetch that started before the write cannot overwrite the newer data.

## Diagnostics

Pass an <xref:WakeQuery.IQueryDiagnosticListener> in <xref:WakeQuery.QueryClientOptions> to receive a <xref:WakeQuery.QueryDiagnosticEvent> for fetch starts, joins, retries, successes, failures, cancellations, invalidations, removals, evictions, and mutation executions:

```csharp
sealed class LogDiagnostics : IQueryDiagnosticListener
{
    public void OnEvent(QueryDiagnosticEvent e) =>
        Debug.Log($"[WakeQuery] {e.Kind} {e.Key} #{e.ExecutionId} failures={e.FailureCount}");
}

QueryClient client = UnityQueryRuntime.CreateClient(
    new QueryClientOptions(diagnosticListener: new LogDiagnostics()));
```

Exceptions thrown by listeners never break the client. They go to `QueryClientOptions.UnhandledException`, which defaults to `Debug.LogException` in Unity.
