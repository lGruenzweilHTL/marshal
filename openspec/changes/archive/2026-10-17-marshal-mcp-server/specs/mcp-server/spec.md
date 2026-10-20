# Spec Delta

## Purpose

Lets external AI clients use Marshal over the Model Context Protocol: recall memory, search notes, see and create tasks, and ask Marshal to act with its own tools under its own guard.

## ADDED Requirements

### Requirement: MCP endpoint
The system SHALL serve an MCP server at `/mcp` over streamable HTTP, stateless, supporting protocol revision 2026-07-28 and the revisions 2025-11-25 and 2025-06-18. Every request SHALL be authenticated with a machine token (`Authorization: Bearer`) that carries `mcp:read` or `mcp:write`; otherwise the response SHALL be 401 (missing, revoked or expired token) or 403 (no MCP scope). Cookie principals SHALL NOT use the endpoint. The server SHALL offer tools only, no resources, prompts, sampling or elicitation.

#### Scenario: Token without MCP scope
- **WHEN** a token with only `conversation:write` calls `/mcp`
- **THEN** the response is 403

#### Scenario: Older client
- **WHEN** a client speaking 2025-06-18 connects with a valid token
- **THEN** it can list and call the tools its scopes allow

### Requirement: Exposed tools
The tool list SHALL contain only tools the token's scopes allow: with `mcp:read` `memory_recall`, `notes_search`, `notes_read`, `tasks_list` and `tasks_status`; with `mcp:write` additionally `tasks_create` and `ask_marshal`. A tool whose Marshal capability is not configured (e.g. notes) SHALL NOT be listed. Each call SHALL run with the token owner's execution context (source `api`, non-interactive) through Marshal's registry and guard with the same arguments and validation as the internal tool, and SHALL be recorded as a run with trigger `mcp:<tool>`. A tool the token's scopes do not allow SHALL fail as unknown.

#### Scenario: Read-only token
- **WHEN** a token with only `mcp:read` lists tools
- **THEN** the list has the five read tools and neither `tasks_create` nor `ask_marshal`

#### Scenario: Recorded call
- **WHEN** a client calls `memory_recall`
- **THEN** a run with source `api` and trigger `mcp:memory_recall` exists for the token owner

#### Scenario: Other owner's data
- **WHEN** a token owned by A calls `tasks_list`
- **THEN** only A's tasks are returned

### Requirement: Ask Marshal
`ask_marshal(text, conversationId?, language?)` SHALL run one Marshal turn for the token owner with the default template, text output mode, and the reply language from `language` or the owner's preference, and SHALL return the reply text and the conversation id. Calls with the same `conversationId` from the same token SHALL continue one session (looked up by token and id); without it, each call SHALL start a new session. The turn SHALL start untainted. It SHALL be non-interactive: confirm-tier calls and taint escalations SHALL create pending actions in the approval queue, the turn SHALL continue with a tool result saying the action waits for approval, and no later `ask_marshal` input SHALL confirm a pending action. The turn SHALL be limited by the latency cap of API turns and SHALL count against the global daily cost cap.

#### Scenario: Ask to switch a light
- **WHEN** a client calls `ask_marshal` with "turn off the office light"
- **THEN** Marshal calls the HA tool under its guard and returns its reply

#### Scenario: Confirm-tier action
- **WHEN** a client asks Marshal to start a print
- **THEN** a pending action appears in the approval queue, the reply says it waits for approval, and the print does not start

#### Scenario: Yes from the client
- **WHEN** the client then calls `ask_marshal` with "yes" and the same `conversationId`
- **THEN** the pending action stays pending

#### Scenario: Cost cap
- **WHEN** the daily cost cap is reached
- **THEN** `ask_marshal` fails with an error saying the cap is reached and no model is called
