# Mutations

A <xref:WakeQuery.Mutation%602>, created from a <xref:WakeQuery.MutationDefinition%602> with <xref:WakeQuery.QueryClient.CreateMutation*>, runs a remote write and then updates the cache.

```csharp
Mutation<RenameRequest, PlayerProfile> rename = queryClient.CreateMutation(
    new MutationDefinition<RenameRequest, PlayerProfile>(
        (request, cancellationToken) => api.RenameAsync(request, cancellationToken),
        success =>
        {
            success.Set(profileKey, success.Output);
            success.Invalidate(QueryFilter.Prefix("leaderboard"));
        }));

PlayerProfile updated = await rename.ExecuteAsync(request, cancellationToken);
```

## Executions

Every <xref:WakeQuery.Mutation%602.ExecuteAsync*> call is an independent execution. Executions can overlap, are never deduplicated, and are never retried automatically.

## Success effects

The `onSuccess` callback receives a <xref:WakeQuery.MutationSuccess%602> with the execution's `Input` and `Output`. Its methods (`Set`, `Update`, `Invalidate`, `Remove`) **stage** cache changes instead of applying them immediately. After the callback returns:

1. The staged effects are applied in order.
2. Observers are notified in one batch.
3. The task returned by `ExecuteAsync` completes.

So when `await rename.ExecuteAsync(...)` returns, the cache already reflects the change.

Staging methods work only during the callback. Calling them later throws `InvalidOperationException`.

If the remote operation succeeds but a local effect fails, the task throws <xref:WakeQuery.MutationEffectException>. The remote change has already happened. The original error is in `InnerException`.

## State

<xref:WakeQuery.Mutation%602.State> and <xref:WakeQuery.Mutation%602.Subscribe*> expose a <xref:WakeQuery.MutationState%601>:

| Field | Meaning |
| --- | --- |
| `Status` | `Pending` while any execution runs; otherwise `Idle`, `Success`, or `Error`. |
| `PendingCount` | Number of running executions. |
| `HasData` / `Data` / `Error` | Result of the latest completed execution. |
| `LastCompletedExecutionId` | Which execution produced that result. |

"Latest" means the most recently **started** execution that has completed, so a slow older execution never overwrites a newer result. Canceled executions do not change the result fields.

## Cancellation and disposal

- Cancel a single execution with the token passed to `ExecuteAsync`.
- <xref:WakeQuery.Mutation%602.Cancel> requests cancellation of every running execution.

An execution whose operation completes after cancellation was requested is reported as canceled, even if the operation ignored the token and returned a result.

Dispose a mutation when its scope ends. Its running tasks complete as canceled. Disposing the owning client does the same for every mutation it created.
