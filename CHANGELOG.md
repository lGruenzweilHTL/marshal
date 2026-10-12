# Changelog

All notable changes to Marshal are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Marshal has no tagged release yet, so everything below is unreleased.

## [Unreleased]

Development started on 2026-09-28. The latest work in the history is dated 2026-10-10.

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
- Docker image, compose file, example `/config` files and clean startup failure messages that list every configuration problem.

### Changed

- Startup failures list each problem with its file and key instead of failing on the first one.

[Unreleased]: https://gitea.gruenzweil.cc/lukas/marshal
