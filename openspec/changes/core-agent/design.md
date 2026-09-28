## Context

The repository holds only design documents: `docs/brainstorm-notes.md` (§1–§6, §8, §19–§22 are relevant here), `docs/diagrams/erd.puml`, `docs/diagrams/packages.puml`, `STYLE_GUIDE.md` and `CLAUDE.md`. This is sub-project 1 of 7. It must deliver a working, fully tested conversation core that the HA component (sub-project 3) and the web chat (sub-project 2) can call, while leaving seams for memory, MCP, skills, templates, tasks and notifications.

Constraints: .NET 10, SQLite, single container, LAN only, tests never need a real LLM, TDD, small interfaces everywhere, owner-scoped data from day one.

## Goals / Non-Goals

**Goals:**
- Solution layout and build rules from the style guide (`Directory.Build.props`, central package versions, analyzers, warnings as errors).
- Agent loop, model router, tool registry with visibility, risk engine and guard with the full in-conversation confirmation flow, taint and reasons.
- `/api/v1/conversation/turns` with SSE, API versioning, version discovery, health.
- Sessions, identity seam, execution context, satellite policy.
- Configuration, secret store, machine tokens (CLI), run recording, audit log.
- Docker image and example config.

**Non-Goals:**
- Human auth, SPA, cookie principals (auth-web-shell).
- MCP, skills, templates, subagents, HA client (extensibility).
- Memory, integrations, tasks, notifications, approval queue, auto-background (later changes).

## Decisions

### D1. Project layout
Projects created now: `Marshal.Abstractions`, `Marshal.Core`, `Marshal.Data`, `Marshal.Api`, `tests/Marshal.{Abstractions,Core,Data,Api}.Tests`, `tests/Marshal.Testing`. References follow `packages.puml`: Core → Abstractions, Data; Data → Abstractions; Api → all. `Marshal.Memory`, `Marshal.Tasks` and `Marshal.Integrations.*` are created by their own changes. The admin CLI is a command mode of `Marshal.Api` (`marshal tokens ...` dispatches before the web host starts) so the container has one entry point.
*Alternative:* separate CLI project — rejected, it would need its own DI and config wiring for three commands.

### D2. Own thin harness on Microsoft.Extensions.AI
The loop is our own code over `IChatClient`; we do **not** use `FunctionInvokingChatClient`, because every tool call must pass through the guard (freeze, taint, reasons) before execution. Tools are exposed to the model as `AIFunctionDeclaration`s (schema only); invocation is ours.
*Alternatives:* Microsoft Agent Framework, Claude Agent SDK — rejected in the notes (§3).

### D3. Interfaces introduced and v1 implementations
| Interface (project) | Purpose | v1 implementation |
|---|---|---|
| `IModelRouter` (Abstractions) | tier + capabilities → `IChatClient` | `ConfiguredModelRouter` (Core) over clients built by the host |
| `ICostCalculator` | tokens → cost | `PriceTableCostCalculator` |
| `IAgentRunner` | run the loop for an execution context | `AgentRunner` (Core) |
| `ITool` | one executable tool: descriptor + `InvokeAsync` | built-in tool classes |
| `IToolSource` | provides a set of tools (built-in now; MCP, skills later) | `BuiltInToolSource` |
| `IToolRegistry` | id ↔ model name, collision checks | `ToolRegistry` |
| `IToolVisibilityFilter` | computes the visible tool list | `ToolVisibilityFilter` |
| `IRiskEngine` | tool call → tier + reasons | `RuleBasedRiskEngine` (rules from `/config/risk.yaml`) |
| `IRiskTargetResolver` | expands call arguments to concrete targets, per namespace | none registered in v1 (unresolvable → confirm); HA resolver in `extensibility` |
| `IGuard` | decide / freeze / confirm | `Guard` |
| `IPendingActionStore` | persisted frozen actions | `EfPendingActionStore` (Data) |
| `IConfirmationJudge` | positive / neutral / negative | `LlmConfirmationJudge` (fast tier, structured output) |
| `IConfirmationKeywords` | per-language keyword match | `ConfiguredConfirmationKeywords` |
| `IContextBuilder` + `IContextContributor` | system prompt assembly | `ContextBuilder`; contributors: base prompt, user profile, time/locale, satellite, output mode |
| `ISatelliteContextProvider` | satellite profile + area context | `YamlSatelliteContextProvider` |
| `ISessionStore` | sessions | `EfSessionStore` |
| `IIdentityResolver` | evidence → owner | `InstanceOwnerIdentityResolver` |
| `IExecutionContextAccessor` | current execution context | `AsyncLocalExecutionContextAccessor` |
| `ISecretStore` + `ISecretSource` | secret lookup | `SecretStore` over `EnvironmentFileSecretSource`, `DatabaseSecretSource` |
| `IConfigSecretScanner` | literal-secret refusal | `ConfigSecretScanner` |
| `IApiTokenService` | create / validate / revoke tokens | `ApiTokenService` (SHA-256 of 32 random bytes) |
| `IRunRecorder` | runs, steps, OTel | `DbRunRecorder` + `OpenTelemetryRunSink` |
| `IAuditLog` | non-run actions | `DbAuditLog` |

