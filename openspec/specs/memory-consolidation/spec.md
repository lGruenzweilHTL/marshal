# memory-consolidation Specification

## Purpose
How memory is written: explicit remembering, summarization when a session closes, and the audit of every change.

## Requirements

### Requirement: Explicit remember
The system SHALL provide `memory.remember(type, title, content, importance?, scope?, project?, sourceRef?)` (safe, `persists`). It SHALL write through the deterministic id (update in place when the id exists). It SHALL NOT be able to pin. In tainted runs it SHALL require confirmation (reason `taint`).

#### Scenario: Remember in a clean run
- **WHEN** the user says "remember that the Voron uses a 0.6 nozzle" and the run is not tainted
- **THEN** a `device` chunk is written without confirmation

#### Scenario: Remember after reading mail
- **WHEN** the run read a mail and then calls `memory_remember`
- **THEN** a pending action with reason `taint` is created and the question names mail as the source

### Requirement: Session-close summarization
When a session is closed after the idle TTL, or a web session rests after it, a cheap-model summarizer SHALL propose chunk candidates from the session's user turns and assistant replies only, never from raw tool results. Background results of the session SHALL count as assistant replies. The summarizer SHALL only read turns and background results that were not summarized before, so a web session that rests several times is not summarized again from its start. Candidates SHALL be written through the deterministic id; candidates that contradict an existing chunk SHALL create a `contradicts` edge instead of overwriting.

#### Scenario: Tool result excluded
- **WHEN** a session contained a mail body as a tool result
- **THEN** the summarizer input does not contain the mail body

#### Scenario: Contradiction
- **WHEN** a candidate says the nozzle is 0.4 mm while the stored chunk says 0.6 mm from a later update
- **THEN** a `contradicts` edge is created and the stored chunk is unchanged

#### Scenario: Second rest of a web session
- **WHEN** a web session rested once after three turns, was continued with two more turns and rests again
- **THEN** the second summarization reads only the two new turns

### Requirement: Audit
The system SHALL compute an audit listing stale chunks (not updated or retrieved for a configurable period), open contradictions and orphans (chunks of deleted projects, dangling edges).

#### Scenario: Open contradiction
- **WHEN** two chunks are linked by `contradicts`
- **THEN** the audit lists the pair until one supersedes the other or is deleted
