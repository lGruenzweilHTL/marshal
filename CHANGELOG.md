# Changelog

All notable changes to Marshal are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Marshal has no tagged release yet, so everything below is unreleased.

## [Unreleased]

Development started on 2026-09-28. The latest work in the history is dated 2026-10-17.

### Added

- Core agent: tool-calling loop, tool registry, risk engine and guard with tiers, model routing, context builder, sessions, run recording and OpenTelemetry export.
- Versioned conversation API with streaming turns, health endpoints and an OpenAPI document per version.
- Authentication: local accounts with passkeys and passwords, OpenID Connect login, role scopes, machine tokens and a write-only secret store.
- Vue web app: streaming chat, runs and cost, dashboard, security, secrets, preferences and a read-only config view for admins, redesigned on 2026-10-08.
- Home Assistant custom component that makes Marshal an Assist conversation agent with streaming replies.
- Home Assistant integration: registry and state cache, target resolution for risk rules, area context, timers, places, energy statistics and location reminders.
- Extensibility: hot-reloaded MCP server catalog, skills, agent templates and `agent.delegate` subagents. MCP tools can be marked untrusted per tool.
- Memory: chunk store with hybrid keyword and vector retrieval, versions and edges, project activation, consolidation and a memory view.
- Background work: durable tasks, scheduler, event bus, trigger matcher, deliveries, notifications, approval queue, follow-ups, system jobs and the daily briefing.
- Integrations: Moonraker printers with print babysitting, IMAP mail without any send path, FreshRSS, git-backed notes, Seerr with read-only Sonarr and Radarr, Music Assistant, read-only Proxmox VE and read-only GitHub.
- Webhook ingest with Uptime Kuma and Proxmox normalizers.
- Artifacts: per-owner artifact store, web viewer, code highlighting, 3D model rendering and `artifacts.inspect`.
- Display surfaces: show artifacts on the display nearest the satellite.
- Chat history: web conversations rest after the idle time instead of closing and can be continued at any time. `GET /api/v1/conversations` lists them, and the web chat shows voice conversations read-only. Background results of a handoff appear in the conversation they came from.
- Visible reasoning: per-model `reasoning: off|low|medium|high`, `reasoning` and `tool` stream events for the web UI, a reasoning block and tool timeline in the chat, and reasoning in the run detail. Voice turns never reason.
- Demo mode (`demo.enabled`): simulated Home Assistant, printer, mail, FreshRSS and notes for unconfigured integrations, seeded sample history, `marshal demo reset` and `marshal demo seed`, and a "Demo" badge.
- AI tasks: Home Assistant's `ai_task.generate_data` runs through Marshal. `POST /api/v1/ai-tasks` with the `aitasks:write` scope, a built-in `ai_task` template, image attachments that taint the run, structured output validated against the requested schema, and the session source `automation`. Confirm-tier calls go to the approval queue and run on approval.
- MCP protocol revision 2026-07-28 with fallback to 2025-11-25 and 2025-06-18, and `ttlMs` on tool lists.
- MCP tool pinning (`pinTools`): new or changed tools stay hidden until an admin approves them in the config view.
- MCP input requests (elicitation) answered only in the web UI, and MCP server tasks that resume a waiting Marshal task.
- Health warnings: `GET /api/v1/system/health`, a banner on every web page, the `health` live topic and the `system.health` tool, used by the example briefing.
- Tool retrieval by namespace (`tools.core`, `tools.retrieval.*`, MCP `description`) and `agent.find_tools`.
- Marshal as an MCP server at `/mcp` (streamable HTTP, stateless) for Claude Code, VS Code and other MCP clients: scopes `mcp:read` (memory, notes and task read tools) and `mcp:write` (also `tasks_create` and `ask_marshal`). Calls run through the guard as recorded runs and count against the daily cost cap; confirm-tier actions go to the approval queue.
- `telemetry.captureContent`: opt-in content on spans, redacted like run recording.
- Spoken fillers before slow tools in voice turns (`voice.fillerThresholdMs`).
- Time to first delta per run, a voice timeline in the run detail and "slowest first words" sorting in the runs list.
- Screenshot sweep (`npm run screenshots`) over every web route at phone and desktop width, with layout checks.
- Docker image, compose file, example `/config` files and clean startup failure messages that list every configuration problem.

### Changed

- Traces follow the OpenTelemetry GenAI semantic conventions (version 1.41) and are emitted while a run executes. Model and embedding calls get `chat` and `embeddings` spans, and MCP requests carry the trace context. Rename dashboards and queries: `marshal.run` is now `invoke_agent`, `marshal.model_call` is now `chat`, and `marshal.tool_call` is now `execute_tool`. The step spans `marshal.context` and `marshal.reply` are gone; with `telemetry.captureContent: true`, the input and reply are attributes of `invoke_agent`. Step attributes moved to convention names (`marshal.model` and `marshal.tier` to `gen_ai.request.model`, `marshal.tokens_in` and `marshal.tokens_out` to `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens`).
- Startup failures list each problem with its file and key instead of failing on the first one.
- Web UI alignment pass: the chat composer's text line and send button share one centre, and controls in one row share the control heights.
- Reply steps are recorded in full, so the chat history shows the whole reply.
- The system prompt orders context contributors by stability (`static`, `session`, `turn`) before their order value, so provider prompt caches keep a longer prefix.

[Unreleased]: https://gitea.gruenzweil.cc/lukas/marshal
