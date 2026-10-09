# Proposal

## Why

Marshal cannot answer "why did CI fail on marshal?", "what's in PR 42?" or "any new issues on homelab?". GitHub's own MCP server could be configured, but it takes free `owner/repo` strings (no structural allowlist), keeps arg-conditional rules visible to the model, and puts 100 KB logs and diffs straight into the context instead of the artifact store.

## What Changes

- New read-only GitHub integration with a fine-grained personal access token (`github.tokenSecret`) and a repo allowlist in config (`github.repos`: id → `owner/name`). Tools take a repo **id** from an enum of configured ids; other repos are invisible.
- New tools in the capability namespace `repos` (built-in namespaces name capabilities, never providers), all safe: `repos.prs`, `repos.pr`, `repos.issues`, `repos.issue`, `repos.runs`, `repos.run_log`. Every tool's output is untrusted (titles, bodies, comments, commit messages and logs are written by other people or by code under review) and taints the run with source `github`.
- `repos.pr` stores the pull request's diff and `repos.run_log` stores failed jobs' logs as tainted artifacts (source `github`, kinds `diff` and `code`) and returns only a summary plus the artifact id, so `artifacts.show` can open them. Repeated calls for the same commit reuse the artifact.
- No write paths: no comments, reviews, merges, issue edits, workflow dispatch or reruns.

## Capabilities

### New Capabilities

- `github`: GitHub configuration, the read-only client, the `repos.*` tools and their artifacts.

### Modified Capabilities

- `artifacts`: new artifact source `github` and its ingest path.
- `tool-registry`: `repos` joins the reserved namespaces.

## Non-goals

- Writing to GitHub (comments, reviews, merges) and a GitHub App identity; a later change.
- GitHub events (webhooks or polling) and watches on CI results.
- GitHub Enterprise Server URLs.
- Code search and reading arbitrary files from repos.
- Sharing the executor's git token; Marshal's token is separate.

## Impact

- New project `src/Marshal.Integrations.GitHub` with `tests/Marshal.Integrations.GitHub.Tests`; referenced by `Marshal.Api`.
- `Marshal.Abstractions`: `ArtifactSource.GitHub`; artifact store ingest for integration sources (dedup by source reference).
- Web: artifact source filter gains `github`; regenerated OpenAPI types.
- `deploy/config.example/marshal.yaml`: `github` section with the token's required permissions.
- CLAUDE.md repo map and `docs/diagrams/packages.puml`.
