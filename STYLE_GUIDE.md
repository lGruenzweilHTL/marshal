# Marshal Style Guide

How we write code in Marshal. Part 1 describes the design philosophy that every change must respect. Part 2 contains the concrete C# rules. Formatting details are enforced by `.editorconfig` and `dotnet format`; this guide covers what tooling cannot.

When this guide and a specification in `openspec/specs/` disagree, the specification wins, and this guide gets fixed.

---

## Part 1 — Design philosophy

### 1. Small interfaces for every capability
Every integration and subsystem sits behind a small, focused interface, even when there is exactly one implementation today.

- An interface describes one capability (`IMailProvider`, `ICalendarProvider`, `INotificationChannel`, `IExecutor`, `IPrintFailureDetector`, `IIdentityResolver`, ...), not a grab bag.
- Prefer several small interfaces over one large one. A consumer depends only on what it uses.
- Differences between implementations are expressed through **capability flags** (e.g. `NativeThreads`, `ScopeEnforcedNoSend`), not through `is`-checks against concrete types.
- v1 may have one trivial implementation (e.g. an identity resolver that always returns the instance owner). The seam still exists.

### 2. The model can't use it → the model can't see it
Tools the model is not allowed to use in the current context are **removed from the tool list**, not refused after the call.

- Reasons for hiding: forbidden tier, not in the agent template's allowlist, denied for the satellite, confirm-tier on a satellite that cannot confirm, advisor in voice turns.
- The guard enforces the same rules again on the server. Visibility is the first line, the guard is the second. Never rely on only one.
- Capabilities that must never happen (sending mail, raw G-code) **do not exist as code paths at all**. Don't write a tool and then forbid it.

### 3. Fail closed
When a safety-relevant decision is uncertain, the answer is "no".

- Judge timeout or parse error → drop the pending action. When the judge is unsure, its prompt makes it answer `neutral`, which also drops the action.
- Unresolvable target in an arg-aware risk rule → confirm tier.

### 4. Freeze what the user confirms
Confirm-tier actions are stored server-side (tool + args + nonce + TTL) before the user is asked. On confirmation, the server executes exactly the frozen action. The model never gets to re-issue or modify it.

### 5. Untrusted content is data, never instructions
Mail, RSS articles, notes, web pages and tool output are untrusted. They are wrapped as data in the context, and they can never confirm an action, change configuration or trigger privileged behavior. Only user actions can (a user turn, a web UI approval, a nonce-verified push action).

### 6. Everything has an owner
Every user-owned row carries `owner_id`. All data access goes through owner-scoped queries (EF Core global query filters). Code reads the current user from the execution context and never from a global or static "the user". The instance is single-user today; the code must not assume that.

### 7. Generic mechanisms over special cases
If a feature can be expressed with the existing generic building blocks (tasks, triggers, conditional delivery, skills, templates), it gets **no dedicated code**. The daily briefing is the reference example: it is a task with a goal text, a template and a skill.

### 8. Configuration is files, credentials are data
Human-edited configuration lives in `/config` as git-friendly files (YAML with camelCase keys, Markdown). Secrets never go there: config holds only secret *names*, resolved through `ISecretStore` from environment / secret files or from encrypted, owner-scoped database rows (OAuth tokens, passwords).

### 9. Deterministic before intelligent
Use deterministic code wherever it is enough: event filters, alias matching, keyword confirmation, retrieval scoring. Call a model only where judgment is actually needed. It is cheaper, faster and testable.

---

## Part 2 — C# rules

### Platform and build
- Target **.NET 10**, latest C# language version.
- `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in every project (set once in `Directory.Build.props`).
- .NET analyzers enabled. Suppressions need a justification comment.
- `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` so the `.editorconfig` `IDE*` rules fail the build, and `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (required for IDE0005, unused usings, to run on build; `CS1591` stays off).
- Formatting: `.editorconfig` is the source of truth; run `dotnet format` before committing.
- Package versions are managed centrally in `Directory.Packages.props`.

