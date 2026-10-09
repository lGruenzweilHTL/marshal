# Tasks

## 1. Project and client

- [ ] 1.1 Create `src/Marshal.Integrations.GitHub` and `tests/Marshal.Integrations.GitHub.Tests`, add both to `Marshal.sln`, reference the project from `Marshal.Api`; verify `dotnet build` succeeds
- [ ] 1.2 Write failing tests for `GitHubOptions` validation (id pattern, `owner/name` pattern, required token secret with repos); implement and register in the configuration stage; verify with the GitHub tests
- [ ] 1.3 Write failing tests for `GitHubRestClient` against an in-process fake GitHub server (headers incl. pinned API version, PR/issue/comment/run/job parsing, issues without PRs, diff via `application/vnd.github.diff`, log 302 followed without `Authorization`, 404 → `github.not_found`, 401 and rate limit → `github.unavailable` with reset time, only `GET` sent); implement `IGitHubClient`; verify tests pass

## 2. Artifact support

- [ ] 2.1 Write failing tests for `ArtifactSource.GitHub` (stored and listed, `tainted`) and `IArtifactStore.FindBySourceAsync` (owner-scoped, null for another owner); implement in Abstractions and `FileArtifactStore`; verify with `dotnet test --project tests/Marshal.Artifacts.Tests`
- [ ] 2.2 Add `github` to the web artifact source filter and labels (en/de), regenerate `web/openapi/v1.json` and `web/src/api/schema.d.ts`; verify `npm test` and `npm run build` in `web/`

## 3. Tools

- [ ] 3.1 Write failing tests for the repo enum and allowlist (schema enum of ids, unknown id → `github.invalid_arguments` with no client call, descriptors safe and untrusted with taint kind `github`, no tools without config)
- [ ] 3.2 Write failing tests for `github.prs`, `github.issues` (state, title filter, limits) and `github.issue` (body and comment cuts) with a fake `IGitHubClient`; implement; verify tests pass
- [ ] 3.3 Write failing tests for `github.pr` (summary, diff artifact named and referenced per head SHA, reuse on same SHA, new artifact on new SHA, quota exceeded → summary with `diffStored: false`); implement; verify tests pass
- [ ] 3.4 Write failing tests for `github.runs` and `github.run_log` (failed jobs only, failed step names, 50-line tail without timestamp prefixes, log artifact per job and reuse, too large → `logStored: false`); implement; verify tests pass
- [ ] 3.5 Write an API test: a turn with a fake chat client calling `github_pr` stores the diff artifact, which appears in `artifacts_list` and taints the run; verify with `dotnet test --project tests/Marshal.Api.Tests`

## 4. Documentation

- [ ] 4.1 Add the `github` section with token permissions to `deploy/config.example/marshal.yaml`; verify `ExampleConfigTests` passes
- [ ] 4.2 Update the CLAUDE.md repo map and `docs/diagrams/packages.puml` (new project, reference from `Marshal.Api`), re-render the SVG; verify the SVG renders

## 5. Integration checks

- [ ] 5.1 Run the full .NET and web test suites and `openspec validate github --strict`; all pass
