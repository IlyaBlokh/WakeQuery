# WakeQuery

WakeQuery is a transport-neutral server-state cache and query lifecycle engine for Unity 2022.3 and newer.

It provides the core lifecycle ideas behind tools such as TanStack Query—structural cache keys, request deduplication, freshness, retries, polling, invalidation, cache retention, observation, and mutation state—without choosing your HTTP client, serializer, dependency-injection container, or UI framework.

WakeQuery has no runtime package dependencies. Fetch and mutation operations are ordinary cancellation-aware `Task<T>` delegates.

**📖 Documentation: https://ilyablokh.github.io/WakeQuery/**, with guides and the full API reference.

## Platform support

- Unity 2022.3+
- Desktop, iOS, Android, and WebGL
- Mono and IL2CPP
- .NET Standard 2.1

The core assembly has no `UnityEngine` reference. The included Unity adapter uses an explicit process-level PlayerLoop callback: no `MonoBehaviour`, `GameObject`, scene object, prefab, or `DontDestroyOnLoad` object is created.

## Installation

In Unity's Package Manager, choose **Add package from git URL** and enter:

```text
https://github.com/IlyaBlokh/WakeQuery.git
```

For local development, choose **Add package from disk** and select this repository's `package.json`, or add a local path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wakequery.core": "file:../../WakeQuery"
  }
}
```

## Quick start

```csharp
using WakeQuery;
using WakeQuery.Unity;

// Composition root: one client per application scope.
QueryClient queryClient = UnityQueryRuntime.CreateClient();

var profile = new QueryDefinition<PlayerProfile>(
    QueryKey.For<PlayerProfile>("player-profile", QueryKeyPart.Text(playerId)),
    cancellationToken => api.GetProfileAsync(playerId, cancellationToken),
    new QueryPolicy(staleAfter: TimeSpan.FromSeconds(30)));

// Emits the current snapshot now, then every change; fetches on the next frame.
QueryObserver<PlayerProfile> observer = queryClient.Watch(profile, RenderProfile);

// Later:
observer.Dispose();
queryClient.Dispose();
```

Continue with the guides:

- [Getting started](https://ilyablokh.github.io/WakeQuery/articles/getting-started.html)
- [Queries and keys](https://ilyablokh.github.io/WakeQuery/articles/queries.html)
- [Cache operations](https://ilyablokh.github.io/WakeQuery/articles/cache-operations.html)
- [Retry and polling](https://ilyablokh.github.io/WakeQuery/articles/retry-and-polling.html)
- [Mutations](https://ilyablokh.github.io/WakeQuery/articles/mutations.html)
- [Testing](https://ilyablokh.github.io/WakeQuery/articles/testing.html)

The importable **Basic Usage** sample in the Package Manager shows a complete query and mutation.

## Building the documentation

The API reference is generated from the XML comments in `Runtime/` with [DocFX](https://dotnet.github.io/docfx/). Every public member must be documented; `dotnet build` fails otherwise. To preview the site locally (requires the .NET 10 SDK):

```bash
dotnet tool restore
dotnet docfx Documentation~/docfx.json --serve
```

Then open http://localhost:8080. The `Docs` GitHub workflow builds the site on every pull request and publishes it to GitHub Pages on every push to `master`.

## Scope

WakeQuery v1 is intentionally in-memory and framework-neutral. HTTP, serialization, authentication, persistence/hydration, UI bindings, reactive adapters, optimistic rollback, offline mutation queues, mutation retry, dependent queries, and arbitrary cache predicates are not included.

## License

MIT. See [LICENSE.md](LICENSE.md).
