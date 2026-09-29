# memory-store Specification

## Purpose
TBD - created by archiving change memory. Update Purpose after archive.
## Requirements
### Requirement: Typed chunks
A memory chunk SHALL have owner, scope (`general` | `project`), project id (set if and only if scope is `project`), type (`fact`, `preference`, `event`, `procedure`, `person`, `device`), title, content, importance (1–5), pinned flag, optional `source_ref`, embedding, timestamps and optional soft-delete time. Chunk titles and content SHALL be written in English.

#### Scenario: Project chunk without project
- **WHEN** a chunk with scope `project` and no project id is written
- **THEN** the write fails with a validation error

### Requirement: Deterministic ids
The chunk id SHALL be a hash of owner id, scope, project id (empty for `general`), type and normalized title. Chunks SHALL never be merged by similarity. Renaming a project SHALL NOT change chunk ids.

#### Scenario: Same title twice
- **WHEN** two writes have the same owner, scope, project, type and normalized title
- **THEN** both address the same chunk id

#### Scenario: Different owners
- **WHEN** two owners write chunks with the same scope, type and title
- **THEN** the chunks have different ids

### Requirement: Version history
Updating a chunk under the same id SHALL update it in place and store the previous content, importance and source as a `ChunkVersion` row. A change of identity (title, scope or project) SHALL create a new chunk and a `supersedes` edge from new to old; `supersedes` SHALL never point from a chunk to itself.

#### Scenario: Same-id update
- **WHEN** chunk "Voron nozzle size" is updated from 0.4 to 0.6 mm
- **THEN** the chunk shows 0.6 mm and one `ChunkVersion` row holds 0.4 mm

#### Scenario: Move to a project
- **WHEN** a general chunk is moved to project "Voron"
- **THEN** a new chunk exists in the project and has a `supersedes` edge to the old one

### Requirement: Edges and soft delete
Chunks SHALL be linked by `supersedes`, `contradicts`, `depends_on` and `relates_to` edges. Deleting a chunk SHALL set its soft-delete time; soft-deleted chunks SHALL NOT be retrieved.

#### Scenario: Soft-deleted chunk
- **WHEN** a chunk is soft-deleted
- **THEN** it is excluded from retrieval but visible in the history view

### Requirement: Storage and indexes
Chunks SHALL be indexed in FTS5 (title, content) and in `sqlite-vec` (embedding). Embeddings SHALL come from the configured multilingual embedding model through `IEmbeddingGenerator`.

#### Scenario: Vector index updated
- **WHEN** a chunk's content changes
- **THEN** its embedding and FTS entry are updated in the same transaction

### Requirement: NDJSON export
The system SHALL export the owner's chunks, versions and edges as NDJSON, one object per line, stable field order, excluding embeddings.

#### Scenario: Export round trip
- **WHEN** the export is imported into an empty database for the same owner
- **THEN** chunks, versions and edges are identical and embeddings are recomputed

