# Changelog

All notable changes to WakeQuery are documented here.

## [Unreleased]

### Added

- XML documentation for every public API member, enforced by the .NET build.
- DocFX documentation site with guides and API reference, published to GitHub Pages.

## [0.1.0] - 2026-09-03

### Added

- Typed structural query keys and exact, prefix, and all-query filters.
- In-memory query cache with observer-specific freshness and polling.
- Shared request deduplication with caller-owned and shared cancellation.
- Fixed and exponential retry scheduling through a deadline heap.
- Invalidation, set, update, removal, and unused-entry garbage collection.
- Concurrent mutation state and staged success effects.
- Explicit, object-free Unity PlayerLoop runtime.
- Deterministic manual runtime, EditMode tests, PlayMode tests, and a framework-neutral sample.
