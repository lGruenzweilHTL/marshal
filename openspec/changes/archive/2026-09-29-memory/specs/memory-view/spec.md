## ADDED Requirements

### Requirement: Memory API
The API SHALL provide owner-scoped endpoints to list, search, create, edit, pin, unpin, supersede and soft-delete chunks, read versions and edges, read the audit, export NDJSON, and manage projects (description, aliases, alias candidates, correction log). Pinning SHALL only be possible through this API.

#### Scenario: Pin from the UI
- **WHEN** the user pins a chunk in the memory view
- **THEN** the chunk is injected in full in every following turn

### Requirement: Memory views
The web UI SHALL provide a chunk list with search and filters, an editor, a history view (versions and supersedes chains), the audit, projects with aliases and correction log, a force-directed graph of chunks and edges, and a PCA scatter of embeddings.

#### Scenario: History
- **WHEN** a chunk has two versions and one superseded predecessor
- **THEN** the history view shows all three in order

#### Scenario: Graph on a phone
- **WHEN** the memory view is opened at phone width
- **THEN** the list view is shown by default and the graph is available without horizontal page scroll
