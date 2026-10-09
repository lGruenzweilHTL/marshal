# executors Specification

## Purpose
Long external work such as coding sessions: the executor interface, the separate executor host, callback tokens, and code changes reviewed and merged only in the web UI.

## Requirements

### Requirement: Executor interface
Long external work SHALL run through `IExecutor` (`StartAsync(spec)` → handle; completion arrives later as an event). Executor sessions SHALL be recorded with task, executor, handle, callback token and status.

#### Scenario: Completion as event
- **WHEN** an executor finishes
- **THEN** an `executor.completed` event triggers the task's `onDoneGoal` run

### Requirement: Separate executor host
The coding executor SHALL run as a separate service (`Marshal.ExecutorHost`, other host / VM or rootless Docker / Sysbox) that Marshal calls over HTTP with a scoped token. Marshal SHALL never access a Docker socket.

#### Scenario: No Docker socket
- **WHEN** the Marshal container is inspected
- **THEN** no Docker socket is mounted and no Docker client is referenced

### Requirement: Per-task callback tokens
Each executor session SHALL get an `executor_callback` token valid only for that session's callbacks, revoked when the session ends.

#### Scenario: Reused token
- **WHEN** a callback token is used after its session ended
- **THEN** the request is rejected with 401

#### Scenario: Wrong task
- **WHEN** a callback token of task A posts a callback for task B
- **THEN** the request is rejected with 403

### Requirement: Claude Code executor
The first executor SHALL run Claude Code headless (`claude -p`, stream-json) in a Docker sandbox on the executor host, with mounted repositories, a git worktree per task and a network allowlist.

#### Scenario: Worktree per task
- **WHEN** two coding tasks run against the same repository
- **THEN** each works in its own worktree and branch

### Requirement: Coding results are reviewed in the UI
A finished coding session SHALL produce a `Proposal` (kind `code`) with branch and diff, reviewed in the review queue. Merge SHALL be a user action in the web UI only; voice SHALL only announce that a review is ready.

#### Scenario: Voice notice
- **WHEN** a coding task completes
- **THEN** the voice delivery says the change is ready for review and offers no merge action

### Requirement: Merge and discard on the executor host
Merging a proposal SHALL ask the executor host to merge the session's branch into the branch it started from, in the host's repository, without pushing; the branch SHALL be deleted after the merge. A merge that conflicts or finds the base branch moved in a conflicting way SHALL change nothing and leave the proposal open. Rejecting a proposal SHALL ask the host to delete the branch (best effort; the rejection stands when the host is unreachable). Marshal SHALL still never access the repositories itself.

#### Scenario: Merge
- **WHEN** the user merges an open code proposal
- **THEN** the host merges `marshal/<id>` into its base branch, deletes the branch, and the proposal is `merged`

#### Scenario: Conflict
- **WHEN** the base branch changed so that the session's branch no longer merges cleanly
- **THEN** the merge is refused with a conflict, no ref changes, and the proposal stays open

### Requirement: Executor artifacts
When a session ends, the executor host SHALL upload each regular file under `.marshal/artifacts/` in the session's worktree to Marshal, one request per file, authenticated with the session's callback token, before it posts completion. Marshal SHALL store each as a tainted artifact with source `executor` and the session's task as source reference, owned by the task's owner. Files over the upload limit, over the owner's quota, symbolic links and files outside that folder SHALL be skipped and named in the completion notes. Uploads SHALL be rejected once the session has ended.

#### Scenario: Model from a CAD task
- **WHEN** a coding session writes `.marshal/artifacts/bracket.stl` and finishes
- **THEN** an artifact `bracket.stl` of kind `model3d`, source `executor`, tainted, exists for the task's owner before the `onDoneGoal` run starts

#### Scenario: Symbolic link
- **WHEN** `.marshal/artifacts/secrets` is a symbolic link to a file outside the worktree
- **THEN** it is not uploaded and the completion notes name it as skipped

#### Scenario: Upload after the session ended
- **WHEN** a callback token posts an artifact after its session ended
- **THEN** the request is rejected with 401
