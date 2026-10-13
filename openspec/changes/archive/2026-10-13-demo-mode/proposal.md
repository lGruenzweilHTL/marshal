# Proposal

## Why

Showing Marshal to someone, taking screenshots for the docs, or trying it out needs a running Home Assistant, a printer, a mail account, FreshRSS and a notes repository. Without them most tools are hidden and most views are empty, so a fresh instance shows little of what Marshal does. A demo mode with simulated integrations and seeded history makes every view and the main tool flows visible on an instance with only a model configured.

## What Changes

- New `demo` configuration section: `demo.enabled` (default `false`) and `demo.showBadge` (default `true`).
- With demo mode on, each integration in scope that is **not configured** is replaced by a simulated one behind the existing interfaces. Configured integrations stay real. Scope for v1:
  - Home Assistant: areas, lights, climate, sensors, zones, energy, timers, music players and a calendar. Service calls change the simulated state. Home control (lights, climate, live context) normally comes from HA's MCP server; when no MCP server with id `ha` is configured, demo mode offers a few tools with the same ids as that server's (`ha.intent__HassTurnOn`, ...), backed by the simulated state.
  - One printer in the middle of a print, with state changes over time and snapshots.
  - Mail: an IMAP-like inbox with sample messages. Drafts are kept in memory, and there is still no send path.
  - FreshRSS: feeds and articles with full text.
  - Notes: a small sample notes collection.
- Simulated tools behave like the real ones toward the model: the same tool ids, the same risk tiers and the same guard, so confirm-tier actions (for example cancelling the print) still need confirmation.
- Seeded data for the instance owner, created once when demo mode starts, with times relative to the seed time:
  - conversations, including voice turns from two satellites, and a backgrounded turn with its result;
  - runs with steps;
  - tasks, including a daily briefing with results;
  - notifications;
  - one pending approval;
  - memory chunks with edges.
- Every seeded row is recorded, so `marshal demo reset` removes exactly the seeded rows, and `marshal demo seed` seeds again. Rows that the user or the agent creates while in demo mode are not removed.
- A "Demo" badge in the navigation, hidden with `demo.showBadge: false`. The system view marks each simulated integration.
- A real model provider is still required.

## Capabilities

### New Capabilities

- `demo-mode`: the demo configuration, which integrations are simulated and when, simulated behavior rules, seeded data, reset and seed commands, and the badge.

### Modified Capabilities

None. The integrations' own requirements are unchanged; demo mode supplies other implementations of their interfaces.

## Non-goals

- Simulating Media (Seerr, Sonarr, Radarr), Proxmox, GitHub, executors, webhooks or MCP servers, apart from the Home Assistant home control tools named above.
- Running without a model (no scripted chat client).
- A hosted public demo with several visitors, or resetting data on a timer.
- Mixing simulated and real data inside one integration (for example two real printers plus one simulated printer).

## Impact

- New project `src/Marshal.Demo` (simulated integrations, seed data, seeder), referenced only by `Marshal.Api`; test project `tests/Marshal.Demo.Tests`.
- `src/Marshal.Data`: `demo_seed` table and migration.
- `src/Marshal.Api`: demo registration in the composition root, `marshal demo reset|seed` CLI, demo flag in `/api/v1/me` or system info for the badge.
- `web/`: badge in the shell, simulated markers in the system view.
- `docs/diagrams/packages.puml` and `erd.puml`, the CLAUDE.md repo map, `deploy/config.example/marshal.yaml`, a how-to page for demo mode.
