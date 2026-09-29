# notes Specification

## Purpose
TBD - created by archiving change integrations. Update Purpose after archive.
## Requirements
### Requirement: Notes source
Notes SHALL be read through `INotesSource`. The v1 implementation SHALL read a git repository of Markdown files, either local under `/data/notes` or a remote repository cloned there and pulled on an interval. Only `.md` files SHALL be indexed.

#### Scenario: Remote repository
- **WHEN** a remote notes repository is configured
- **THEN** it is cloned into `/data/notes` on first start and pulled periodically

### Requirement: Notes index
The Notes integration SHALL maintain its own index (`NoteIndexEntry`: owner, path, heading, text, embedding) with FTS and multilingual embeddings, rebuilt per file on change. The index SHALL be derived data: never exported, rebuildable from the repository.

#### Scenario: File changed
- **WHEN** a note file changes after a pull
- **THEN** only that file's index entries are rebuilt

### Requirement: Notes tools
The system SHALL provide safe tools `notes.list(folder?)`, `notes.read(path)` and `notes.search(query)` (hybrid FTS + embeddings). Their results SHALL be `untrustedOutput` and `sensitive`. Paths SHALL be confined to the repository.

#### Scenario: Path traversal
- **WHEN** `notes_read` is called with `../../etc/passwd`
- **THEN** the call fails and nothing outside the repository is read

### Requirement: Notes are not memory
Notes SHALL never be injected into the context automatically. Content from notes SHALL enter memory only when the agent writes a chunk in its own words with `source_ref` set to the note path.

#### Scenario: No automatic injection
- **WHEN** a turn is about a topic covered by a note
- **THEN** the note is not in the context unless the model calls a notes tool

