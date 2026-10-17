## MODIFIED Requirements

### Requirement: Nightly memory consolidation
A cron task SHALL run memory consolidation over the day's closed sessions (user turns and assistant replies only), using the same summarizer as session close. After it, the system SHALL run the idle-time jobs per owner: contradiction proposals for open `contradicts` pairs without an open or dismissed proposal, duplicate proposals for pairs of open chunks with the same owner, type and scope whose embedding similarity is at least `memory.duplicateThreshold` (default 0.92), and project summaries for projects whose chunks changed since their last summary. The jobs SHALL read only memory chunks, never sessions or tool results, and SHALL count against the global daily cost cap.

#### Scenario: Consolidation input
- **WHEN** consolidation runs
- **THEN** no raw tool result is part of its model input

#### Scenario: Duplicate found
- **WHEN** two open `preference` chunks "Likes oat milk" and "Prefers oat milk in coffee" have similarity 0.95
- **THEN** one open duplicate proposal for the pair exists after the nightly run

#### Scenario: Summary only on change
- **WHEN** no chunk of project "Voron" changed since its last summary
- **THEN** the nightly run makes no model call for that project
