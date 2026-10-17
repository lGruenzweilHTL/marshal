# Tasks

## 1. Package, scopes and endpoint

- [ ] 1.1 Add `ModelContextProtocol.AspNetCore` to `Directory.Packages.props` and `Marshal.Api`, the `CLAUDE.md` package index and `packages.puml` (re-render the SVG). Verify the solution builds.
- [ ] 1.2 Write failing tests: `ApiScopes.All` contains `mcp:read` and `mcp:write`; the `user` role grants both; the token dialog API accepts them. Implement. Verify the tests pass.
- [ ] 1.3 Write failing API integration tests for `/mcp`: 401 without or with a revoked token; 403 with a token without MCP scopes; 403 for cookie principals; a 2026-07-28 client and a 2025-06-18 client can list tools; no resources, prompts, sampling or elicitation capabilities. Map the endpoint with the `mcp` policy. Verify the tests pass.

## 2. Tool adapter

- [ ] 2.1 Write failing tests that the agent runner's per-call path still records, guards and executes identically after extracting `IToolCallPipeline` (existing runner tests stay green, plus pipeline unit tests). Extract `GuardedToolCallPipeline`. Verify all runner and guard tests pass, including the guard branch-coverage check.
- [ ] 2.2 Write failing tests for `McpServerToolCatalog`: scope-filtered list; unconfigured capabilities not listed; descriptions and schemas equal the Marshal descriptors; disallowed tools fail as unknown; calls run with the token owner's context, are recorded as runs with trigger `mcp:<tool>`, and return owner-scoped data only. Implement. Verify the tests pass.

## 3. Ask Marshal

- [ ] 3.1 Write failing tests with `FakeChatClient`: `ask_marshal` runs a turn with the default template and returns reply and conversation id; same `conversationId` continues the session per token; new session without it; untainted start; a confirm-tier call creates an approval-queue entry and the reply says so; a later "yes" does not confirm; latency cap applies. Implement `IAskMarshalRunner`. Verify the tests pass.
- [ ] 3.2 Write failing tests that runs with trigger `mcp:*` count against the daily cap and that over the cap tools return an MCP tool error and make no model call. Implement. Verify the tests pass.

## 4. Web UI and docs

- [ ] 4.1 Write failing component tests for the new scopes and their explanations in the token creation dialog; implement with German and English strings. Verify `npm test` passes.
- [ ] 4.2 Write a guide for connecting Claude Code and VS Code to Marshal's MCP server (token creation, config snippets, what each scope exposes, privacy and injection notes, OAuth limitation). Verify the page follows `docs/development/docs-style.md`.

## 5. Integration check

- [ ] 5.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate marshal-mcp-server --strict`. Verify all pass.
