---
_layout: landing
---

# WakeQuery

WakeQuery is a transport-neutral server-state cache and query lifecycle engine for Unity 2022.3 and newer.

It brings the lifecycle ideas behind tools such as TanStack Query to Unity: structural cache keys, request deduplication, freshness, retries, polling, invalidation, cache retention, observation, and mutation state. It does not choose your HTTP client, serializer, dependency-injection container, or UI framework. Fetch and mutation operations are ordinary cancellation-aware `Task<T>` delegates.

## Platform support

- Unity 2022.3+
- Desktop, iOS, Android, and WebGL
- Mono and IL2CPP
- .NET Standard 2.1

The core assembly has no `UnityEngine` reference and no runtime package dependencies. The Unity adapter uses a PlayerLoop callback: it creates no `MonoBehaviour`, `GameObject`, or scene object.

## Installation

In Unity, open **Window → Package Manager**, choose **+ → Add package from git URL**, and enter:

```text
https://github.com/IlyaBlokh/WakeQuery.git
```

To use a local clone instead, choose **Add package from disk** and select the repository's `package.json`, or add a path to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.wakequery.core": "file:../../WakeQuery"
  }
}
```

## Where to go next

- [Getting started](articles/getting-started.md): create a client and observe your first query.
- [Guides](articles/queries.md): keys, cache operations, retries, polling, mutations, and testing.
- [API reference](xref:WakeQuery): every public type and member.

## Scope

WakeQuery is intentionally in-memory and framework-neutral. HTTP, serialization, authentication, persistence, UI bindings, reactive adapters, optimistic rollback, offline mutation queues, mutation retry, dependent queries, and arbitrary cache predicates are not included.
