## MODIFIED Requirements

### Requirement: Artifact store
Each artifact SHALL be stored as a file under the data directory (`/data/artifacts/<owner>/`) with one database row holding owner, id, name, kind, media type, size, SHA-256, source (`upload`, `executor`, `printer`, `mail`, `github`), source reference, `tainted` flag and creation time. All access SHALL go through owner-scoped queries; another owner's artifact SHALL behave as not found. Deleting an artifact SHALL remove its row and its file.

#### Scenario: Other owner's artifact
- **WHEN** owner A requests an artifact id that belongs to owner B
- **THEN** the response is 404

#### Scenario: Delete
- **WHEN** the user deletes an artifact
- **THEN** its row and its file are gone and its links return 404

### Requirement: Artifact ingest
Artifacts SHALL be created only through these paths: web upload (`POST /api/v1/artifacts`, multipart, at most `artifacts.maxUploadMb` per file, default 200); executor uploads with the session's callback token; keeping a printer snapshot; `mail.save_attachment`; diffs and logs stored by the `github.pr` and `github.run_log` tools. Artifacts from executors, mail and GitHub SHALL be marked tainted; uploads and printer snapshots SHALL NOT.

#### Scenario: Upload
- **WHEN** the user uploads `notes.md`
- **THEN** an artifact of kind `markdown`, source `upload`, not tainted, is created and appears on the live-update topic `artifacts`

#### Scenario: Oversized upload
- **WHEN** the user uploads a 300 MB file with the default limit
- **THEN** the response is 413 and nothing is stored

#### Scenario: GitHub log
- **WHEN** `github_run_log` stores a job's log
- **THEN** an artifact of kind `code`, source `github`, tainted, is created and appears on the live-update topic `artifacts`
