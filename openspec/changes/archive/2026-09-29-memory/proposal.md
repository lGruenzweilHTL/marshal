## Why

Without memory, Marshal forgets every preference, project and fact between conversations, and the user has to repeat themselves. The corpus will outgrow any context window within months, so memory must be retrieved, not dumped. This change (sub-project 5 of 7) adds granum-inspired semantic memory ported to .NET, with automatic retrieval, project activation and a memory view.

## What Changes

- New project `Marshal.Memory` with typed chunks (`fact`, `preference`, `event`, `procedure`, `person`, `device`), deterministic ids, same-id version history (`ChunkVersion`), edges (`supersedes`, `contradicts`, `depends_on`, `relates_to`), soft deletes.
- Storage in SQLite: FTS5 for keywords, `sqlite-vec` for vectors; embeddings through `IEmbeddingGenerator` with a multilingual model; canonical chunk language English.
- Layers: pinned (always injected, UI-only pinning), general retrieved, project-scoped.
- Automatic hybrid retrieval in the context builder (no model round trip), two-phase (seeds → edge expansion), score = similarity × importance × freshness, per-type quotas, soft project scoping.
- Tools `memory.remember` (safe, `persists`, cannot pin) and `memory.recall` (safe), plus `memory.correct_project`.
- Project activation: explicit mentions and learned aliases, centroid matching on the turn embedding, confidence tiers, stickiness with decay, recency prior, corrections stored as `ProjectFeedback`.
- Session-close summarization into memory candidates (cheap model, dedupe by id, contradiction flags), reading only user turns and assistant replies.
- Memory audit (stale, conflicts, orphans) and NDJSON export on demand.
- Web UI memory view: force graph, PCA scatter, list, search, editor (incl. pin / unpin), history, audit, projects with aliases and correction log.

## Non-goals

- No nightly consolidation or scheduled export (need the scheduler; added by `background-work` as system tasks).
- Notes are not memory: no automatic import of notes (notes tools come in `integrations`).
- No git-branch scoping, no reuse of granum's Python code.
- No area-specific memory (the area is part of the turn embedding input only).
- No multi-user sharing of memory.

## Capabilities

### New Capabilities
- `memory-store`: chunk model, ids, versions, edges, soft delete, embeddings storage, export.
- `memory-retrieval`: automatic retrieval, hybrid search, scoring, quotas, pinned layer, `memory.recall`.
- `project-activation`: projects, aliases, centroid matching, confidence tiers, stickiness, corrections, transparency.
- `memory-consolidation`: `memory.remember`, session-close summarization, dedupe, contradiction handling, audit.
- `memory-view`: web UI memory views and API.

### Modified Capabilities
- None.

## Impact

- New code: `src/Marshal.Memory`, `tests/Marshal.Memory.Tests`, memory views in `web/`.
- New entities: `Chunk`, `ChunkVersion`, `ChunkEdge`, `Project`, `ProjectAlias`, `ProjectFeedback` (already in `erd.puml`).
- New native dependency: `sqlite-vec` loaded as SQLite extension; embedding model configured under `models` (`embedding` entry).
- Depends on `core-agent`, `auth-web-shell`, `extensibility` (templates for the summarizer).
