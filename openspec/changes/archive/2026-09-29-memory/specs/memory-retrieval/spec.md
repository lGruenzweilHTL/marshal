## ADDED Requirements

### Requirement: Automatic retrieval
The context builder SHALL retrieve memory for every turn server-side, without a model tool call: embed the turn text (with area name), search, score, and add the result to the system prompt. The turn embedding SHALL be computed once and reused for project activation.

#### Scenario: Relevant preference retrieved
- **WHEN** the user asks about coffee and a `preference` chunk "Likes oat milk" exists
- **THEN** that chunk appears in the context without any tool call

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
The system SHALL provide `memory.recall(query, type?, project?, limit?)` (safe) for deliberate deep search with a larger budget than automatic retrieval.

#### Scenario: Recall by type
- **WHEN** the model calls `memory_recall` with type `procedure`
- **THEN** only procedure chunks are returned
