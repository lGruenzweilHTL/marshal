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

### MCP client (tasks 2.1–2.4)

- **SDK.** `ModelContextProtocol.Core` 2.2 (client and server types; the full `ModelContextProtocol` package only
  adds hosting helpers). Before `initialize` the 2.2 client sends a `server/discover` probe (protocol
  `2026-07-28`); HA 2026.9 answers it with JSON-RPC error `-32602` and the client falls back to `initialize` with
  `2025-06-18`. The probe is recorded in `mcp-session.json`; a smoke run against the test instance listed all 29
  HA tools and called `homeassistant__GetLiveContext`.
- **Server files.** `envSecrets` is a list of `{variable, secret}` pairs rather than a map, because the config
  secret scanner flags map keys such as `SEARCH_API_KEY`. The `ha:` connection details stay in `marshal.yaml`;
  `/config/mcp/ha.yaml` only points at `/api/mcp` with the same token secret.
- **Connection loop.** Each `McpToolSource` runs a background loop: resolve secrets, connect, list tools, then
  wait until the client completes (stdio process exit, closed stream) or a call fails with a transport error
  (the only signal for HA's stateless HTTP endpoint). It then reconnects with exponential backoff (1 s doubling to
  1 min). While a server is down `GetToolsAsync` throws, so the registry leaves its tools out and lists the reason
  in its errors. `tools/list_changed` re-lists in the background. Startup waits for the first attempt only.
- **Descriptors.** MCP tools default to `confirm` (fail-closed, also enforced by the risk engine for every
  non-built-in tool); `untrusted: true` sets `untrustedOutput` with taint kind `mcp:<server id>`. Text content is
  passed verbatim, other content is summarized (`[image: image/png]`, `[resource: name uri]`); `isError` results
  and JSON-RPC errors are failed tool results, transport failures are `mcp.unavailable`.
- **Stdio children** do not inherit Marshal's environment (it may hold `MARSHAL__…` secrets): they get `PATH`,
  `HOME`, `LANG`, `LC_ALL`, `TZ`, `TMPDIR` and their configured `envSecrets` only.

### Registry and reload (tasks 2.5, 2.6)

- **Dynamic sources.** `McpToolSources` implements `IToolSourceProvider` and keeps one source per catalog entry:
  added files start a source, removed files stop it, changed files replace it; unchanged servers keep their
  connection. The registry reads static sources and providers on every refresh, built-in sources first.
- **Per-run snapshot.** `IToolRegistry.RefreshAsync` returns an immutable `ToolSnapshot`. The agent runner
  refreshes at run start and stores the snapshot in `MarshalExecutionContext.Tools`; the visibility filter and the
  guard use it, so a reload during a turn never changes that turn's tools (subagents inherit the snapshot). A
  confirmation in a later turn resolves the frozen tool against the then-current snapshot.
- **Fail-closed default.** The risk engine uses `confirm` as the default tier for every non-built-in tool (a
  stricter descriptor tier stays), independent of what the source's descriptor says.
- **Startup.** Invalid MCP server files are logged and skipped; a literal secret in any of them stops startup,
  like in every other `/config` file. Startup waits up to 10 s for first connection attempts. MCP secret names
  appear in the secrets view (`mcp/<file>: authTokenSecret`).
- **One watcher for `/config`.** Hot-reloaded files (`risk.yaml`, `satellites.yaml`, `mcp/*.yaml`, later skills and
  templates) share one `ConfigDirectoryWatcher` (a single inotify instance per process) instead of one watcher per
  file; the default Linux limit is 128 instances per user.

### Home Assistant client (tasks 3.1–3.11)

- **Project.** `Marshal.Integrations.HomeAssistant` references only `Marshal.Abstractions`. `IAreaDirectory` moved
  from Core to Abstractions so HA can supply area and floor names. The design's `ITimeZoneSource` is the existing
  `IInstanceTimeZone`; `HomeAssistantTimeZoneSource` replaces `ConfiguredInstanceTimeZone`.
- **Always registered, inert without `ha.url`.** Test configuration (and any late configuration source) arrives
  after service registration, so the integration registers unconditionally: without `ha.url` the cache never
  connects, the timer source offers no tools, the area directory falls back to ids, the time zone comes from
  config or UTC and `/health` reports `homeassistant: not configured`. With `ha.url` set, `ha.tokenSecret` is
  required and must resolve at startup.
- **Cache.** One websocket session: subscribe (`state_changed` and the four `*_registry_updated` events) before
  loading, so nothing is lost; states carry `last_updated`, and an older event never overwrites a newer state.
  Registry events and a 5-minute timer reload the registries (exposure changes have no event). While
  disconnected or reloading after a reconnect `Current` is null; the resolver and the context contributor treat
  that as unknown (confirm / no section). Entity name = registry name, else friendly name, else original name.
  HA 2026.9 lists `null` in `aliases` for "the entity's own name"; it is skipped.
- **Resolver.** Only exposed entities; every targeting argument narrows the set (like HA's matching). A call
  without targeting arguments yields one attribute-less target, so rules with target conditions do not match and
  `ha.*: safe` applies (e.g. `GetLiveContext`, `HassBroadcast`); anything unresolvable is `confirm`. Targets carry
  `domain`, `deviceClass` and `area`.
- **Timers.** `timers.*` is a separate built-in tool source; tools need the turn's `DeviceId` (now part of the
  execution context) and are hidden if HA MCP ever offers `HassStartTimer`. Durations: `10m`, `1h30m`, `90s`,
  `25 minutes` or ISO 8601 `PT1H30M`, passed as HA's `hours` / `minutes` / `seconds` slots. REST intent calls are
  never retried (a retry could start a second timer).
- **Smoke test.** The cache ran only against the recorded traffic of the HA 2026.9.4 test instance; a live run
  was not possible because the instance stopped finishing its bootstrap (no HTTP server, no error logged).
