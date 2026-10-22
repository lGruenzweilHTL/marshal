---
icon: fa-solid fa-brain
---

# Memory

Teach Marshal facts and preferences, say when they were true, check what it remembers, and manage projects, pinning, the audit and its proposals.

## Before you start

- A multilingual embedding model is configured, see [`embedding`](../configuration/marshal-yaml/embedding.md). Changing it re-embeds all memory at the next start.
- Concepts: [memory model](../concepts/memory-model.md).

## What Marshal remembers

Memory is a set of chunks. Each has a type (`fact`, `preference`, `event`, `procedure`, `person`, `device`), a title, content, an importance from 1 to 5, a scope (`general` or `project`), a valid time and a history of versions. Titles and content are written in English. The chunk id is a hash of owner, scope, project, type and normalized title, so writing the same title again updates the same chunk and keeps the previous content as a version. Chunks are never merged by similarity.

## Use your memory

### Tell Marshal to remember something

```text
Remember that the Voron uses a 0.6 mm nozzle.
```

The agent calls `memory.remember` (type, title, content, optional importance, scope, project, sourceRef). In a clean run it writes without asking. In a tainted run (it read mail, RSS, notes, web content or event data) the write needs your confirmation with the reason `taint`, see [approvals](tasks/approvals.md). `memory.remember` cannot pin.

Marshal also learns by itself: when a session closes or a web chat session rests after `sessions.idleTtl`, a fast-tier summarizer proposes chunks from your turns and Marshal's replies only, never from raw tool results. A candidate that contradicts a stored chunk creates a `contradicts` edge and leaves the stored chunk unchanged. A nightly consolidation catches sessions the close path missed.

### Say since when or until when

Each chunk and each version has a valid time: `validFrom` and an optional `validTo`, the time span in which the fact was true in the world. This is separate from when Marshal stored it.

```text
Remember that I have worked at Fronius since March 2025.
```

The agent passes `validFrom` and `validTo` to `memory.remember` as a date (`2025-03-01`) or an ISO 8601 date-time. Both are read in your time zone; a date means 00:00. Without `validFrom` the chunk is valid from the moment it is written. A `validTo` before `validFrom` is rejected. The summarizer sets a valid time only when your turns state one.

Marshal closes valid time itself in two cases:

- Updating a chunk in place: the previous content becomes a version that ends when the new content starts.
- Superseding a chunk: the old chunk ends when its successor starts.

A chunk whose `validTo` has passed is closed. Closed chunks stay in memory and in the history, but automatic retrieval leaves them out.

### See what is used in a turn

Retrieval is automatic: for each turn Marshal embeds the text, searches by vector and keyword (FTS), expands along edges, replaces superseded chunks with their successors and scores each candidate as similarity x importance x freshness. Per-type quotas and a token budget apply. Pinned chunks are always injected in full, before the retrieved ones. Open **Runs**, choose a run and read its context step to see which chunks and projects were used.

For a deliberate deep search the agent calls `memory.recall` (query, optional type, project, limit) with a larger token budget. Without `asOf` it searches chunks that are valid now. With `asOf` (a date or date-time) it searches what was valid at that time, including closed chunks and earlier versions, and every result carries its valid time:

```text
Which nozzle did the Voron have in August?
```

For active projects, the turn also gets the project's summary (see [step 7](#use-projects)).

### Browse and edit in the web UI

**Memory** has these tabs:

| Tab | Use |
|---|---|
| Chunks | Search and filter (the **No longer true** filter lists closed chunks), open the editor, edit content, importance and valid time, view history (versions and supersedes chains with their valid times), pin or unpin, delete (soft delete). |
| Projects | Descriptions, aliases, alias candidates, the correction log and Marshal's read-only project summary. |
| Map | Toggle between a force-directed graph of chunks and edges and a PCA scatter of embeddings. On a phone the map shows a list instead. |
| Audit | Proposals to accept or dismiss, stale chunks, open contradictions and orphans. |

