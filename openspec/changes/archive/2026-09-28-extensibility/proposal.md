## Why

After `core-agent` Marshal can talk but has almost no tools. Home control, timers and every user-specific behavior come from extensions: Home Assistant's MCP server, custom MCP servers, skills and agent templates. This change (sub-project 4 of 7) makes Marshal extensible without touching code and connects it to Home Assistant, which also gives the risk engine its first real target resolver.

## What Changes

- MCP client (official `ModelContextProtocol` SDK) for servers listed in `/config/mcp/` (stdio and streamable HTTP), hot-reloaded, with flat namespaces (server id or `toolPrefix`), collision handling, risk rules and an `untrusted` flag.
- Home Assistant client: REST + websocket with a long-lived token, cached registries (entities, devices, areas, floors, exposure), live state cache, HA as MCP server `ha`.
- HA target resolver for arg-aware risk rules (name, area, floor, domain, device class → exposed entities).
- Context contributor with current-area entity states (cached `GetLiveContext` data) instead of full dumps.
- Timers: HA timer intents via MCP when offered; otherwise `timers.*` tools calling HA intent handling with the turn's device.
- Skills in Anthropic Agent Skills format (`/config/skills/<name>/SKILL.md`), skill index in context, `agent.load_skill`.
- Agent templates (`/config/templates/*.md`): `name`, `description`, `modelTier`, `tools` allowlist, `skills`, prompt body; subagents via `agent.delegate(template, task)`.
- Config view in the web UI (read-only): skills, templates, MCP servers, risk rules, models, satellites.
- Instance time zone taken from HA when `instance.timeZone` is not set.

## Non-goals

- No HA event bus integration for tasks (state changes as trigger events come in `background-work`); the websocket here only keeps caches fresh.
- No calendar tools (`integrations`), no HA-based notifications or announcements (`background-work`).
- No config editing in the web UI; `/config` stays file-based.
- No MCP server hosting by Marshal (client only).
- No skill marketplace or remote skill installation.

## Capabilities

### New Capabilities
- `mcp-client`: MCP server config, transports, tool discovery, namespaces, reload, failure isolation.
- `home-assistant`: HA connection, registries and state cache, target resolver, area context, timers, time zone.
- `skills`: skill format, index, progressive loading.
- `agent-templates`: template format, template selection, allowlists, model tier, subagents and their limits.
- `config-view`: read-only web view of `/config`.

### Modified Capabilities
- `tool-registry` (introduced by `core-agent`): adds external tool sources (MCP, skills) and default tier for unmatched external tools.

## Impact

- New code: `src/Marshal.Integrations.HomeAssistant`, MCP and skills / templates in `Marshal.Core`, config view in `web/`.
- New dependency: `ModelContextProtocol`.
- New config: `/config/mcp/*.yaml`, `/config/skills/`, `/config/templates/`, `ha:` section in `marshal.yaml` (`url`, `tokenSecret`).
- Depends on `core-agent` and `auth-web-shell`.
