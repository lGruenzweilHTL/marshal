# Design

## Context

See proposal.md (Why). Current state:

- Chunks have deterministic ids, in-place updates with `ChunkVersion` rows, `supersedes` / `contradicts` / `depends_on` / `relates_to` edges, soft delete, an embedding in sqlite-vec and FTS5 text.
- Retrieval is two-phase (vector + FTS) with scoring (similarity × importance × freshness), quotas and project boost.
- The summarizer (cheap model) writes candidates from user turns and replies; contradictions become edges; the audit lists stale chunks, open contradictions and orphans.
- `ConsolidationJob` runs nightly as a system task.
- Tasks already have `notes` (free text the run sees); the briefing is a named agent task with a cron trigger.

## Goals / Non-Goals

**Goals:**
- Answer "when" and "before" questions from memory without growing every prompt.
- Turn the audit from a list of problems into proposals the user resolves with one click.
- Cheaper, faster briefings by moving reading work to the night.

**Non-Goals:**
- Valid time on edges.
- Re-embedding versions. `asOf` searches versions by text and by the chunk's embedding.

## Decisions

### 1. Valid time columns

`memory_chunks.valid_from` (not null, backfilled from `created_at`), `memory_chunks.valid_to` (nullable); same on `chunk_versions`. Stored as UTC instants. Inputs are dates or date-times in the owner's time zone; a date means 00:00 local. "Closed" = `valid_to <= now`.

Automatic retrieval adds `valid_to is null or valid_to > now` to both search phases, so closed chunks cost nothing. The deterministic id does not include valid time: a fact keeps its identity across its lifetime, and its history lives in versions.

### 2. `asOf` search

`memory.recall(asOf)` searches open and closed chunks where `valid_from <= asOf < coalesce(valid_to, ∞)`. Versions are searched through FTS on their content (new FTS rows for versions) and through their parent chunk's embedding. A version only matches when its own valid time covers `asOf`. Results carry the valid time so the model can phrase "until March". New interface: none; `IMemoryRetriever` gains the parameter.

### 3. Proposals and idle-time jobs

`memory_proposals (owner_id, id, kind, chunk_a, chunk_b, suggestion_json, status, created_at, decided_at)`. A unique index on `(owner_id, kind, min(chunk), max(chunk))` blocks re-proposing dismissed pairs.

New interfaces (`Marshal.Memory`):
- `IIdleMemoryJob`: `RunAsync(OwnerId, CancellationToken)`; v1: `ContradictionProposalJob`, `DuplicateProposalJob`, `ProjectSummaryJob`. `ConsolidationJob` runs them in that order after summarization, each in its own try/catch so one failure does not stop the others.
- `IMemoryProposals`: list, accept, dismiss; v1 `EfMemoryProposals`. Accept applies through the normal memory write path with the user as actor, so versions and the audit log record it.

Duplicate candidates: for each chunk changed since the last run, query sqlite-vec for neighbors with the same owner, type and scope above the threshold. This is bounded by the day's changes, not quadratic in memory size. Contradiction and duplicate suggestions come from the `fast` tier with a JSON Schema answer (reusing the AI task change's structured output shaper). Summaries come from the `fast` tier from chunk titles and contents only.

### 4. Project summaries in context

`projects.summary`, `projects.summary_updated_at`. The project activation contributor (stability `turn`) adds the summaries of active projects. Summaries are derived from memory only, which is already trusted user-confirmed data, so no taint.

### 5. Task preparation

`tasks.prepare_before` (interval), `tasks.prepare_goal`, `tasks.prepared_notes`, `tasks.prepared_taint` (JSON), `tasks.prepared_at`. The scheduler computes the preparation time from the next cron occurrence minus `before` and enqueues a run with trigger `prepare` and the preparation goal. The main run's context gets a "Prepared notes" section (stability `turn`) and its execution context starts with the stored taint sources. This stays generic: any cron task can prepare. The briefing example uses it, with no briefing-specific code.

Alternative: a briefing-specific precompute job. Rejected because the briefing must stay "only a task".

## Risks / Trade-offs

- [The model sets wrong valid times from vague speech] → valid times are visible and editable; `asOf` results show them; the summarizer only sets them when the turns state them.
- [Version FTS rows grow the index] → versions are small and few per chunk; retention purge already removes old soft-deleted data.
- [Prepared notes go stale (late calendar change)] → `before` is at most 12 hours; the main run can still call tools; notes say when they were prepared.
- [Nightly jobs cost money with cloud models] → bounded by the day's changes and counted in the daily cap; `fast` tier.

## Migration Plan

EF migration: valid time columns (backfill `valid_from = created_at`), version FTS rows (built by the existing index rebuilder on first start), `memory_proposals`, project summary columns, task preparation columns. Rollback: older versions ignore new columns; closed chunks would be retrieved again.