Every interface lives in `Marshal.Abstractions` unless it is purely internal to Core.

### D4. Model-facing tools in this change
| Tool | Tier | Flags | Hidden when |
|---|---|---|---|
| `agent.ask_advisor(question)` | safe | — | voice turns; no advisor model configured; per-run advisor limit reached; daily advisor cap reached; inside subagents (later) |

No other built-in tools exist yet. Guard and visibility tests use fake tools from `Marshal.Testing` covering safe, confirm, forbidden, arg-aware, `untrustedOutput` and `persists`.

### D5. Risk evaluation pipeline
```
call ─▶ registry (known? visible?) ─▶ rules (first match, per resolved target, max tier)
     ─▶ satellite policy (denyTools / denyConfirm) ─▶ taint escalation (persists → confirm)
     ─▶ safe: execute │ confirm: freeze + reasons │ forbidden/denied: error result
```
Tier order `safe < confirm < forbidden`; each stage may only raise. Reasons are collected along the way and localized through a small resource table (de / en). The pending action is written **before** the question is streamed.

### D6. Confirmation turn handling
The conversation endpoint checks for a pending action in the session before running the loop. Keyword match → execute frozen action, then run the loop once with the result so the model can phrase the reply. Judge `positive` → same. `negative` → fixed localized "cancelled" reply without a model call. `neutral` / failure → drop and run the loop on the message as a normal turn. The judge uses structured output (JSON enum) with a 3 s timeout.

### D7. SSE with TypedResults.ServerSentEvents
The endpoint returns `TypedResults.ServerSentEvents` over an `IAsyncEnumerable<SseItem<TurnEvent>>` fed by a `Channel<TurnEvent>` the runner writes to. Non-streaming clients read the same stream. The latency cap is a linked `CancellationTokenSource` with `TimeProvider`; on expiry the runner writes the status + fixed reply and records outcome `cancelled` / `latency_cap`.

### D8. Persistence
EF Core with SQLite, one `MarshalDbContext` in `Marshal.Data`, snake_case column naming via convention, value converters for strongly typed ids, global query filters from `IExecutionContextAccessor`; a query without context throws (fail closed). Entities in this change: `User` (plain table now; becomes the Identity user in auth-web-shell), `UserPreferences`, `ApiToken`, `Session`, `Run`, `RunStep`, `PendingAction`, `AuditEvent`, `Secret`. The instance owner row is created on first start. Migration backup: `File.Copy` to `marshal.db.<timestamp>.bak` when `GetPendingMigrations()` is non-empty.

### D9. Configuration files
`/config/marshal.yaml` (models, prices, http, sessions, confirmation keywords, latency caps), `/config/risk.yaml`, `/config/satellites.yaml`. Hot reload through `IOptionsMonitor` with a validating wrapper that keeps the last good value. The secret scanner runs on the raw YAML tree (not the bound options) so unknown keys are also checked.

### D10. Taint bookkeeping
Taint lives on the execution context of the run (`TaintState { IsTainted, Sources[] }`) and is persisted on `Run` (`tainted`, `taint_sources`). It is set by the loop after a tool with `untrustedOutput` returns, before the result is appended to the context.

## Risks / Trade-offs

- [Judge latency adds a model round trip on non-keyword replies] → keywords cover the common path; judge on `fast` tier with a short timeout, failure drops (safe default).
- [Own loop instead of `FunctionInvokingChatClient` means more code] → the guard requirements cannot be met with automatic invocation; loop is small and fully covered by scripted fake tests.
- [Latency cap cancels work in this sub-project] → accepted; background continuation arrives in `background-work`.
- [No target resolvers yet → every target rule makes calls confirm] → intended fail-closed behavior until the HA resolver exists.
- [Global query filters can be bypassed with `IgnoreQueryFilters`] → banned by an analyzer rule / code review; ownership tests with a second user for every store.
- [Entropy-based secret scan may flag harmless values] → exact pattern list is documented; users can move such values to a secret.

## Migration Plan

Greenfield. The first start creates `/data/marshal.db`, `/data/keys` and the instance owner. Rollback = previous image plus the automatic pre-migration DB copy.

## Open Questions