### Files and namespaces
- File-scoped namespaces.
- One top-level type per file; the file name matches the type name.
- Namespace matches the folder path (`Marshal.Memory.Retrieval` → `src/Marshal.Memory/Retrieval/`).
- Organize by feature, not by technical kind (`Retrieval/`, not `Services/` + `Models/` + `Helpers/`).

### Naming
| Element | Convention | Example |
|---|---|---|
| Types, methods, properties, constants | `PascalCase` | `ChunkScorer`, `MaxSteps` |
| Interfaces | `I` + `PascalCase` | `IMailProvider` |
| Private fields | `_camelCase` | `_timeProvider` |
| Locals, parameters | `camelCase` | `pendingAction` |
| Async methods | `Async` suffix | `SearchAsync` |
| Type parameters | `T` or `T` + name | `TResult` |

Model-facing tools have an internal id `<namespace>.<snake_case_name>` (`mail.search`, `printer.start_print`); the model sees `<namespace>_<name>`. Built-in namespaces name the capability, never the provider (`printer`, not `moonraker`). Risk rules, satellite policies and template allowlists always refer to the internal id. Details: tool naming in the design notes (§3).

Names describe intent, not implementation. No abbreviations except well-known ones (`Id`, `Url`, `Http`, `Llm`, `Mcp`).

### Types
- Classes are **`sealed` by default**. Unseal only with a reason.
- `record` for DTOs, messages, events and other immutable data.
- `readonly record struct` for small value types (ids, scores).
- Strongly typed ids for domain entities (`ChunkId`, `TaskId`, `OwnerId`) instead of raw `Guid` / `string`. A user's id is always `OwnerId`, also where it is not an owner column (execution context, Identity user key); there is no separate `UserId` type.
- Prefer immutability: `init` properties, `IReadOnlyList<T>` / `IReadOnlyDictionary<TKey, TValue>` in public signatures.

### `var`
Use `var` when the type is obvious from the right side (`new`, cast, well-named factory). Otherwise write the type explicitly.

```csharp
var session = new Session(owner, source);        // obvious
IReadOnlyList<Chunk> hits = await retriever.RetrieveAsync(query, ct);  // not obvious from the call
```

### Dependency injection and options
- Primary constructors for services.
- Each project registers its services through one extension method: `services.AddMarshalMemory(configuration)`.
- Options pattern for configuration, with validation and `ValidateOnStart()`. Options classes are `sealed` and live next to the feature they configure.
- No service locator (`IServiceProvider.GetService` in business code).

```csharp
public sealed class ChunkScorer(TimeProvider timeProvider, IOptions<RetrievalOptions> options) : IChunkScorer
{
    public double Score(Chunk chunk, double similarity) { ... }
}
```

### Error handling
- **Expected failures return a `Result`**, not an exception: mailbox unreachable, printer offline, invalid tool arguments, entity not found, budget exceeded.
- Use Marshal's own `Result` / `Result<T>` from `Marshal.Abstractions` (no third-party library). Errors carry a stable code and a human-readable message.
- **Exceptions are for bugs and truly exceptional states** (broken invariants, misconfiguration at startup). Don't catch them for control flow.
- Tool executors turn a failed `Result` into a structured error message for the model. API endpoints turn it into a `ProblemDetails` response.
- Never swallow exceptions silently. Catch only where you can handle or translate them, and log once at the boundary.

```csharp
public async Task<Result<PrinterStatus>> GetStatusAsync(CancellationToken ct)
{
    if (!_cache.IsConnected)
        return Result.Failure<PrinterStatus>(PrinterErrors.Offline);
    ...
}
```

### API endpoints and versioning
- The public API is versioned by URL segment with `Asp.Versioning.Http`: `/api/v{version:apiVersion}/...`.
- **API versions are declared once in code** (`ApiVersions.V1`, ...). They are never read from configuration: a version describes the contract the code implements.
- **Endpoint files contain no version literals** and no `/api/...` prefixes. Each feature exposes a `Map<Feature>Endpoints(this RouteGroupBuilder group)` extension and registers its routes on the group it receives.
- A breaking change adds a new version and a new route group; the old version keeps working side by side until it is retired. Never change the contract of a published version in place.
- Unversioned: `/health`, `/api/versions` (version discovery), `/openapi/{document}.json` (the name carries the version), auth protocol callbacks (`/signin-oidc`, `/signout-callback-oidc`) and SPA routes only.
- Endpoints return typed results (`Results<Ok<T>, NotFound, ...>`); failed `Result`s become `ProblemDetails`.

