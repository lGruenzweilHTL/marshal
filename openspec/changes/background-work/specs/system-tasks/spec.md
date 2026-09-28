## ADDED Requirements

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
A cron task SHALL run memory consolidation over the day's closed sessions (user turns and assistant replies only), using the same summarizer as session close.

#### Scenario: Consolidation input
- **WHEN** consolidation runs
- **THEN** no raw tool result is part of its model input

### Requirement: Retention purge
A system cron task SHALL delete full run payloads older than 30 days and metadata older than 1 year (both configurable). Memory chunks SHALL NOT be affected.

#### Scenario: Payload expiry
- **WHEN** a run step payload is 31 days old
- **THEN** the payload is deleted and the step metadata stays