Pinning is possible only here (or through the API); the agent cannot pin. Soft-deleted chunks are excluded from retrieval but stay in the history view. To replace a chunk with a changed identity, use supersede: it creates a new chunk with a `supersedes` edge.

In the editor, **True since** and **True until** set the valid time. Leave **True until** empty while the fact still holds; clearing it reopens a closed chunk. The fields use your browser's time zone.

### Resolve the audit

The audit lists chunks not updated or retrieved for `memory.audit.staleAfterDays` (default 180), contradiction pairs (until one supersedes the other, is closed or is deleted) and orphans (chunks of deleted projects, dangling edges). Open each item and edit, supersede or delete.

### Accept or dismiss proposals

After the nightly consolidation, idle-time jobs look at memory while nobody talks to Marshal. They use the `fast` tier, read only chunks and project descriptions (never sessions or tool results), and count against the daily cost cap of background tasks. One job failing does not stop the others.

| Job | What it does |
|---|---|
| Contradiction proposals | For each open `contradicts` pair, suggests which fact is still true and since when the other one is outdated. Each pair is proposed once. |
| Duplicate proposals | For chunks changed since the last night, finds chunks of the same type and scope whose embeddings are at least `memory.duplicateThreshold` (default 0.92) similar, and suggests a merged text. |
| Project summaries | Writes a summary of at most 120 words for each project whose chunks changed. |

Proposals appear at the top of **Memory → Audit**. Nothing changes until you decide:

- **Accept** on a contradiction closes the outdated chunk at the suggested time.
- **Accept** on a duplicate updates the kept chunk with the merged text and supersedes the other one.
- **Dismiss** leaves memory unchanged. A dismissed pair is not proposed again.

Accepting is recorded as your action in the chunk versions and in the audit log. The API offers the same through `GET /api/v1/memory/proposals` and `POST /api/v1/memory/proposals/{id}/accept` or `/dismiss`.

### Use projects

A project groups chunks (scope `project`). Marshal activates a project:

- By name or alias in the turn text, with no model call (reason `alias`).
- By similarity of the turn to the project's centroid (description plus top chunks, minus rejected phrases). High confidence activates stickily, medium for this turn only, low does nothing. Sticky activations decay each turn and drop on a topic shift.

Chunks of active projects are boosted and chunks of inactive projects are penalized, never excluded. If Marshal picks the wrong project, say so ("not that project"): the agent calls `memory.correct_project` with `rejected`, `switched` or `confirmed`. Every correction is stored with its phrase. Phrases that led to an uncorrected inference become alias candidates and are promoted to aliases after 3 confirmations (`memory.projects.aliasPromotion`). Active projects per turn and the reason are shown in the run detail, never spoken.

When an active project has a summary, Marshal adds it to the turn's context. **Memory → Projects** shows the summary and when Marshal wrote it; you cannot edit it, because the next nightly run would overwrite it. A project without a summary adds nothing.

## Tuning

Defaults are starting points: retrieval token budget `memory.retrieval.tokenBudget` (600), per-type quotas and half-lives, project confidence thresholds (`highConfidence` 0.75, `mediumConfidence` 0.6), summarizer `memory.summarizer.enabled` and `maxTurns`, and the duplicate threshold `memory.duplicateThreshold` (0.92). All keys are in [`embedding` and `memory`](../configuration/marshal-yaml/embedding.md).

## Export and backup

The nightly backup writes an NDJSON export of each owner's chunks, versions and edges with their valid times (without embeddings) next to the database copy, see [backups and restore](backups-and-restore.md). Download one from the API at `GET /api/v1/memory/export`.

## Check that it works

Say "remember that my favorite coffee is oat flat white", open **Memory** and find the chunk, then ask "what coffee do I like?" in a new session.

## Related

- [Memory model](../concepts/memory-model.md), [untrusted content](../concepts/untrusted-content.md), [context building](../concepts/context-building.md)
- [Data ownership](../concepts/data-ownership.md)
