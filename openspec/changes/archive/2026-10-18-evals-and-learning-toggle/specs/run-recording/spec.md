## ADDED Requirements

### Requirement: Turn context snapshot
The context step of every run SHALL store the text of the run's turn-level context contributions (for example retrieved memory, area context, time, task section) separately from the static and session parts, so that the turn can be rebuilt for replay. The snapshot SHALL be removed with the run by the retention purge.

#### Scenario: Snapshot recorded
- **WHEN** a turn's context contains two retrieved memory chunks and the area context
- **THEN** its context step stores their text as the turn-level snapshot