```csharp
public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder group)
    {
        var tasks = group.MapGroup("/tasks").RequireAuthorization(Policies.TasksRead);
        tasks.MapGet("/", ListTasksAsync);
        tasks.MapGet("/{id}", GetTaskAsync);
        return group;
    }
}
```

### Async
- All I/O is async. No sync-over-async (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`).
- `CancellationToken` is the **last parameter** of every async method and is always passed on. Name it `ct` (or `cancellationToken` in public API surfaces).
- No `async void` (except event handlers, which should not exist in this codebase).
- `ConfigureAwait(false)` is not used (ASP.NET Core has no synchronization context).
- Long-running loops in hosted services respect `stoppingToken`.
- Use `Channel<T>` for producer/consumer, not hand-rolled queues with locks.

### Time
- Always inject `TimeProvider`. Never `DateTime.Now`, `DateTime.UtcNow` or `DateTimeOffset.Now`.
- Store and pass `DateTimeOffset` in UTC. Convert to local time only for display and for prompts.
- Timers and delays use `TimeProvider` (`CreateTimer`, `Task.Delay(timeSpan, timeProvider, ct)`), so tests control time.

### Logging
- Structured logging with source-generated `[LoggerMessage]` methods.
- Message templates with named placeholders, never string interpolation.
- **Never log secrets or content**: no tokens, passwords, mail bodies, note contents or full prompts. Log ids and references instead. Run content goes through `IRunRecorder`, which applies redaction.
- Log at boundaries (incoming request, tool execution, external call), not in every method.

### LINQ and collections
- LINQ for querying and transforming; loops when there are side effects or when performance matters.
- Materialize (`ToList()`, `ToArray()`) once, deliberately. Don't enumerate an `IEnumerable<T>` twice.
- Return empty collections, never `null`.

### Comments and documentation
- XML doc comments on **public interfaces and abstractions** (everything in `Marshal.Abstractions`, public types used across projects).
- Elsewhere, comments explain **why**, not what. If code needs a "what" comment, rename or extract instead.
- No commented-out code. Git has the history.
- `TODO` comments reference an issue or OpenSpec change.

### Control flow and formatting
- Always use braces, except for single-line guard clauses (`if (x is null) return ...;`). The analyzer can only enforce "braces when multiline"; "braceless only for guard clauses" is checked in review.
- Early returns over deep nesting.
- Expression-bodied members for one-line members only.
- Pattern matching (`is`, `switch` expressions) over type checks and casts.
- `is null` / `is not null`, never `== null`.

### Security-relevant code
- Code that decides risk tiers, visibility, confirmation or ownership has **100 % test coverage of its branches** and fails closed.
- No custom cryptography. Use ASP.NET Core Data Protection, Identity and standard auth handlers.
- Secrets come only through `ISecretStore` (environment / `/run/secrets` files, or Data Protection-encrypted DB rows), never from `/config`. Options properties holding secrets are marked `[Secret]` and fail startup if bound from a config file.

---

## Part 3 — Tests

- **TDD:** write the failing test first, then the code.
- **Frameworks:** xUnit with plain `Assert`. No assertion libraries.
- **No mocking library.** Use hand-written fakes for our small interfaces (`FakeChatClient`, `FakeEmbeddingGenerator`, `FakeMailProvider`, `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`). Shared fakes live in a `Marshal.Testing` project.
- **Tests never need a real LLM or live account.** Models and embeddings are always faked; external services run in Testcontainers or as recorded fakes.
- **Naming:** `MethodOrBehavior_Condition_ExpectedResult`, e.g. `Confirm_KeywordWithPunctuation_ExecutesFrozenAction`.
- **Structure:** Arrange / Act / Assert, separated by blank lines. One behavior per test.
- **Ownership tests:** data-access code is tested with a second fake user to prove there is no leakage.
- Test projects mirror source projects: `src/Marshal.Memory` → `tests/Marshal.Memory.Tests`.
