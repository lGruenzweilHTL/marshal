# Design

## Context

See proposal.md (Why) and the explore discussion (option B). Facts about the GitHub REST API (checked against docs.github.com):

- Requests carry `Authorization: Bearer <token>`, `Accept: application/vnd.github+json` and `X-GitHub-Api-Version` (current: `2026-03-10`; pinned in code).
- Endpoints used: `GET /repos/{o}/{r}/pulls?state=&per_page=`, `GET /repos/{o}/{r}/pulls/{n}` (JSON, and the diff with `Accept: application/vnd.github.diff`), `GET /repos/{o}/{r}/issues?state=&per_page=` (contains PRs, marked by a `pull_request` key), `GET /repos/{o}/{r}/issues/{n}`, `GET /repos/{o}/{r}/issues/{n}/comments`, `GET /repos/{o}/{r}/actions/runs?branch=&per_page=`, `GET /repos/{o}/{r}/actions/runs/{id}/jobs` (jobs with `steps[].conclusion`), `GET /repos/{o}/{r}/actions/jobs/{job_id}/logs` (302 to a storage URL valid for one minute).
- Rate limit: 403/429 with `x-ratelimit-remaining: 0` and `x-ratelimit-reset` (epoch seconds).
- Fine-grained PAT permissions: Metadata, Contents, Pull requests, Issues, Actions — all read.
- `IArtifactStore.CreateAsync(draft, stream)` already enforces size limit and quota; `ListAsync` filters by kind, source and name, not by source reference. `.log` and `.diff` are detected as `code` and `diff`.

## Goals / Non-Goals

**Goals:**
- The allowlist is structural: other repos cannot be named in a call.
- Large text (diffs, logs) goes to artifacts; the model gets summaries.

**Non-Goals:**
- ETag caching and conditional requests (useful once polling exists).
- Pagination beyond the first page; the tool limits are below one page.

## Decisions

### 1. Project and interfaces

New project `Marshal.Integrations.GitHub` (references `Marshal.Abstractions`), registered by `Marshal.Api` when `github` is configured with at least one repo.

| Interface | Project | v1 implementation | Purpose |
|---|---|---|---|
| `IGitHubClient` | GitHub integration | `GitHubRestClient` (named `HttpClient`, resilience handler) | typed reads per endpoint above; `Result<T>` with `github.not_found` / `github.unavailable` |
| `IArtifactStore.FindBySourceAsync(source, reference)` | Abstractions (existing interface, new member) | `FileArtifactStore` | owner-scoped lookup for dedup |

`GitHubRepo(Id, Owner, Name)` comes only from options; the client takes a `GitHubRepo`, never strings from the model.

The log download follows the 302 with a second request on a client without the `Authorization` header (the storage URL is pre-signed); the target host is GitHub's choice, not the model's, so the "no caller-chosen URL" rule holds. .NET drops `Authorization` on cross-host redirects anyway; the explicit second request makes the behavior testable.

Alternative: GitHub's MCP server (option A in the explore notes). Rejected: no structural allowlist, arg-conditional forbidden rules stay visible, large outputs land in context.

### 2. Tools

| Tool | Tier | Untrusted output | Hidden when |
|---|---|---|---|
| `github.prs(repo, state?)` | safe | yes (`github`) | `github` not configured or no repos |
| `github.pr(repo, number)` | safe | yes | same |
| `github.issues(repo, state?, query?)` | safe | yes | same |
| `github.issue(repo, number)` | safe | yes | same |
| `github.runs(repo, branch?)` | safe | yes | same |
| `github.run_log(repo, runId)` | safe | yes | same |

All outputs are untrusted with taint kind `github`: PR/issue text and comments come from other people, run titles are commit messages, logs are produced by code under review. Tools stay visible while GitHub is unreachable.

Storing diffs and logs is a side effect of safe tools. It is not escalated in tainted runs: the artifacts are themselves tainted, private to the owner, and do not drive later runs (unlike memory or tasks). `persists` stays false.

`repo` schema: `{"type":"string","enum":[ids…]}`; the description lists `id = owner/name`. Ids are re-read on every tool-list build, so a config reload updates the enum.

### 3. Artifacts and dedup

- `github.pr`: reference `<id>/pr/<n>@<head sha>`, name `<id>-pr-<n>.diff`.
- `github.run_log`: per failed job, reference `<id>/job/<job id>`, name `<id>-run-<run id>-<job name slug>.log`. Job logs never change after completion, so the job id alone is a stable key.
- Before downloading, look up `FindBySourceAsync(GitHub, reference)`; on a hit, return the id without downloading. On `artifacts.quota_exceeded` / `artifacts.too_large`, return the summary with `"diffStored": false` (or per job `"logStored": false`) and the reason.
- Log tail: the last 50 lines of the downloaded log, with GitHub's timestamp prefixes (`2026-10-09T12:00:00.0000000Z `) removed to save tokens. The file in the artifact store keeps them.
- Failed steps: `steps[]` with `conclusion == "failure"`.

### 4. Configuration

```yaml
github:
  tokenSecret: github_pat     # fine-grained PAT: chosen repos only, read-only Metadata, Contents,
                              # Pull requests, Issues, Actions
  repos:
    marshal: lukas/marshal
    homelab: lukas/homelab
```

Validation in the configuration stage: id pattern, `owner/name` pattern (`^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$`), `tokenSecret` required when `repos` is non-empty.

### 5. Web

`ArtifactSource.GitHub` serializes as `github`; the artifact list's source filter and labels (en/de) get the new value; OpenAPI types are regenerated.

## Risks / Trade-offs

- [Prompt injection through PR/issue text or logs] → All outputs untrusted and tainting; no write tools exist, and later memory/task writes in the run are escalated to confirm by the existing taint rules.
- [Token lifetime: fine-grained PATs expire] → A 401 surfaces as `github.unavailable` with "token rejected or expired"; the secret is replaced in the secret store without restart (secret lookup per request).
- [Huge logs] → The log stream is read once with a rolling 50-line tail, so the tail is always returned; storing fails with `artifacts.too_large` above `artifacts.maxUploadMb` and the result says the log was not stored.
