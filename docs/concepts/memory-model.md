# Memory model

This page explains how Marshal remembers things across conversations: what a memory chunk is, how chunks reach a turn, how projects scope them, and how memory is written.

## The problem

An assistant without memory asks the same questions every day. An assistant with naive memory has other problems:

- It stores near-duplicates and merges facts that only look similar, so "nozzle 0.4" and "nozzle 0.6" become a mess.
- It relies on the model to call a "search memory" tool, which small models forget and which costs a step on every voice turn.
- It fills the prompt with everything it knows, or with whatever an attacker managed to get into a mail it summarized.
- It cannot tell you what it knows, where that came from, or what it used to believe.

## Chunks

Memory is a set of typed **chunks**. Each chunk has:

- an owner, a scope (`general` or `project`) and a project id (set only for project scope)
- a type: `fact`, `preference`, `event`, `procedure`, `person` or `device`
- a title and content, always written in English, whatever language you speak
- an importance from 1 to 5 and a pinned flag
- an optional `source_ref` (for example a note path)
- an embedding, timestamps and an optional soft-delete time

### Deterministic ids

A chunk's id is a hash of owner, scope, project, type and normalized title. Writing the same title again addresses the same chunk and updates it in place. Chunks are never merged by similarity. Two owners writing the same title get different ids. Renaming a project does not change chunk ids.

### Versions and edges

Updating a chunk keeps the previous content, importance and source as a version row, so "Voron nozzle size" shows 0.6 mm and its history shows 0.4 mm. A change of identity (title, scope or project) creates a new chunk with a `supersedes` edge to the old one.

Chunks are linked by four kinds of edges: `supersedes`, `contradicts`, `depends_on` and `relates_to`. Deleting a chunk soft-deletes it: it is no longer retrieved but stays visible in the history view.

![Entity relationship diagram](../diagrams/erd.svg)

### Storage

Chunks live in SQLite, indexed in FTS5 (title and content) and in `sqlite-vec` (embedding). Embeddings come from the configured multilingual embedding model; see [`embedding`](../configuration/marshal-yaml/embedding.md). When content changes, its embedding and FTS entry change in the same transaction.

## How memory reaches a turn

Retrieval is automatic and server-side. The model does not need to call a tool.

1. **Pinned layer.** Pinned chunks are always injected in full, before anything else, regardless of relevance.
2. **Turn embedding.** Marshal embeds the turn text, with the area name, once. The same embedding is reused for project activation.
3. **Seeds.** Vector similarity and FTS keyword matches both produce seeds (20 each by default). A keyword hit matters when an exact device name is missed by the embedding.
4. **Expansion.** Retrieval follows edges breadth-first from the seeds. Superseded chunks are replaced by their successors.
5. **Scoring.** Each candidate scores similarity × importance × freshness. Freshness decays with age since the last update, with a half-life per type (30 days for events, one year for facts and preferences, two years for people, procedures and devices).
6. **Quotas and budget.** Results respect per-type quotas (for example at most 5 events) and a token budget (600 tokens by default, pinned chunks are extra).

The result appears in the system prompt as "Relevant memory", with the note that it may be outdated and that what you say now wins. If the embedding server or the index fails, the turn continues without retrieved memory; device control never depends on it.

For deeper searches the model calls `memory.recall(query, type?, project?, limit?)`, which has a larger budget (2000 tokens by default).

The run's context-build step records which chunks were used and why, so the run detail in the web UI shows what Marshal knew for each turn.

## Projects

Projects scope memory softly. A chunk of an active project is boosted (× 1.5 by default); a chunk of an inactive project is penalized (× 0.5), never excluded. If an inactive project's chunk is the only strong match, it still appears.

A project becomes active in one of these ways:

| Way | How |
|---|---|
| Explicit | The turn text contains the project's name or an active alias. No model call. Reason `alias`. |
| Inferred | The turn embedding is compared with the project's centroid (description plus its top chunks, minus rejected phrases), plus a recency prior. High confidence (≥ 0.75) activates stickily, medium (≥ 0.6) for this turn only, low does nothing. |
| Sticky | A sticky activation carries over to later turns, decaying each turn, and is dropped on a topic shift. |

Inferred activation is never asked about by voice. Instead, you correct it: "not that project" makes the model call `memory.correct_project` with `rejected`, which unloads the project and stores the phrase as a negative example. Phrases that led to uncorrected inferences become alias candidates and are promoted to active aliases after 3 confirmations.

Active projects and their reasons are recorded in the run and shown in the web UI, never spoken.

## How memory is written

Memory is written in three ways, all through the deterministic id:

- **Explicitly.** `memory.remember(type, title, content, importance?, scope?, project?, sourceRef?)` writes or updates a chunk. It cannot pin; pinning is a user action in the web UI. In tainted runs it needs confirmation with reason `taint`.
- **At session close.** When a session closes after its idle TTL, a cheap model proposes chunk candidates from your turns and Marshal's replies only, never from raw tool results. A candidate that contradicts an existing chunk creates a `contradicts` edge instead of overwriting.
- **Nightly consolidation.** A system task runs the same summarizer over the day's closed sessions.

Notes are never memory by themselves. Content from notes enters memory only when the agent writes a chunk in its own words with `source_ref` set to the note path.

## Keeping memory healthy

An audit lists stale chunks (not updated or retrieved for 180 days by default), open contradictions and orphans (chunks of deleted projects, dangling edges). The memory view in the web UI shows chunks, versions, edges and the audit. Every night the backup writes an NDJSON export of each owner's chunks, versions and edges (without embeddings), which you can re-import; embeddings are recomputed.

See [memory](../guides/memory.md) for working with memory day to day.

## Trade-offs

- **English chunks.** Writing all chunks in English keeps titles stable for deterministic ids and works with one multilingual embedding model, at the cost of not storing your exact words.
- **No merging by similarity.** Deterministic ids mean two phrasings of the same fact can coexist. The audit and the summarizer's view of existing memory reduce this, but do not remove it.
- **Automatic retrieval costs prompt tokens on every turn.** The token budget and quotas keep this bounded.
- **Changing the embedding model needs a restart and re-embeds memory.**

## Related

- [Context building](context-building.md)
- [Untrusted content](untrusted-content.md)
- [Data ownership](data-ownership.md)
- [Backups and restore](../guides/backups-and-restore.md)
