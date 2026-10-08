# Proposal

## Why

Home Assistant's MCP server exposes to-do lists (`HassListAddItem`, `HassListCompleteItem`, `HassListRemoveItem` and a read tool `todo__get_items`), and the example `risk.yaml` already makes every `ha.*` tool safe, so shopping lists work by voice without new code. But lists synced from other apps (Bring, Todoist, Google Tasks, CalDAV) hold text other people typed, and reading them is a prompt-injection path. Marshal can only mark a whole MCP server `untrusted`; doing that for `ha` would taint every home-control turn. A server needs a way to mark only some of its tools as returning third-party content. The same need will come back for other servers (e.g. issue text from a GitHub server).

## What Changes

- MCP server files gain `untrustedTools`: a list of globs on the tool name as the server reports it (e.g. `todo__get_items`, `*get_items`). Matching tools get `untrustedOutput` and taint the run with source `mcp:<id>`; other tools of the server stay trusted.
- `untrusted: true` keeps its meaning (every tool of the server); with it, `untrustedTools` is redundant and SHALL be rejected as a config error, so a file never says two different things.
- A glob that matches none of the server's tools is reported as a warning on the config view and `/health` (likely a typo or a renamed tool), not an error.
- The example `deploy/config.example/mcp/ha.yaml` sets `untrustedTools: ["todo__get_items"]` with a comment explaining why.
- The config API and view show the list per server.
- Removing a to-do item stays safe under the existing `ha.*` rule (decided; no new risk rule).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `mcp-client`: the "MCP server configuration" requirement gains `untrustedTools`; the "Untrusted servers taint runs" requirement covers per-tool marking and the conflict with `untrusted: true`.

## Non-goals

- A Marshal-built `lists.*` tool or any to-do code; HA's tools are used as they are.
- Per-tool trust for built-in tools (they declare it in code already).
- Marking only some fields of a tool result as untrusted.
- Changing the example risk rules for `ha.*`.

## Impact

- `src/Marshal.Core/Mcp/McpServerFile.cs`, `YamlMcpServerCatalog.cs` (validation), `McpServerDefinition`, `McpToolSource.cs` (`Describe`), `McpToolSources.SameDefinition` (hot reload compares the list).
- `src/Marshal.Api/Configuration/ConfigEndpoints.cs`: additive `untrustedTools` field on the MCP server response (v1, additive); `web/src/config/ConfigView.vue` shows it.
- `deploy/config.example/mcp/ha.yaml`.
- Tests in `tests/Marshal.Core.Tests` (catalog validation, taint per tool, hot reload) and `tests/Marshal.Api.Tests` / web component tests for the config view.
- No database or dependency changes.
