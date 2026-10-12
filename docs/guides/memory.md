# Memory

Teach Marshal facts and preferences, check what it remembers, and manage projects, pinning and the audit.

## Before you start

- A multilingual embedding model is configured, see [`embedding`](../configuration/marshal-yaml/embedding.md). Changing it re-embeds all memory at the next start.
- Concepts: [memory model](../concepts/memory-model.md).

## What Marshal remembers

Memory is a set of chunks. Each has a type (`fact`, `preference`, `event`, `procedure`, `person`, `device`), a title, content, an importance from 1 to 5, a scope (`general` or `project`) and a history of versions. Titles and content are written in English. The chunk id is a hash of owner, scope, project, type and normalized title, so writing the same title again updates the same chunk and keeps the previous content as a version. Chunks are never merged by similarity.

## Steps

### 1. Tell Marshal to remember something

```text
Remember that the Voron uses a 0.6 mm nozzle.
```

The agent calls `memory.remember` (type, title, content, optional importance, scope, project, sourceRef). In a clean run it writes without asking. In a tainted run (it read mail, RSS, notes, web content or event data) the write needs your confirmation with the reason `taint`, see [approvals](tasks/approvals.md). `memory.remember` cannot pin.

Marshal also learns by itself: when a session closes or a web chat session rests after `sessions.idleTtl`, a fast-tier summarizer proposes chunks from your turns and Marshal's replies only, never from raw tool results. A candidate that contradicts a stored chunk creates a `contradicts` edge and leaves the stored chunk unchanged. A nightly consolidation catches sessions the close path missed.

### 2. See what is used in a turn

Retrieval is automatic: for each turn Marshal embeds the text, searches by vector and keyword (FTS), expands along edges, replaces superseded chunks with their successors and scores each candidate as similarity x importance x freshness. Per-type quotas and a token budget apply. Pinned chunks are always injected in full, before the retrieved ones. Open **Runs**, choose a run and read its context step to see which chunks and projects were used.

For a deliberate deep search the agent calls `memory.recall` (query, optional type, project, limit) with a larger token budget.

### 3. Browse and edit in the web UI

**Memory** has these tabs:

| Tab | Use |
|---|---|
| Chunks | Search and filter, open the editor, view history (versions and supersedes chains), pin or unpin, delete (soft delete). |
| Projects | Descriptions, aliases, alias candidates and the correction log. |
| Map | Toggle between a force-directed graph of chunks and edges and a PCA scatter of embeddings. On a phone the map shows a list instead. |
| Audit | Stale chunks, open contradictions and orphans. |

Pinning is possible only here (or through the API); the agent cannot pin. Soft-deleted chunks are excluded from retrieval but stay in the history view. To replace a chunk with a changed identity, use supersede: it creates a new chunk with a `supersedes` edge.

### 4. Resolve the audit

The audit lists chunks not updated or retrieved for `memory.audit.staleAfterDays` (default 180), contradiction pairs (until one supersedes the other or is deleted) and orphans (chunks of deleted projects, dangling edges). Open each item and edit, supersede or delete.

### 5. Use projects

A project groups chunks (scope `project`). Marshal activates a project:

- By name or alias in the turn text, with no model call (reason `alias`).
- By similarity of the turn to the project's centroid (description plus top chunks, minus rejected phrases). High confidence activates stickily, medium for this turn only, low does nothing. Sticky activations decay each turn and drop on a topic shift.

Chunks of active projects are boosted and chunks of inactive projects are penalized, never excluded. If Marshal picks the wrong project, say so ("not that project"): the agent calls `memory.correct_project` with `rejected`, `switched` or `confirmed`. Every correction is stored with its phrase. Phrases that led to an uncorrected inference become alias candidates and are promoted to aliases after 3 confirmations (`memory.projects.aliasPromotion`). Active projects per turn and the reason are shown in the run detail, never spoken.

## Tuning

Defaults are starting points: retrieval token budget `memory.retrieval.tokenBudget` (600), per-type quotas and half-lives, project confidence thresholds (`highConfidence` 0.75, `mediumConfidence` 0.6), summarizer `memory.summarizer.enabled` and `maxTurns`. See [`agent`](../configuration/marshal-yaml/agent.md) and the [configuration index](../configuration/index.md) for where these keys are documented.

## Export and backup

The nightly backup writes an NDJSON export of each owner's chunks, versions and edges (without embeddings) next to the database copy, see [backups and restore](backups-and-restore.md). Download one from the API at `GET /api/v1/memory/export`.

## Check that it works

Say "remember that my favorite coffee is oat flat white", open **Memory** and find the chunk, then ask "what coffee do I like?" in a new session.

## Related

- [Memory model](../concepts/memory-model.md), [untrusted content](../concepts/untrusted-content.md), [context building](../concepts/context-building.md)
- [Data ownership](../concepts/data-ownership.md)
