# memory-view Specification

## Purpose
The memory API and web views to browse, edit, pin and audit memory.

## Requirements

### Requirement: Memory API
The API SHALL provide owner-scoped endpoints to list, search, create, edit (including valid time), pin, unpin, supersede and soft-delete chunks, read versions and edges, read the audit, list, accept and dismiss memory proposals, export NDJSON, and manage projects (description, aliases, alias candidates, correction log, read-only summary). Pinning SHALL only be possible through this API.

#### Scenario: Pin from the UI
- **WHEN** the user pins a chunk in the memory view
- **THEN** the chunk is injected in full in every following turn

#### Scenario: Accept a proposal
- **WHEN** the user accepts a duplicate proposal through the API
- **THEN** the kept chunk is updated, the other is superseded, and the proposal is `accepted`

### Requirement: Memory views
The web UI SHALL provide a chunk list with search and filters (including a filter for closed chunks), an editor with valid time, a history view (versions and supersedes chains with their valid times), the audit with open proposals that the user can accept or dismiss, projects with aliases, correction log and summary, and a map of chunks that switches with one toggle between a force-directed graph of chunks and edges and a PCA scatter of embeddings.

#### Scenario: History
- **WHEN** a chunk has two versions and one superseded predecessor
- **THEN** the history view shows all three in order with their valid times

#### Scenario: Map toggle
- **WHEN** the user switches the map from graph to scatter
- **THEN** the same chunks are shown as a scatter without leaving the page

#### Scenario: Graph on a phone
- **WHEN** the map is opened at phone width
- **THEN** a list of the same chunks is shown instead of the graph, without horizontal page scroll

#### Scenario: Proposal in the audit
- **WHEN** a contradiction proposal is open
- **THEN** the audit shows both chunks, the suggestion and accept and dismiss buttons
