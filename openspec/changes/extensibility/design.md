## Context

Sources: brainstorm notes §3 (harness, formats, tool naming), §4 (arg-aware rules and target expansion), §7 (HA MCP usage), §8 (area context). `core-agent` provides the registry, visibility filter, risk engine with an empty `IRiskTargetResolver` seam, and the context builder with contributors. HA details (MCP tool names, endpoint, timer intents without device context, websocket commands for registries and exposure) are marked "verify when building".

## Goals / Non-Goals

**Goals:**
- Any MCP server usable as a tool source with risk rules and fail-closed defaults.
- HA home control through HA MCP with entity-level risk decisions.
- Skills and templates as files; subagents.

**Non-Goals:**
- HA events for tasks, announcements, push (background-work); calendar (integrations).
- UI editing of config.

## Decisions

### D1. MCP client
One `McpToolSource` per configured server, built on the `ModelContextProtocol` client (`StdioClientTransport`, `HttpClientTransport` in streamable-HTTP mode). Tools are discovered with `ListToolsAsync` and re-listed on `tools/list_changed`. Each MCP tool becomes an `ITool` whose invocation calls `CallToolAsync`; text content becomes the tool result, other content types are summarized. Stdio servers run as child processes of the container.
*Alternative:* one global MCP manager exposing all tools — rejected, per-server sources make isolation and reload simple.

### D2. HA connection split
`Marshal.Integrations.HomeAssistant` holds the REST client, the websocket client (auth, `subscribe_events` for `state_changed` and registry updates, registry list commands, exposure list) and the caches. HA MCP is just the MCP server `ha` in `/config/mcp/ha.yaml`, authenticated with the same token secret. Marshal system calls never go through MCP.

### D3. Target resolution
`HaTargetResolver` maps HA intent arguments to entities using the cache: `name` (entity name or alias, case-insensitive, per HA matching rules as far as practical), `area` / `floor` (area and floor names or aliases), `domain`, `device_class`. It returns entities with `domain` and `device_class` for rule matching. If the argument schema of an intent is not understood, the call is unresolved (confirm). Default rules shipped in `deploy/config/risk.yaml`: locks, alarm panels, garage / door / gate covers → confirm; other `ha.*` → safe.

### D4. Timers
At startup, inspect the `ha` MCP tool list for timer intents. If present and they work without device context (verify), no `timers.*` tools are registered. Otherwise register `TimerTools` that call HA's intent handling directly (e.g. the `intent/handle` API) with the turn's device id. Verify the endpoint and whether it accepts a device id; if it does not, the last resort is timers as Marshal tasks in `background-work` (wakeup + announce), losing the satellite's native timer UI.

### D5. Skills and templates
`SkillCatalog` parses `SKILL.md` frontmatter with YamlDotNet; `SkillIndexContributor` adds the index. `TemplateCatalog` parses template files; the active template determines tool allowlist, skills subset, model tier, limits and the prompt contributor. `agent.delegate` creates a child execution context (`Depth + 1`, `IsSubagent = true`, same owner / satellite / taint) and calls `IAgentRunner`; its run is recorded as a child run step group within the parent run.

### D6. Interfaces introduced
| Interface | Purpose | v1 implementation |
|---|---|---|
| `IToolSource` (existing) | tools from a source | `McpToolSource`, `SkillToolSource` (for `agent.load_skill`), `TemplateToolSource` (for `agent.delegate`) |
| `IMcpServerCatalog` | configured servers, status, reload | `YamlMcpServerCatalog` |
| `IHomeAssistantClient` | REST + websocket calls | `HomeAssistantClient` |
| `IHomeAssistantCache` | registries, exposure, states | `HomeAssistantCache` |
| `IRiskTargetResolver` (existing) | target expansion | `HaTargetResolver` for namespace `ha` |
| `ISkillCatalog` | skills | `FileSkillCatalog` |
| `ITemplateCatalog` | templates | `FileTemplateCatalog` |
| `ITimeZoneSource` | instance time zone | `HomeAssistantTimeZoneSource` with config override |

### D7. Model-facing tools in this change
| Tool | Tier | Flags | Hidden when |
|---|---|---|---|
| `ha.*` (from HA MCP) | per `risk.yaml`, arg-aware via `HaTargetResolver`; unmatched → confirm | — | server down; `denyTools`; confirm-only under `denyConfirm` |
| other MCP tools | per `risk.yaml`; unmatched → confirm | `untrustedOutput` if server `untrusted` | server down; template allowlist; satellite policy |
| `agent.load_skill(name, file?)` | safe | — | no skills available to the template |
| `agent.delegate(template, task)` | safe | propagates taint | depth limit reached; no delegable template |
| `timers.start`, `timers.cancel`, `timers.status` | safe | — | HA MCP offers usable timer intents; turn has no device |

