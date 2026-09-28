## 1. Verification

- [x] 1.1 Verify against current HA docs: MCP server endpoint and tool list, websocket commands for entity / device / area / floor registries and exposed entities, timer intents without device context; record in `design.md`
- [x] 1.2 Record HA MCP and websocket traffic from a test instance as fixtures for fake servers

## 2. MCP client

- [x] 2.1 Write catalog tests: YAML parsing, id / `toolPrefix` validation, `authTokenSecret` via `ISecretStore`, reserved namespace rejection, hot reload
- [x] 2.2 Implement `YamlMcpServerCatalog`
- [x] 2.3 Write `McpToolSource` tests with an in-process fake MCP server: discovery, flat namespace, sanitizing, collision rejection, `list_changed`, disconnect hides tools, reconnect restores, untrusted flag taints
- [x] 2.4 Implement `McpToolSource` (stdio + streamable HTTP)
- [x] 2.5 Write registry tests: source register / unregister as a unit, unmatched external tool defaults to confirm
- [x] 2.6 Extend `ToolRegistry` and risk engine defaults
- [ ] 2.7 Add MCP server status to `/health`

## 3. Home Assistant client

- [ ] 3.1 Create `Marshal.Integrations.HomeAssistant` and test project; update `packages.puml` and `CLAUDE.md` repository map
- [ ] 3.2 Write websocket client tests against a fake server: auth, registry loads, `state_changed` updates, reconnect reload
- [ ] 3.3 Implement `HomeAssistantClient` and `HomeAssistantCache`
- [ ] 3.4 Write `HaTargetResolver` tests: name, alias, area, floor, domain, device class expansion; non-exposed and unknown → unresolved; area with lock → confirm through the risk engine
- [ ] 3.5 Implement `HaTargetResolver` and ship default `risk.yaml` rules
- [ ] 3.6 Write area context tests: current-area states, mentioned entities, no full dump
- [ ] 3.7 Implement the HA area context contributor
- [ ] 3.8 Write timer tests: tools hidden without device, hidden when HA MCP timers are usable, start / cancel / status call HA with the turn's device
- [ ] 3.9 Implement `TimerTools`
- [ ] 3.10 Write time zone tests (HA value, config override)
- [ ] 3.11 Implement `HomeAssistantTimeZoneSource`

## 4. Skills

- [ ] 4.1 Write skill catalog tests: frontmatter parsing, invalid skill skipped, hot reload, secret scanner applied
- [ ] 4.2 Implement `FileSkillCatalog` and `SkillIndexContributor`
- [ ] 4.3 Write `agent.load_skill` tests: body, extra file, unknown skill error, hidden without skills, no taint
- [ ] 4.4 Implement `agent.load_skill`

## 5. Templates and subagents

- [ ] 5.1 Write template catalog tests: frontmatter, allowlist globs, default template, hot reload
- [ ] 5.2 Implement `FileTemplateCatalog` and template-driven visibility / model tier / limits
- [ ] 5.3 Write `agent.delegate` tests: inline result, same owner and satellite policy, taint flows back, depth limit hides tool, confirm-tier hidden and denied inside subagents
- [ ] 5.4 Implement `agent.delegate` and child execution contexts; record subagent steps in the parent run

## 6. Config view

- [ ] 6.1 Write API tests: config summary without secrets, reload errors, admin-only
- [ ] 6.2 Implement config summary endpoint
- [ ] 6.3 Write component tests and implement the config view in `web/`

## 7. Documentation

- [ ] 7.1 Add example `deploy/config/mcp/ha.yaml`, a sample skill and a sample template
- [ ] 7.2 Update `CLAUDE.md` package index if needed
