# Spec Delta

## Purpose

Read-only access to an allowlist of GitHub repositories (pull requests, issues, workflow runs and their logs) as model tools, with diffs and logs kept as artifacts.

## ADDED Requirements

### Requirement: GitHub configuration
GitHub SHALL be configured as `github: { tokenSecret, repos }`, where `tokenSecret` names the secret holding a fine-grained personal access token and `repos` maps repo ids (`[a-z][a-z0-9-]*`) to `owner/name`. Without a `github` section or with an empty `repos`, no GitHub tools SHALL exist. An invalid id or a value that is not `owner/name` SHALL fail startup with a configuration error naming the key.

#### Scenario: Not configured
- **WHEN** `marshal.yaml` has no `github` section
- **THEN** no `github_*` tool is in any tool list

#### Scenario: Invalid repo value
- **WHEN** `github.repos.marshal` is `https://github.com/lukas/marshal`
- **THEN** startup fails with an error naming `github.repos.marshal`

### Requirement: Repo allowlist
Every GitHub tool SHALL take a `repo` argument whose JSON schema is an enum of the configured repo ids, with each id's `owner/name` in the tool description. A call with any other value SHALL fail validation with `github.invalid_arguments` and SHALL send no request. Requests SHALL only be sent for the configured `owner/name` of the given id.

#### Scenario: Repo outside the allowlist
- **WHEN** a tool is called with `repo: torvalds-linux` and only `marshal` is configured
- **THEN** the call fails with `github.invalid_arguments` and no request is sent to GitHub

### Requirement: Read-only access
The GitHub client SHALL only send `GET` requests. No tool or code path SHALL comment, review, merge, close, label, edit, dispatch or rerun workflows, or change repository settings. The documented token SHALL be fine-grained, limited to the allowlisted repos, with read-only permissions for Metadata, Contents, Pull requests, Issues and Actions.

#### Scenario: No write path
- **WHEN** the model asks to merge a pull request
- **THEN** no GitHub tool can do it and no request other than `GET` is sent

### Requirement: GitHub tools
The system SHALL provide these safe tools; their output SHALL be untrusted and SHALL taint the run with source `github`:
- `github.prs(repo, state?)`: up to 30 newest pull requests (`open` by default, `closed`, `all`) with number, title, author, draft flag, head and base branch and update time;
- `github.pr(repo, number)`: title, author, state, draft and merged flags, branches, body (cut at 4000 characters), changed files, additions, deletions, and the id of a `diff` artifact holding the pull request's diff;
- `github.issues(repo, state?, query?)`: up to 30 newest issues (pull requests excluded) with number, title, author, labels, comment count and update time; `query` filters titles case-insensitively among the 100 newest;
- `github.issue(repo, number)`: title, author, state, labels, body (cut at 4000 characters) and the 20 newest comments (author, time, body cut at 1000 characters);
- `github.runs(repo, branch?)`: up to 20 newest workflow runs with id, workflow name, title, branch, event, status, conclusion, short commit SHA and time;
- `github.run_log(repo, runId)`: for each failed job of the run, its name, failed step names and the last 50 log lines, and the id of a `code` artifact holding the job's full log.
A tool SHALL fail with `github.not_found` for an unknown number or run id and with `github.unavailable` when GitHub cannot be reached or rejects the token, naming the rate-limit reset time when the rate limit is exhausted.

#### Scenario: Pull request with diff
- **WHEN** `github_pr` is called for `marshal` PR 42
- **THEN** the result holds the PR's summary and the id of a `diff` artifact named `marshal-pr-42.diff`, and the run is tainted with source `github`

#### Scenario: Failed CI run
- **WHEN** `github_run_log` is called for a run with one failed job `build`
- **THEN** the result names `build`, its failed steps and the last 50 log lines, and the full log is a `code` artifact

#### Scenario: Issue title filter
- **WHEN** `github_issues` is called with `query: "printer"`
- **THEN** only issues whose title contains "printer" in any case are returned

#### Scenario: Rate limit exhausted
- **WHEN** GitHub answers 403 with `x-ratelimit-remaining: 0`
- **THEN** the tool fails with `github.unavailable` naming the reset time

### Requirement: GitHub artifacts
Diff and log artifacts SHALL be stored for the run's owner with source `github`, tainted, and a source reference that identifies the content (`<repo id>/pr/<number>@<head sha>` for diffs, `<repo id>/job/<job id>` for logs). When an artifact with the same owner, source and source reference exists, the tool SHALL return its id instead of storing a new one. When the owner's quota or the size limit is exceeded, the tool SHALL still return its summary and SHALL say that the diff or log was not stored.

#### Scenario: Same PR twice
- **WHEN** `github_pr` is called twice for PR 42 without new commits
- **THEN** both results name the same artifact id and one artifact exists

#### Scenario: New commit
- **WHEN** a commit is pushed to PR 42 and `github_pr` is called again
- **THEN** a new diff artifact is stored

#### Scenario: Over quota
- **WHEN** storing the log would exceed the owner's quota
- **THEN** the result holds the failed jobs and log tails and says the full log was not stored
