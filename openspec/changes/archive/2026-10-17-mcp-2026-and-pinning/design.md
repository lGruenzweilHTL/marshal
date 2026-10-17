# Design

## Context

See proposal.md (Why). Current state:

- `McpToolSource` keeps one connection per server with reconnect and backoff, re-lists on `tools/list_changed`, and throws from `GetToolsAsync` while down so the registry drops the source for that refresh.
- `ModelContextProtocol.Core` 2.2.0 implements 2026-07-28 (stateless core, `server/discover`, MRTR, extensions framework) and negotiates older revisions.
- `/health` is built from ASP.NET health checks in `Marshal.Api/Health` (database, secrets, config, memory, HA, MCP). There is no Core-level view of warnings that a tool could read.
- The approval queue freezes pending actions (tool, args, nonce, TTL), executes them server-side, and resumes suspended tasks with the decision.

## Goals / Non-Goals

**Goals:**
- No MCP tool definition reaches a prompt in a form an admin did not accept, after the first discovery.
- Server-initiated human input never passes through the model.
- Server tasks reuse Marshal's durable tasks and deliveries.

**Non-Goals:**
- Pinning other servers' resources or prompts (Marshal uses tools only).

## Decisions

### 1. Fingerprints and the pin store

`IMcpToolPins` (`Marshal.Abstractions`): `GetAsync(serverId)`, `PinAsync(serverId, tool, fingerprint, state)`, `DecideAsync(serverId, toolName, approve, actor)`. v1: `EfMcpToolPins` on a table `mcp_tool_pins (server_id, tool_name, fingerprint, state: approved | pending | rejected, approved_fingerprint, previous_definition_json, current_definition_json, first_seen, decided_at, decided_by)`; `approved_fingerprint` lets a tool that changes back to its approved definition work again without a decision. No owner column (instance configuration, see the data-ownership delta).

Fingerprint: SHA-256 over canonical JSON (sorted keys, no insignificant whitespace) of `name`, `title`, `description`, `inputSchema`, `outputSchema`, `annotations`. `title` is included because it reaches the prompt when a tool has no description. `McpToolSource` compares after each discovery and marks tools; the visibility filter and the guard read the mark from the tool (`ITool` gains a `PinState`), so the filter and the guard keep one rule.

Alternative: store pins in a file under `/data` — no migration, but approvals need atomic writes and auditing, which the DB already gives.

### 2. Health warnings in Core

`ISystemHealth` (`Marshal.Abstractions`): `GetWarningsAsync() → IReadOnlyList<HealthWarning>` and a change notification. v1: `AggregatedSystemHealth` in `Marshal.Api`, built from the existing health checks (mapped to codes and Marshal-authored messages) plus warning providers (`IHealthWarningProvider`) that Core components register (MCP sources, pin store). External error texts stay in logs and the config view, never in warnings, because `system.health` output is trusted.

| Tool | Tier | Hidden when |
|---|---|---|
| `system.health()` | safe | no warnings |

`system` joins the reserved namespaces. In the tool retrieval change's core list it is not core; briefing templates allow it by name.

### 3. Elicitation through MRTR

Finding (task 4.1, `ModelContextProtocol.Core` 2.2.0, in-process probe): the SDK never surfaces a raw `InputRequiredResult` to `CallToolAsync`. On 2026-07-28 its MRTR interceptor calls `McpClientHandlers.ElicitationHandler` in the caller's async flow and retries `tools/call` itself with `inputResponses` and `requestState`; without a handler it throws. On 2025-11-25 and older the server sends a classic `elicitation/create` request on the connection, answered by the same handler outside the caller's async flow. So Marshal's handler never waits for a person: it records the request and throws, which ends the call (2026-07-28: the exception reaches `CallToolAsync`; older revisions: the server reports the tool call failed). The handler finds its call through an `AsyncLocal` call scope, or through the only call in flight on that connection (older revisions); with several calls in flight on an older server it declines. Answering repeats the frozen call with the stored answers: the handler answers the server's input request from them, and the SDK sends `inputResponses` and the server's fresh `requestState`. Marshal therefore stores no `requestState`; the server gets the same answers in the protocol's own retry. A request the stored answers do not match (other mode, message or schema) is a chained request and becomes a new pending input request. Either way, `McpToolSource` turns it into a `PendingInputRequest` (frozen: server, tool, args, inputRequests, requestState, nonce, TTL) and returns a fixed tool result to the model ("The server needs input from the user; it was asked in the web UI."). The client declares elicitation support (form and URL), so servers send requests instead of failing.

