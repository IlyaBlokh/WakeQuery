# Getting started

This page walks through creating a client, defining a query, and observing it from Unity code.

## 1. Create and own a client

Create one <xref:WakeQuery.QueryClient> in your application's composition root with <xref:WakeQuery.Unity.UnityQueryRuntime>, and dispose it when that application scope ends:

```csharp
using WakeQuery;
using WakeQuery.Unity;

QueryClient queryClient = UnityQueryRuntime.CreateClient();

// At application-scope shutdown:
queryClient.Dispose();
```

The first client installs one WakeQuery callback into Unity's PlayerLoop. Additional clients share that callback but keep independent caches. Disposing the last client removes it.

All client, observer, and mutation methods must be called from the Unity main thread. Your fetch tasks may complete on any thread; WakeQuery applies their results on the main thread during the next frame.

## 2. Define a query

A <xref:WakeQuery.QueryDefinition%601> combines a key, a fetch delegate, and a <xref:WakeQuery.QueryPolicy>. Keep definitions in one central factory so every consumer of a key describes the same resource:

```csharp
public static class PlayerProfileQueries
{
    public static QueryKey<PlayerProfile> Key(string playerId) =>
        QueryKey.For<PlayerProfile>("player-profile", QueryKeyPart.Text(playerId));

    public static QueryDefinition<PlayerProfile> Profile(IPlayerProfileApi api, string playerId) =>
        new QueryDefinition<PlayerProfile>(
            Key(playerId),
            cancellationToken => api.GetAsync(playerId, cancellationToken),
            new QueryPolicy(staleAfter: TimeSpan.FromSeconds(30)));
}
```

## 3. Observe it

<xref:WakeQuery.QueryClient.Watch*> returns a <xref:WakeQuery.QueryObserver%601>. The listener receives the current snapshot immediately, then every change:

```csharp
QueryObserver<PlayerProfile> observer = queryClient.Watch(
    PlayerProfileQueries.Profile(api, playerId),
    RenderProfile);

void RenderProfile(QueryState<PlayerProfile> state)
{
    if (state.HasData)
    {
        nameLabel.text = state.Data.DisplayName;
    }

    spinner.SetActive(state.IsFetching);
    errorBanner.SetActive(state.Status == QueryStatus.Error);
}
```

Because the data is stale at first, the observer starts a fetch on the next frame. Dispose the observer when the UI that uses it goes away:

```csharp
observer.Dispose();
```

## 4. Read the state

<xref:WakeQuery.QueryState%601> keeps the cached outcome separate from current activity:

| Field | Meaning |
| --- | --- |
| `Status` | `Empty`, `Success`, or `Error`: the result of the latest completed fetch. |
| `FetchActivity` | `Idle`, `Fetching`, or `RetryDelay`: what the shared fetch is doing now. |
| `HasData` / `Data` | The cached value. Kept when a later fetch fails. |
| `IsStale` | Whether this observer considers the data out of date. |

A background refresh can therefore report `Status == Success`, `HasData == true`, and `FetchActivity == Fetching` at the same time.

## Next steps

- [Queries and keys](queries.md)
- [Cache operations](cache-operations.md)
- [Mutations](mutations.md)
- The importable **Basic Usage** sample in the Package Manager.