## Risks / Trade-offs

- [HA intent argument semantics differ from our resolver] → unknown shapes resolve to confirm; tests use recorded HA MCP tool schemas.
- [Stdio MCP servers run inside the Marshal container] → operator's choice; documented; secrets via env of the child process from `ISecretStore`.
- [Registry cache stale during reconnect] → resolver returns unresolved (confirm) until reload completes.
- [Large MCP tool lists cost tokens] → template allowlists; `GetLiveContext` not called automatically.

## Migration Plan

Add `ha:` section and `/config/mcp/ha.yaml`; ship default `risk.yaml`. No data migration.

## Open Questions

- HA MCP endpoint path and tool names on the current HA version.
- Whether timer intents work over HA MCP without a device.

## Implementation Notes

### Verification (task 1.1, 1.2)

Verified against Home Assistant 2026.9.4: its source, and a local test instance (demo integration, a template
lock, floors / areas / aliases, `mcp_server` with the Assist API). The recorded traffic is in
`tests/Marshal.Integrations.HomeAssistant.Tests/Fixtures/` (`websocket-session.json`, `mcp-session.json`,
`rest-session.json`; the access token is replaced by `<token>`).

- **MCP endpoint.** `mcp_server` serves streamable HTTP at `POST /api/mcp` (stateless: every request gets a
  plain `application/json` JSON-RPC response, notifications get 202, no session id) with the HA token as
  `Authorization: Bearer`; without it the answer is 401. `/api/mcp/<llm api id>` selects one LLM API (admin only
  except `assist`). The legacy SSE transport (`/mcp_server/sse`) is not used. Protocol version `2025-06-18`,
  `tools.listChanged = false` (the tool list changes only between requests; Marshal re-lists on reconnect / reload).
- **Tool names are domain-prefixed.** HA 2026.9 reports `<domain>__<Intent>`: `intent__HassTurnOn`,
  `intent__HassTurnOff`, `intent__HassSetPosition`, `intent__HassCancelAllTimers`, `homeassistant__GetLiveContext`,
  `light__HassLightSet`, `climate__HassClimateSetTemperature`, `todo__get_items`, `llm__GetDateTime`, … The
  names are kept as reported (decided with the user): ids `ha.intent__HassTurnOn`, model-facing names
  `ha_intent__HassTurnOn`. The delta specs' examples were updated accordingly; default risk rules use these ids.
- **Intent arguments.** Targeting tools take `name`, `area`, `floor` (strings), `domain` (array) and
  `device_class` (array, enum). `intent__HassTurnOff {area: "Hall"}` turned off both hall lights, **unlocked the
  hall lock and closed the garage door** in one call (recorded), which is why the resolver must expand areas to
  every exposed entity. A failed match returns `isError: true` with a `MatchFailedError` text; success returns the
  intent response JSON (`data.success` lists the affected entities).
- **Websocket commands** (all require an admin token for the exposure list): `get_config` (time zone),
  `config/area_registry/list`, `config/floor_registry/list`, `config/device_registry/list`,
  `config/entity_registry/list` (no aliases / device class), `config/entity_registry/get_entries` with
  `entity_ids` (extended entries with `aliases`, `device_class`, `original_device_class`),
  `homeassistant/expose_entity/list` (`{exposed_entities: {entity_id: {conversation: true}}}`), `get_states`,
  `subscribe_events` for `state_changed`, `entity_registry_updated`, `device_registry_updated`,
  `area_registry_updated`, `floor_registry_updated`. Area and floor entries carry `aliases`; areas carry
  `floor_id`. Entities without a unique id (e.g. demo locks) have no registry entry and can have no area.
- **Timers.** Timer intents (`HassStartTimer`, `HassCancelTimer`, `HassTimerStatus`, …) are offered to an LLM only
  when its context has a device that supports timers; `mcp_server` always uses `device_id = None`, so HA MCP never
  offers them (only `intent__HassCancelAllTimers`). The fallback `timers.*` tools are therefore always needed.
  `POST /api/intent/handle` accepts `name`, `data` (slot values), `language`, `device_id`, `satellite_id`; slots of
  `HassStartTimer` are `hours` / `minutes` / `seconds` (at least one), `name`, `conversation_command`. A device
  without a registered timer handler (anything that is not an Assist satellite) answers `response_type: error`,
  "Device does not support timers".
- **Instance time zone.** `get_config` / `GET /api/config` return `time_zone` (IANA id).
