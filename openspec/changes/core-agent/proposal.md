## Why

Marshal has a complete design (docs/brainstorm-notes.md, docs/diagrams/) but no code. Every later sub-project (auth, HA integration, extensibility, memory, integrations, background work) depends on the same core: an agent loop that talks to local and cloud models, a tool registry that hides what the model may not use, a guard that freezes and confirms risky actions, a versioned conversation API, owner-scoped persistence and a secret store. This change builds that core first (sub-project 1 of 7), so everything else plugs into a tested foundation.

## What Changes

- New .NET 10 solution with `Marshal.Api`, `Marshal.Abstractions`, `Marshal.Core`, `Marshal.Data`, their test projects and `Marshal.Testing` (shared fakes).
- Provider-neutral model access via `Microsoft.Extensions.AI`: Ollama, OpenAI-compatible servers, Anthropic. Model router with tiers (`fast`, `standard`, `advisor`) and capability flags (`vision`, `tools`, `contextLength`).
- Own thin agent loop (model → tool calls → guard → execute → repeat, max steps) with the advisor tool `agent.ask_advisor`.
- Tool registry with internal ids (`<namespace>.<name>`), model-facing names (`<namespace>_<name>`), and visibility filtering.
- Risk engine (safe / confirm / forbidden, arg-aware rules from `/config`, target resolution seam), guard with frozen pending actions, keyword + judge confirmation, fail-closed behavior, taint tracking and confirmation reasons.
- Versioned public API (`/api/v1`, `Asp.Versioning.Http`, one OpenAPI document per version), `POST /api/v1/conversation/turns` streamed as SSE, `/api/versions`, `/health`.
- Sessions with ownership pinned on first turn via `IIdentityResolver` (v1: instance owner), execution context, per-satellite context and policy from `/config/satellites.yaml`.
- Configuration from `/config/marshal.yaml` + env (`MARSHAL__`), camelCase keys, hot reload for rules and satellites, startup refusal of literal secrets.
- `ISecretStore` with environment / `/run/secrets` and Data Protection-encrypted DB sources.
- Machine API tokens (hashed, scoped, revocable) created through an admin CLI command.
- Run recording (`runs`, `run_steps`, cost from a price table) and an audit log, behind `IRunRecorder`; OpenTelemetry traces and metrics.
- EF Core + SQLite with owner-scoped global query filters and auto-applied migrations (DB copied first).
- Dockerfile and example `deploy/` config.
- Latency cap for this sub-project: stream `status` events and end the turn with a short "this takes too long" reply (no backgrounding yet).

## Non-goals

- No human login, web UI, cookie auth or token management UI (sub-project 2: `auth-web-shell`).
- No Home Assistant component, HA MCP, skills, agent templates or subagents (sub-projects 3 and 4).
- No memory, no integrations (printer, mail, RSS, calendar, notes).
- No background tasks, scheduler, approval queue, notifications, auto-background or voice follow-ups (sub-project 7). Tools that need them do not exist yet and are therefore invisible.
- No retention purge job (needs the scheduler, sub-project 7); payloads are recorded but not yet purged.
- No multi-user behavior: the identity resolver always returns the instance owner.

## Capabilities

### New Capabilities
- `model-routing`: provider configuration, tiers, capability flags, router selection, price table and cost calculation.
- `agent-loop`: the agent loop, step limits, advisor tool and its limits.
- `tool-registry`: tool ids and model-facing names, namespaces, collisions, tool descriptors and the visibility filter.
- `risk-guard`: risk tiers, arg-aware rules and target resolution, pending actions, confirmation (keywords + judge), taint escalation, confirmation reasons.
- `context-builder`: system prompt assembly, output modes, language and locale, satellite context lines, contributor seam.
- `conversation-api`: API versioning, conversation turn endpoint, SSE events, latency cap, version discovery, health.
- `sessions-identity`: sessions, ownership, identity resolver, execution context, idle close.
- `satellite-policy`: satellite profiles, `denyTools` / `denyConfirm`.
- `configuration`: config files, layering, key naming, hot reload, literal-secret refusal.
- `secret-store`: secret names, sources, lookup order, encryption, required secrets.
- `machine-tokens`: API tokens, scopes, hashing, revocation, CLI creation.
- `data-ownership`: persistence, strongly typed ids, owner-scoped query filters, migrations.
- `run-recording`: runs, run steps, audit log, OpenTelemetry, redaction.

### Modified Capabilities
- None (no specs exist yet).

## Impact

- New code: `src/Marshal.Api`, `src/Marshal.Abstractions`, `src/Marshal.Core`, `src/Marshal.Data`, `tests/*`, `Directory.Build.props`, `Directory.Packages.props`, `Dockerfile`, `deploy/`.
- New public API: `/api/v1/conversation/turns`, `/api/versions`, `/openapi/v1.json`, `/health`.
- New dependencies: `Microsoft.Extensions.AI`, `OllamaSharp`, `Anthropic`, `Microsoft.Extensions.AI.OpenAI`, `Asp.Versioning.Http`, `Asp.Versioning.Mvc.ApiExplorer`, `Microsoft.AspNetCore.OpenApi`, `NetEscapades.Configuration.Yaml`, `YamlDotNet`, `Microsoft.EntityFrameworkCore.Sqlite`, `OpenTelemetry.*`, `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`.
- Diagrams: `docs/diagrams/erd.puml` and `packages.puml` keep "(planned)" but must match the entities and projects this change creates.
- Later changes (`auth-web-shell`, `extensibility`, `background-work`) add requirements to `conversation-api`, `risk-guard`, `machine-tokens`, `secret-store` and `tool-registry`; archive this change first.
