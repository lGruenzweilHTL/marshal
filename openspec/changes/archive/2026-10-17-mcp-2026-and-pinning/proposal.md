# Proposal

## Why

The MCP specification revision 2026-07-28 (implemented by the `ModelContextProtocol.Core` 2.x SDK Marshal already uses) adds ways for servers to ask the human for input (elicitation through multi round-trip requests) and to run long operations (the tasks extension). Marshal supports neither, so servers that need them fail or degrade. MCP servers can also change their tool names, descriptions and schemas at any time; a changed description is an injection channel into every prompt ("rug pull" / tool poisoning, part of the supply-chain risk ASI04 in the OWASP Top 10 for Agentic Applications 2026), and Marshal accepts such changes silently.

## What Changes

- **Protocol:** Marshal's MCP client speaks 2026-07-28 and stays compatible with servers on 2025-06-18 and 2025-11-25 (e.g. Home Assistant's server). It honors cacheable list results (`ttlMs`) when refreshing tool lists.
- **Tool pinning:** Marshal stores a fingerprint of every MCP tool (name, description, input and output schema, annotations). On a server's first successful discovery all tools are trusted (trust on first use). Later, a new or changed tool is hidden from the model and blocked by the guard until an admin approves it in the configuration view, which shows the difference. Unchanged tools keep working. `pinTools: false` in a server file turns pinning off for that server.
- **Health warnings everywhere:** pending tool approvals and other health warnings appear as a banner on every web page and through a new safe tool `system.health`, so a briefing task can mention them. `system` becomes a reserved namespace.
- **Elicitation (web UI only):** when a server answers a tool call with an input request (form or URL), Marshal freezes the call and the request as a pending input request and shows it to the user in the web UI. It never passes the request to the model and never lets the model answer. After the user answers, the server repeats the frozen call with the answer and delivers the result like an approved action. Voice and background runs tell the user to answer in the web UI and send a push with a link.
- **Server tasks:** when a server returns a task handle, Marshal creates a waiting agent task that is resumed when the server reports the task finished (polling `tasks/get`), and delivers the result through the normal task delivery path.

## Capabilities

### New Capabilities

- `system-health`: aggregated health warnings (codes, components, Marshal-authored messages), the `system.health` tool, and live updates.

### Modified Capabilities

- `mcp-client`: protocol versions, `pinTools` configuration, tool pinning, elicitation, server tasks.
- `approval-queue`: pending input requests next to pending actions.
- `web-shell`: health banner.
- `config-view`: MCP tool approvals (the view's only write action).
- `data-ownership`: tool pins are instance-scoped rows.
- `tool-registry`: `system` is reserved; tools awaiting pin approval are hidden.

## Non-goals

- MCP sampling, roots and logging (deprecated in 2026-07-28).
- Voice answers to elicitation forms.
- Pinning built-in tools or skills (they are trusted configuration or code).
- Showing MCP Apps UIs (separate change).
- Marshal acting as an MCP server (separate change).

## Impact

- `src/Marshal.Core/Mcp`: client options (capabilities, extensions), fingerprinting, pin store, input requests, task handles.
- `src/Marshal.Data`: tables for tool pins and pending input requests; migration; `erd.puml`.
- `src/Marshal.Tasks`: pending input requests in the approval queue; waiting tasks for server task handles; MCP task poller.
- `src/Marshal.Api`: system health aggregation behind a Core interface, approval and input request endpoints, config view approval endpoint.
- `web/`: health banner, input request form in chat and review queue, pin approval diff in the config view.
- Docs: MCP guide (pinning, elicitation, tasks), briefing example using `system.health`.
