# Design

## Context

See proposal.md (Why). Current state:

- Machine tokens carry scopes; cookie principals get role-derived scopes. `ApiScopes.All` lists known scopes.
- The tool registry, guard and execution context run every tool call; the agent runner runs turns. The `ha-ai-task` change adds approvals for non-interactive runs without a task (queued, executed on approval, result notified).
- `ModelContextProtocol.Core` 2.2 is referenced; the ASP.NET hosting package (`ModelContextProtocol.AspNetCore`) is not. SDK v2.0 made the HTTP server transport stateless by default.
- Sessions from token clients are looked up by `(api_token_id, external_id)`.

## Goals / Non-Goals

**Goals:**
- One authorization path: the MCP server is an adapter over the registry and the guard, never a second implementation of a tool.

**Non-Goals:**
- Per-tool scopes beyond read/write.

## Decisions

### 1. Hosting

`ModelContextProtocol.AspNetCore` in `Marshal.Api`, mapped at `/mcp` with `.RequireAuthorization("mcp")`. The policy accepts token principals with `mcp:read` or `mcp:write`, and rejects cookie principals: the endpoint is for machines, and the SPA's antiforgery and cookie model do not fit it. Stateless transport (the SDK default), so no session affinity is needed and restarts lose nothing.

### 2. Adapter over the registry

`McpServerToolCatalog` (`Marshal.Api`) builds the tool list per request from a fixed mapping:

| MCP tool | Marshal tool | Scope |
|---|---|---|
| `memory_recall` | `memory.recall` | `mcp:read` |
| `notes_search` | `notes.search` | `mcp:read` |
| `notes_read` | `notes.read` | `mcp:read` |
| `tasks_list` | `tasks.list` | `mcp:read` |
| `tasks_status` | `tasks.status` | `mcp:read` |
| `tasks_create` | `tasks.create` | `mcp:write` |
| `ask_marshal` | (turn) | `mcp:write` |

Descriptions and input schemas are taken from the Marshal tool descriptors, so they cannot drift. A call builds the execution context (owner = token owner, source `api`, `IsInteractive = false`, trigger `mcp:<tool>`), checks availability (`IsAvailableAsync`, so unconfigured capabilities are not listed), and executes through `IToolCallPipeline` (guard + execution + recording) as a one-step run without a model call. New interface: `IToolCallPipeline` (`Marshal.Core`), extracted from the agent runner's per-call path; v1: `GuardedToolCallPipeline`. The runner uses the same pipeline, so both paths share one implementation.

`tasks.create` from MCP: the run is untainted and `persists` tools execute; the task gets the template's allowlist as policy (sessionless creation, quarantined extraction change).

### 3. `ask_marshal`

`IAskMarshalRunner` (`Marshal.Core`): `AskAsync(owner, tokenId, text, conversationId?, language?) → Result<AskResult>`. v1 wraps `ConversationService` with source `api`, `inputMode = text`, `IsInteractive = false`, the API latency cap, and no SSE (it collects the final reply). Because the context is non-interactive and has no task, the runner uses the "approvals without a task" path added by the `ha-ai-task` change: pending actions go to the approval queue, never into the session, so a later "yes" in the same session has nothing to confirm. Tests prove it for this path.

### 4. Cost cap

The daily cap query counts runs with source `task`, `automation`, or source `api` and a trigger starting with `mcp:`. Over the cap, tools answer an MCP tool error (`isError`) with a message, not an HTTP error, so clients show it.

### 5. Scopes

`mcp:read`, `mcp:write` added to `ApiScopes.All`; the `user` role grants both (non-admin scopes). The token dialog in the security view lists them with a short explanation.

## Risks / Trade-offs

- [An injected external agent writes tasks or asks Marshal to act] → accepted for an untainted start (user decision); confirm-tier still needs the user in the approval queue; every call is a recorded run; tokens are revocable and scoped. Documented in the guide.
- [Notes and memory content leave Marshal to a cloud model behind the client] → that is the purpose; the guide states it, and `mcp:read` is opt-in per token.
- [Clients that require OAuth cannot connect] → non-goal; documented.

## Migration Plan

No data migration. New scopes appear in token creation; existing tokens are unaffected.
