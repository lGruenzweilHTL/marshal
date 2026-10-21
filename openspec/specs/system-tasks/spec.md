# system-tasks Specification

## Purpose
Code-only maintenance jobs run by the task system: nightly backup, memory export, memory consolidation and the retention purge.

## Requirements

### Requirement: Nightly backup
A system cron task SHALL create an SQLite online backup with `VACUUM INTO` in `/data/backups`, include a copy of `/data/keys`, and keep N copies (configurable).

#### Scenario: Retention of copies
- **WHEN** N+1 backups exist after a run
- **THEN** the oldest is deleted

#### Scenario: Keys included
- **WHEN** a backup is restored to a new instance
- **THEN** DB secrets can be decrypted

### Requirement: Memory export
The backup run SHALL also write the NDJSON memory export of every owner.

#### Scenario: Export file
- **WHEN** the nightly backup completes
- **THEN** a dated NDJSON export exists next to the backup

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

### Requirement: Retention purge
A system cron task SHALL delete full run payloads older than 30 days and metadata older than 1 year (both configurable). Memory chunks SHALL NOT be affected.

#### Scenario: Payload expiry
- **WHEN** a run step payload is 31 days old
- **THEN** the payload is deleted and the step metadata stays
