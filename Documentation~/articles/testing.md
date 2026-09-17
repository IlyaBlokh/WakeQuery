# Testing

<xref:WakeQuery.Testing.ManualQueryRuntime> runs WakeQuery without Unity objects. You control time, frames, focus, and reconnects, so lifecycle tests are fast and deterministic. It works in Unity EditMode tests and in plain .NET test projects.

```csharp
using WakeQuery;
using WakeQuery.Testing;

using var runtime = new ManualQueryRuntime();
using QueryClient client = runtime.CreateClient();

runtime.RunOneFrame();
runtime.AdvanceBy(TimeSpan.FromSeconds(30));
runtime.SetFocused(false);
runtime.SetFocused(true);
runtime.NotifyReconnected();
```

## Driving the runtime

| Method | Effect |
| --- | --- |
| <xref:WakeQuery.Testing.ManualQueryRuntime.RunOneFrame> | Applies completed work, runs due timers, and sends notifications. Time does not move. |
| <xref:WakeQuery.Testing.ManualQueryRuntime.AdvanceBy(System.TimeSpan)> | Moves the clock forward, then runs one frame. |
| <xref:WakeQuery.Testing.ManualQueryRuntime.SetFocused(System.Boolean)> | Simulates focus changes. Clients start focused. |
| <xref:WakeQuery.Testing.ManualQueryRuntime.NotifyReconnected> | Simulates a network reconnect. |

Nothing happens between calls, so every step of a test is explicit.

## Example

```csharp
[Test]
public void InvalidationRefetchesObservedQuery()
{
    using var runtime = new ManualQueryRuntime();
    using QueryClient client = runtime.CreateClient();
    int calls = 0;
    var definition = new QueryDefinition<int>(
        QueryKey.For<int>("counter"),
        _ => Task.FromResult(++calls));

    using QueryObserver<int> observer = client.Watch(definition);
    Assert.That(observer.State.Status, Is.EqualTo(QueryStatus.Empty));

    runtime.RunOneFrame(); // starts the fetch
    runtime.RunOneFrame(); // applies the result
    Assert.That(observer.State.Data, Is.EqualTo(1));

    client.Invalidate(QueryFilter.Exact(definition.Key));
    runtime.RunOneFrame(); // starts the refetch
    runtime.RunOneFrame(); // applies the result
    Assert.That(observer.State.Data, Is.EqualTo(2));
}
```

Use `TaskCompletionSource<T>` in fetch delegates to control exactly when a fetch completes or fails.

## Exceptions

By default, exceptions thrown by listeners are rethrown from `RunOneFrame`, so a broken listener fails the test. Pass `QueryClientOptions.UnhandledException` to collect them instead.

## Threading

The thread that creates the runtime owns it and every client it creates. Call all methods from that thread.
