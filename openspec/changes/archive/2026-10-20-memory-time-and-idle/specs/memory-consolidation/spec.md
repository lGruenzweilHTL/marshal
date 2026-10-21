## MODIFIED Requirements

### Requirement: Explicit remember
The system SHALL provide `memory.remember(type, title, content, importance?, scope?, project?, sourceRef?, validFrom?, validTo?)` (safe, `persists`). It SHALL write through the deterministic id (update in place when the id exists). `validFrom` and `validTo` SHALL be dates or date-times in the owner's time zone. It SHALL NOT be able to pin. In tainted runs it SHALL require confirmation (reason `taint`).

#### Scenario: Remember in a clean run
- **WHEN** the user says "remember that the Voron uses a 0.6 nozzle" and the run is not tainted
- **THEN** a `device` chunk is written without confirmation

#### Scenario: Remember after reading mail
- **WHEN** the run read a mail and then calls `memory_remember`
- **THEN** a pending action with reason `taint` is created and the question names mail as the source

#### Scenario: Remember with valid time
- **WHEN** the user says "I've been working at Fabrikam since March" and the model calls `memory_remember` with `validFrom` 2026-03-01
- **THEN** the chunk is valid from 2026-03-01

### Requirement: Session-close summarization
When a session is closed after the idle TTL, or a web session rests after it, a cheap-model summarizer SHALL propose chunk candidates from the session's user turns and assistant replies only, never from raw tool results. Background results of the session SHALL count as assistant replies. The summarizer SHALL only read turns and background results that were not summarized before, so a web session that rests several times is not summarized again from its start. Candidates MAY carry `validFrom` and `validTo` when the turns state them. Candidates SHALL be written through the deterministic id; candidates that contradict an existing chunk SHALL create a `contradicts` edge instead of overwriting.

#### Scenario: Tool result excluded
- **WHEN** a session contained a mail body as a tool result
- **THEN** the summarizer input does not contain the mail body

#### Scenario: Contradiction
- **WHEN** a candidate says the nozzle is 0.4 mm while the stored chunk says 0.6 mm from a later update
- **THEN** a `contradicts` edge is created and the stored chunk is unchanged

#### Scenario: Second rest of a web session
- **WHEN** a web session rested once after three turns, was continued with two more turns and rests again
- **THEN** the second summarization reads only the two new turns

#### Scenario: Stated valid time
- **WHEN** the user said "until last month the printer was in the garage"
- **THEN** the candidate carries a `validTo` at the end of last month

## ADDED Requirements

### Requirement: Memory proposals
The system SHALL keep memory proposals (owner, kind `contradiction` | `duplicate`, the chunk ids, a suggestion, status `open` | `accepted` | `dismissed`, creation time). A contradiction proposal SHALL suggest which chunk is current and the valid times to set. A duplicate proposal SHALL suggest the merged title and content. Accepting SHALL apply the suggestion as the user (contradiction: close the outdated chunk; duplicate: update the kept chunk and supersede the other); dismissing SHALL keep memory unchanged and SHALL prevent the same pair from being proposed again for the same kind. Proposals SHALL never change memory without a user action. The audit SHALL list open proposals.

#### Scenario: Accept a contradiction proposal
- **WHEN** the user accepts a proposal that says the 0.6 mm chunk is current since 2026-03-01
- **THEN** the 0.4 mm chunk is closed at 2026-03-01, the `contradicts` edge is resolved and the audit no longer lists the pair

#### Scenario: Dismissed duplicate
- **WHEN** the user dismisses a duplicate proposal for two chunks
- **THEN** both chunks stay unchanged and the pair is not proposed again