- Exact confirmation keyword defaults per language (proposal: en `confirm`, `confirmed`, `approved`, `yes do it`; de `bestätigt`, `bestätigen`, `ja mach`).
- Anthropic SDK `IChatClient` adapter maturity for structured output (judge) — verify when building; fall back to a JSON-only prompt + parse.

## Implementation Notes

Findings and decisions made while building (verified against the package versions in `Directory.Packages.props`).

- **Anthropic SDK (v12.50) — verified.** `new AnthropicClient { ApiKey = ... }.AsIChatClient(model)` is the official `IChatClient` adapter. It maps `ChatResponseFormat.ForJsonSchema` to Anthropic structured outputs and keeps `enum` in the schema, so the judge's JSON-enum format works without the prompt-only fallback. The judge still accepts a bare `positive` / `neutral` / `negative` and treats anything else as a failure (drop).
- **OllamaSharp (5.4.30).** Its bundled source generator needs a newer Roslyn than SDK 10.0.1xx; Marshal does not use it, so `Directory.Build.targets` removes that analyzer for every project.
- **Asp.Versioning (10.2).** OpenAPI per version comes from the maintainers' `Asp.Versioning.OpenApi` package (`AddApiVersioning().AddApiExplorer().AddOpenApi()` and `MapOpenApi().WithDocumentPerVersion()`); it serves `/openapi/v1.json`. With URL-segment versioning an unsupported version (`/api/v9/...`) is an empty 404, so an `/api/{**path}` fallback returns ProblemDetails (`api.unsupported_version`) and the `api-supported-versions` header naming the supported versions.
- **Tests** run on xunit.v3 4.x with Microsoft.Testing.Platform (`global.json` `test.runner`). Branch coverage via `Microsoft.Testing.Extensions.CodeCoverage`: `RuleBasedRiskEngine` (incl. `RiskRulesFile`, `ToolGlob`), `ToolVisibilityFilter` and `ToolCallGuard` are at 100 % branch coverage (`dotnet test --project tests/Marshal.Core.Tests -- --coverage --coverage-output-format cobertura`); so are `ConfirmationFlow`, `ConfiguredConfirmationKeywords` and `ConfirmationReasonText`.
- **`IGuard` implementation** is named `ToolCallGuard` (a class `Guard` would clash with the `Marshal.Core.Guard` namespace).
- **`risk.yaml` format.** `match.tool` is a glob on the internal id; `match.args` maps top-level argument names to allowed values (argument conditions, case-insensitive); every other `match` key is a target condition compared with the resolved targets' attributes (`domain`, `deviceClass`, ...). Scalars or lists are allowed. An invalid file never becomes current; if one ever did, the engine fails closed (everything forbidden).
- **Confirmation keywords (open question resolved).** Defaults: en `confirm`, `confirmed`, `approved`, `yes do it`; de `bestätigt`, `bestätigen`, `ja mach`, `ja mach das`. A configured language list replaces that language's defaults.
- **Confirmation after expiry.** The keyword / judge verdict is still computed; a confirming reply gets the "expired" reply, a negative one "cancelled", anything else continues as a normal turn. The action is marked `expired` in every case.
- **Persistence.** Data services take `IDbContextFactory<MarshalDbContext>` and use one context per operation, so singleton tools and the registry can depend on them. The only query that bypasses the owner filter is the token lookup by hash in `ApiTokenService.ValidateAsync` (authentication happens before an owner is known). The `Secret` filter returns only instance rows when there is no execution context (startup secret resolution). `SaveChanges` refuses rows of another owner.
- **Sessions.** The unique index on `(api_token_id, external_id)` is partial (`closed_at IS NULL`) so a client id can start a new session after an idle close. Token lookups run as the token's owner; in v1 that is always the instance owner (multi-user resolution will revisit this). Conversation history is rebuilt from the context and reply steps of the session's runs.
- **Run recording.** Extra columns beyond the planned ERD: `runs.outcome_reason` (`latency_cap`, `step_limit`, `model_unavailable`), `run_steps.first_token_ms`, `pending_actions.language` and `created_at`. Outcome for the step limit is `error` with reason `step_limit`. OpenTelemetry export (`telemetry.otlpEndpoint`) uses the same redacted data through `IRunObserver`.
- **Configuration.** `paths.config` / `paths.data` / `paths.secrets` locate the volumes (`MARSHAL__Paths__Config` is read before configuration exists). Options annotations name the config key in their messages (e.g. `agent.maxSteps`).
- **Example config location.** `.gitignore` treats `deploy/config/` as a local instance config and keeps examples in `deploy/config.example/`, so the example files live there instead of `deploy/config/` (task 14.2).
