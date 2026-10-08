## ADDED Requirements

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
