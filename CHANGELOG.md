# Changelog

All notable changes to Marshal are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Marshal has no tagged release yet, so everything below is unreleased.

## [Unreleased]

Development started on 2026-09-28. The latest work in the history is dated 2026-10-20.

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
- Chat widgets and MCP apps: printer and music widgets below replies in the web chat (live state, controls as recorded `widget:*` runs, confirm-tier controls behind a dialog), and MCP Apps UIs of servers with `apps: true` in a sandboxed iframe. Apps call only their own server's tools, as `app:<server>/<tool>` runs; confirm-tier calls are denied. The SPA CSP allows frames from Marshal's own origin only.
- Ratings and evals: thumbs up and down on chat replies and in the run detail, a rating filter in the runs list, and the "help Marshal learn" preference (off by default). With it on, rated runs become eval cases; admins can save any run as a case. The admin evals view replays cases against a candidate model per tier, template or base prompt without executing tools, compares tool calls and replies (pass, changed, diverged, failed) with an optional judge model, and reports totals and cost. Context steps now store the turn-level context text.
- Quarantined extraction: `agent.extract` answers one typed question about a mail, RSS article or note through a tool-less `fast` tier model, so the content never reaches the main model. Typed answers (yes/no, numbers, dates, enum values) do not taint the run; `text` answers do. Agent tasks have a frozen tool policy (`tools` argument on `tasks.create` and `tasks.watch`, editable only in the task view), enforced by the visibility filter and the guard (`tool.outside_policy`).
- Web search through a self-hosted SearXNG (`web.searxng`): `web.search` returns titles, snippets, URLs and domains without opening any page and taints the run with `web`; `web.answer` gives only the snippets to the quarantined extractor, so typed answers stay untainted. `agent.extract` accepts `web:<result id>` references for one hour. Queries are safe in every run; the guide states that query text reaches the configured engines.
- CalDAV calendars (`calendar.caldav.accounts`) next to the Home Assistant calendars, for Nextcloud, Radicale, Baïkal, Fastmail and iCloud: discovery from the DAV URL, ids `caldav:<account>/<calendar>`, organizer and attendees with their answers, recurrence expansion, and `scope: occurrence | series` on `calendar.update_event`. A failing account raises a health warning and leaves the other calendars working. `calendar.find_free_time` finds free slots across all calendars without tainting the run. Every `pollMinutes` Marshal publishes trusted, id-only `calendar.event_created`, `event_updated`, `event_removed` and `invitation` events for watches. Demo mode simulates a CalDAV work calendar with an invitation.
- Memory valid time: chunks and versions carry `validFrom` and `validTo`; updates and supersedes close the previous fact. Automatic retrieval skips closed chunks, and `memory.recall` with `asOf` searches what was true at a past time, including earlier versions. The memory view edits valid time, filters closed chunks and shows valid times in the history.
- Idle-time memory jobs after the nightly consolidation: contradiction and duplicate proposals (`memory.duplicateThreshold`) that you accept or dismiss in the audit, and project summaries added to the context of active projects.
- Task preparation (`prepare: { before, goal }` on cron tasks): a preparation run up to 12 hours before each scheduled run collects notes the main run reads; the example briefing prepares appointments and the RSS digest at 03:00.
- Proactive suggestions (with "help Marshal learn" on): detectors `window_rain`, `energy_anomaly`, `print_done_away` and `maintenance_counter` propose things in the Inbox or as push with Accept and Dismiss. An acceptance estimate per detector decides between dropping, Inbox and push; proposed actions are frozen and run through the guard on Accept. Mute and reset detectors in Preferences (`suggestions.*`). `printers[].powerEntity` lets `print_done_away` offer to switch the printer off.
- Skill proposals: a nightly job (`maintenance.skillProposalSchedule`) finds workflows you repeat and drafts a `SKILL.md`, with a diff against a same-name skill. Preview, download or dismiss them in the Inbox; Marshal never writes to `/config`.
- Maintenance by usage (`maintenance.items`): items counted in printer hours, filament meters or a Home Assistant counter, "soon" and "due" notifications, `maintenance.status` and `maintenance.done`, and a Home widget with a confirmed "done".
- `telemetry.captureContent`: opt-in content on spans, redacted like run recording.
- Spoken fillers before slow tools in voice turns (`voice.fillerThresholdMs`).
- Time to first delta per run, a voice timeline in the run detail and "slowest first words" sorting in the runs list.
- Screenshot sweep (`npm run screenshots`) over every web route at phone and desktop width, with layout checks.
- Docker image, compose file, example `/config` files and clean startup failure messages that list every configuration problem.
- Documentation site built with Clay from `docs/`, in Marshal's visual style, with the API v1 reference and request cells generated from the OpenAPI document; deployed to GitHub Pages.

### Changed

- The OpenAPI document names each operation by its route below `/api/v1` (the former summary is its description) and declares the `apiToken` and `session` security schemes with each operation's requirements.
- Traces follow the OpenTelemetry GenAI semantic conventions (version 1.41) and are emitted while a run executes. Model and embedding calls get `chat` and `embeddings` spans, and MCP requests carry the trace context. Rename dashboards and queries: `marshal.run` is now `invoke_agent`, `marshal.model_call` is now `chat`, and `marshal.tool_call` is now `execute_tool`. The step spans `marshal.context` and `marshal.reply` are gone; with `telemetry.captureContent: true`, the input and reply are attributes of `invoke_agent`. Step attributes moved to convention names (`marshal.model` and `marshal.tier` to `gen_ai.request.model`, `marshal.tokens_in` and `marshal.tokens_out` to `gen_ai.usage.input_tokens` and `gen_ai.usage.output_tokens`).
- Startup failures list each problem with its file and key instead of failing on the first one.
- Web UI alignment pass: the chat composer's text line and send button share one centre, and controls in one row share the control heights.
- Reply steps are recorded in full, so the chat history shows the whole reply.
- The system prompt orders context contributors by stability (`static`, `session`, `turn`) before their order value, so provider prompt caches keep a longer prefix.

[Unreleased]: https://gitea.gruenzweil.cc/lukas/marshal
