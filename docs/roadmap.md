# Roadmap

What Marshal can do today and which ideas are deferred. This page is a summary, not a commitment; the specifications in `openspec/specs/` define the exact behavior.

## What exists

All planned features through the third roadmap wave are implemented and archived, followed by chat history, visible reasoning, demo mode, Home Assistant AI tasks, MCP tool pinning with input requests and server tasks, and tool retrieval with voice latency work.

| Area | Capabilities |
|---|---|
| Voice and chat | Assist conversation agent, streaming replies, spoken fillers before slow tools, voice follow-ups, web chat with conversation history, background results in place, reasoning and tool steps |
| Home | Home Assistant devices, areas, timers, places, energy statistics, to-do lists through MCP, location reminders, AI tasks for automations |
| Printers | Moonraker state, snapshots, inspection, print babysitting |
| Information | Mail (read and draft only), FreshRSS, git-backed notes, daily briefing |
| Media | Seerr requests, read-only Sonarr and Radarr, Jellyfin links, Music Assistant playback |
| Homelab | Uptime Kuma and Proxmox events, read-only Proxmox, read-only GitHub |
| Artifacts and displays | Artifact store and viewer, code highlighting, 3D renders, show on the nearest display |
| Memory and tasks | Hybrid memory retrieval, projects, consolidation, scheduled and event-driven tasks, approval queue |
| Platform | Local and OIDC login, machine tokens, secret store, skills, agent templates, MCP servers (tool pinning, input requests, server tasks), tool retrieval, health warnings, run recording with voice timelines, demo mode |

See the [changelog](../CHANGELOG.md) for the full list.

## In progress

These changes are proposed in `openspec/changes/` and not implemented yet: CalDAV calendars, web search through SearXNG, Marshal as an MCP server, MCP Apps, quarantined extraction of untrusted content, memory with valid time and idle-time work, evals and the learning toggle, proactive suggestions and learning, and OpenTelemetry GenAI traces.

## Deferred

These ideas are not planned for a specific release. Each needs its own OpenSpec change before work starts.

| Idea | Why it waits |
|---|---|
| Phone channel | Push already reaches you for critical alerts. Calls need a SIP number, real-time voice and caller-ID trust handling. |
| Speaker identification | It pays off only with several users, and Marshal never receives audio today. |
| Browser or computer use | Navigation is a caller-chosen URL, so every run would be tainted, and the injection risk sits next to home control. |
| Self-modification | Needs a git host integration and strict protection for sensitive paths. Marshal would propose changes that something outside Marshal applies. |

## Known gaps

- Several users on one instance. The data model is ready, the product is not.
- Checks against a real Music Assistant installation are still open.
- Reasoning has not been checked live against a real reasoning model, and demo mode's chat flows have not been checked end to end with a real model.

## Influence the roadmap

Open a [feature request](../.github/ISSUE_TEMPLATE/feature_request.yml). See [CONTRIBUTING.md](../CONTRIBUTING.md) for how changes are proposed.
