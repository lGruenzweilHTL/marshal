## MODIFIED Requirements

### Requirement: Typed chunks
A memory chunk SHALL have owner, scope (`general` | `project`), project id (set if and only if scope is `project`), type (`fact`, `preference`, `event`, `procedure`, `person`, `device`), title, content, importance (1–5), pinned flag, optional `source_ref`, embedding, valid time (`validFrom`, defaulting to the creation time, and optional `validTo`; `validTo` SHALL be after `validFrom`), timestamps and optional soft-delete time. A chunk SHALL be closed when its `validTo` is in the past. Chunk titles and content SHALL be written in English.

#### Scenario: Project chunk without project
- **WHEN** a chunk with scope `project` and no project id is written
- **THEN** the write fails with a validation error

#### Scenario: Default valid time
- **WHEN** a chunk is written without valid time on 2026-10-20
- **THEN** its `validFrom` is 2026-10-20 and its `validTo` is empty

#### Scenario: Invalid range
- **WHEN** a chunk is written with `validTo` before `validFrom`
- **THEN** the write fails with a validation error

### Requirement: Version history
Updating a chunk under the same id SHALL update it in place and store the previous content, importance, source and valid time as a `ChunkVersion` row; the stored version's `validTo` SHALL be set to the new content's `validFrom` when it was open. A change of identity (title, scope or project) SHALL create a new chunk and a `supersedes` edge from new to old; `supersedes` SHALL never point from a chunk to itself, and the old chunk's `validTo` SHALL be set to the new chunk's `validFrom` when it was open.

#### Scenario: Same-id update
- **WHEN** chunk "Voron nozzle size" is updated from 0.4 to 0.6 mm with `validFrom` 2026-03-01
- **THEN** the chunk shows 0.6 mm valid from 2026-03-01, and one `ChunkVersion` row holds 0.4 mm valid until 2026-03-01

#### Scenario: Move to a project
- **WHEN** a general chunk is moved to project "Voron"
- **THEN** a new chunk exists in the project, has a `supersedes` edge to the old one, and the old chunk is closed
