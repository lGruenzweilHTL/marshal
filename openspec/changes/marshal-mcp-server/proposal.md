# Proposal

## Why

Other AI clients the user works with (Claude Code, VS Code, Cursor) know nothing about what Marshal remembers, the user's notes or Marshal's tasks, and cannot hand work to Marshal. Home Assistant 2026.10 made its own MCP server a one-click feature for the same reason. With Marshal as an MCP server, an external agent can recall Marshal's memory, search notes, see and create tasks, and ask Marshal to do something with its own tools under its own guard.

## What Changes

- New MCP endpoint `/mcp` (streamable HTTP, stateless, MCP 2026-07-28 with fallback to older revisions), authenticated with existing machine tokens (`Authorization: Bearer`).
- New scopes `mcp:read` and `mcp:write`. A client sees only the tools its token's scopes allow:
  - `mcp:read`: `memory_recall`, `notes_search`, `notes_read`, `tasks_list`, `tasks_status`.
  - `mcp:write`: `tasks_create` and `ask_marshal(text, conversationId?, language?)`.
- Exposed tools run through Marshal's tool registry and guard with the token owner's execution context, and each call is recorded as a run (source `api`, trigger `mcp:<tool>`).
- `ask_marshal` runs a full Marshal turn (default template, Marshal's own tools and guard) for the token owner and returns the reply. Its input starts untainted, like an API turn. Nobody can confirm inside it: confirm-tier calls and taint escalations go to the approval queue, and the reply says the action waits for approval. A reply from the external agent never confirms anything. Turns with the same `conversationId` continue one session.
- MCP runs count against the global daily cost cap.

## Capabilities

### New Capabilities

- `mcp-server`: the endpoint, authentication and scopes, the exposed tools, `ask_marshal`, recording and limits.

### Modified Capabilities

- `conversation-api`: `/mcp` is an unversioned endpoint.

## Non-goals

- OAuth for MCP clients (claude.ai or ChatGPT connectors). Bearer tokens only.
- Exposing home control, mail, printer or other integration tools directly; external agents reach them only through `ask_marshal`.
- Deciding approvals over MCP.
- Resources, prompts, MCP Apps or elicitation on Marshal's server side.
- Starting `ask_marshal` runs tainted.

## Impact

- New package `ModelContextProtocol.AspNetCore` (host only, `Marshal.Api`); package index in `CLAUDE.md`; `packages.puml`.
- `src/Marshal.Api`: MCP server setup, token auth on `/mcp`, scope-filtered tool list, adapters to the registry.
- `src/Marshal.Abstractions`: scopes `mcp:read`, `mcp:write`.
- `src/Marshal.Core`: `ask_marshal` turn runner (non-interactive API turn).
- `src/Marshal.Tasks`: daily cap counts MCP runs.
- Web UI: scopes in the token creation dialog.
- Docs: guide for connecting Claude Code / VS Code to Marshal.
