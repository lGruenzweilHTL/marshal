## MODIFIED Requirements

### Requirement: Automatic retrieval
The context builder SHALL retrieve memory for every turn server-side, without a model tool call: embed the turn text (with area name), search, score, and add the result to the system prompt. Closed chunks SHALL NOT be retrieved automatically. Chunks with a `validFrom` in the future SHALL be retrieved with their valid time shown. The turn embedding SHALL be computed once and reused for project activation.

#### Scenario: Relevant preference retrieved
- **WHEN** the user asks about coffee and a `preference` chunk "Likes oat milk" exists
- **THEN** that chunk appears in the context without any tool call

#### Scenario: Closed fact not retrieved
- **WHEN** chunk "Works at Contoso" has `validTo` 2026-06-30 and the user asks about work on 2026-10-20
- **THEN** that chunk is not in the context

### Requirement: Deliberate recall
The system SHALL provide `memory.recall(query, type?, project?, limit?, asOf?)` (safe) for deliberate deep search with a larger budget than automatic retrieval. Without `asOf`, it SHALL search open chunks. With `asOf` (a date or date-time in the owner's time zone), it SHALL search the chunks and versions that were valid at that time, including closed ones, and SHALL return each result with its valid time.

#### Scenario: Recall by type
- **WHEN** the model calls `memory_recall` with type `procedure`
- **THEN** only procedure chunks are returned

#### Scenario: Earlier value
- **WHEN** the nozzle chunk was 0.4 mm until 2026-03-01 and is 0.6 mm since, and the model calls `memory_recall` for the nozzle with `asOf` 2026-02-01
- **THEN** the result is the 0.4 mm version with its valid time
