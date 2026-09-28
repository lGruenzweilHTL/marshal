## ADDED Requirements

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
