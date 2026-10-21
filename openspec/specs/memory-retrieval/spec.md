# memory-retrieval Specification

## Purpose
How memory reaches a run: automatic retrieval, the pinned layer, hybrid search with scoring and quotas, and deliberate recall.

## Requirements

### Requirement: Automatic retrieval
The context builder SHALL retrieve memory for every turn server-side, without a model tool call: embed the turn text (with area name), search, score, and add the result to the system prompt. Closed chunks SHALL NOT be retrieved automatically. Chunks with a `validFrom` in the future SHALL be retrieved with their valid time shown. The turn embedding SHALL be computed once and reused for project activation.

#### Scenario: Relevant preference retrieved
- **WHEN** the user asks about coffee and a `preference` chunk "Likes oat milk" exists
- **THEN** that chunk appears in the context without any tool call

#### Scenario: Closed fact not retrieved
- **WHEN** chunk "Works at Contoso" has `validTo` 2026-06-30 and the user asks about work on 2026-10-20
- **THEN** that chunk is not in the context

### Requirement: Pinned layer
Pinned chunks SHALL always be injected in full, before retrieved chunks, regardless of relevance.

#### Scenario: Pinned always present
- **WHEN** a turn is unrelated to a pinned chunk
- **THEN** the pinned chunk is still in the context

### Requirement: Hybrid two-phase search
Retrieval SHALL combine vector similarity and FTS keyword matches as seeds, then expand one or more steps along edges (breadth-first). Superseded chunks SHALL be replaced by their successors.

#### Scenario: Keyword-only hit
- **WHEN** the query contains an exact device name that the embedding misses
- **THEN** the FTS match is included as a seed

#### Scenario: Superseded chunk
- **WHEN** a seed chunk is superseded
- **THEN** its successor is returned instead

### Requirement: Scoring and quotas
Each candidate SHALL be scored as similarity × importance × freshness (freshness decays with age since last update). Results SHALL respect per-type quotas and a total token budget. Chunks of active projects SHALL be boosted and chunks of inactive projects penalized, never excluded.

#### Scenario: Quota
- **WHEN** 20 `event` chunks score higher than any `preference` chunk and the event quota is 5
- **THEN** at most 5 events are returned and preferences fill the remaining slots

#### Scenario: Inactive project not excluded
- **WHEN** a chunk of an inactive project is the only strong match
- **THEN** it is still returned, with a lower score than if its project were active

### Requirement: Deliberate recall
The system SHALL provide `memory.recall(query, type?, project?, limit?, asOf?)` (safe) for deliberate deep search with a larger budget than automatic retrieval. Without `asOf`, it SHALL search open chunks. With `asOf` (a date or date-time in the owner's time zone), it SHALL search the chunks and versions that were valid at that time, including closed ones, and SHALL return each result with its valid time.

#### Scenario: Recall by type
- **WHEN** the model calls `memory_recall` with type `procedure`
- **THEN** only procedure chunks are returned

#### Scenario: Earlier value
- **WHEN** the nozzle chunk was 0.4 mm until 2026-03-01 and is 0.6 mm since, and the model calls `memory_recall` for the nozzle with `asOf` 2026-02-01
- **THEN** the result is the 0.4 mm version with its valid time
