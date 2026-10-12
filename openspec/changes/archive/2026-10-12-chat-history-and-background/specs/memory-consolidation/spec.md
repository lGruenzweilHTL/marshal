# Spec Delta

## MODIFIED Requirements

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