Answering: `POST /api/v1/approvals/inputs/{id}` with the form answer (validated against the requested schema) or `done` for URL mode. The server-side executor repeats `tools/call` once; the SDK sends `inputResponses` and `requestState` in its retry. A second `input_required` creates a new request (chained). The result is delivered like an approved action: web sessions get a background result in place, tasks resume.

New interface: `IInputRequestQueue` (`Marshal.Abstractions`), v1 `EfInputRequestQueue`, kept next to the approval queue in `Marshal.Tasks` and sharing its TTL, notification and resume code.

### 4. Server tasks

When a tool call returns a task handle, `McpToolSource` asks `IMcpTaskTracker` (v1 `PollingMcpTaskTracker`, a hosted service in `Marshal.Tasks`) to track `(server, taskId)`. The tracker creates a Marshal agent task in status `waiting` (`AwaitsResume`, like input requests and executor sessions) with the origin session and satellite as delivery target, and stores the handle in `mcp_task_handles` (owner, server, task id, tool, marshal task id, taint kind, poll interval, polls, next poll), so handles survive restarts. It polls `tasks/get` through `IMcpTaskClient` (implemented by `McpToolSources`). When the server task ends, it publishes `mcp.task_finished` (ids and status only) on the event bus and resumes the waiting task directly with the outcome (`TaskResume`, payload key `mcpTask`, plus `taint` when the tool is untrusted), the same path as executor outcomes; the trigger matcher is not involved. Cancel: a poll that finds the waiting task cancelled calls `tasks/cancel` and drops the handle. Without a tracker, or beyond the per-owner cap, the server task is cancelled at once and the tool result says why.

Polling: the wait doubles from 5 seconds up to 5 minutes, and is never shorter than the server's latest `pollIntervalMs`. A server that is down keeps the handle and backs off; a server that no longer knows the task (`-32602`) ends it as failed.

Finding (task 5.1, `ModelContextProtocol.Core` 2.2.0, in-process probe): the SDK does not model the tasks extension. Its typed `CallToolAsync` keeps `resultType: "task"` but drops `taskId` and `pollIntervalMs`. `McpToolSource` therefore sends `tools/call` through the session's generic `SendRequestAsync` with its own result type (`McpTaskProtocol.CallResult`, both shapes); the MRTR interceptor still applies on that path (the elicitation tests pass on 2026-07-28, 2025-11-25 and 2025-06-18). `tasks/get` and `tasks/cancel` use the same generic call with the wire shapes of the extension spec (SEP-2663).

### 5. Banner

The SPA reads `GET /api/v1/system/health` on load and subscribes to the `health` topic. Dismissal is stored per browser (`localStorage`, keyed by the sorted warning codes); this is a per-viewer convenience, so browser storage is fine.

## Risks / Trade-offs

- [HA's tool list changes when entities are exposed, causing approval noise] → `pinTools: false` per server; the example `ha.yaml` explains the trade-off. TOFU means a fresh install has nothing to approve.
- [A malicious server is already malicious at first discovery] → pinning protects against later changes only; documented. `untrusted` remains the tool for servers whose output is third-party data.
- [SDK surfaces MRTR differently than expected] → the first task checks it; decision 3 covers both shapes.
- [Many server tasks polling] → backoff to 5 minutes; handles cap per owner (50), beyond that the tool result says the server task limit is reached.

## Migration Plan

EF migration adds `mcp_tool_pins`, `pending_input_requests` and `mcp_task_handles`. On first start after the upgrade, every server counts as first discovery (no pins), so existing tools are pinned as approved without prompts. Rollback: older versions ignore the tables.
