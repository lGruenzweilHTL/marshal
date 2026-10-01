# Design

## Context

Startup checks happen in several places today (see proposal.md, Why):

- `MarshalApp.Create` builds configuration (a `marshal.yaml` parse error throws here), builds the app and reads `IOptions<HttpOptions>` before the host starts.
- `Host.StartAsync` resolves every `IHostedService`. Composition-root factories such as the embedding generator (`CoreServiceCollectionExtensions.cs:73`) read `IOptions<T>.Value` while they are constructed, so an invalid option surfaces as an `OptionsValidationException` deep inside DI resolution. Only that one options type is reported.
- `MarshalStartup.StartingAsync` checks in sequence and throws a bare `InvalidOperationException` at the first failing check: `marshal.yaml` literal-secret scan, `risk.yaml` / `satellites.yaml` (via `YamlConfigFile.Load`), database init, secret binding (from-configuration and missing-required mixed in one `SecretBinder.BindAsync` call), literal secrets in MCP / skill / template files, then MCP start and tool registry.
- The host logs `Hosting failed to start` with the full exception, then the runtime prints `Unhandled exception.` with the trace again.

Constraints:
- Secrets can come from the encrypted database (`DatabaseSecretSource`), so secret resolution needs an initialized, migrated database.
- In-process API tests (`MarshalApiFactory : WebApplicationFactory<Program>`) observe startup failures as exceptions and assert on messages with `StartupErrors.Flatten`. They must keep working.
- Options validator messages already start with the full key path (`embedding.model is required.`, `agent.maxSteps must be between 1 and 50.`). `ValidateDataAnnotations` wraps those messages in its own text.
- `YamlConfigFile`, the MCP / skill / template catalogs and `ConfigFileError` already report file-level errors without values.

## Goals / Non-Goals

**Goals:**
- One place that decides what a startup failure looks like: `Program.cs`.
- Configuration and secret checks return error lists; they do not throw.
- An invalid option never reaches a DI factory during hosted-service resolution.

**Non-Goals:**
- Changing how hot reload reports errors.
- Structured (JSON) error output.
- Reworking `MarshalCli` / `OpenApiExport` error output.

## Decisions

### 1. `StartupError` record plus one boundary exception

`StartupError(string Source, string? Key, string Message)` in `Marshal.Abstractions.Configuration`. `Source` is a file (`marshal.yaml`, `risk.yaml`, `mcp/search.yaml`) or an environment variable name. Checks return `IReadOnlyList<StartupError>`.

When a stage has errors, the stage runner throws `StartupFailedException(IReadOnlyList<StartupError> errors)` (in `Marshal.Api.Hosting`). This is the only throw for expected failures. It crosses the hosting boundary because `IHostedLifecycleService.StartingAsync` cannot return a result, and the in-process tests observe startup through that same boundary. Its `Message` lists all errors, so `StartupErrors.Flatten` keeps working.

Alternatives:
- A typed exception thrown at every check site: no aggregation, and it spreads exceptions for expected failures, which the style guide forbids.
- Checks run from `Program.cs` after `Create` and before `RunAsync`, returning `Result`: no exception at all, but `WebApplicationFactory` starts the host itself and would skip the checks. The database init would move out of the host, so the tests would need their own path.

No new interface. Startup checks are part of the composition root, not a subsystem with alternative implementations.

### 2. Stage 1 runs after the app is built, before the host starts

`MarshalHost.RunAsync` runs the configuration stage right after `MarshalApp.Create` and before `StartAsync`. `Create` stays pure composition: `--export-openapi` builds the app without starting it and without any real configuration, so it must not run the checks. Reading `HttpOptions` inside `Create` is harmless because it has no validation. The stage covers:

- Options: resolve `IStartupValidator` (the service behind `ValidateOnStart`, which validates every registered options type and aggregates all failures) and flatten its `OptionsValidationException.Failures`.
- `marshal.yaml` literal-secret scan (moved from `MarshalStartup.ScanMainFile`).
- `risk.yaml` and `satellites.yaml`: `YamlConfigFile.Load` gains a non-throwing variant that returns the first-load error; the DI registration consumes the already-loaded instance.
- Literal secrets in MCP server, skill and template files: the catalogs' `Errors` with `LiteralSecret`. The catalogs do not resolve secrets while constructed (they resolve lazily), so no database is touched.
- `[Secret]` properties bound from configuration: `SecretBinder` splits into a pure "set in configuration" scan and the resolving bind.

Because this runs before `Host.StartAsync`, no hosted-service constructor can trip over an invalid option first. The user's original failure (missing `embedding`) is the first regression test.

Alternative: rely on `ValidateOnStart` ordering inside `Host.StartAsync`. The observed trace shows hosted-service resolution failing before validation reports, so the ordering cannot be relied on.

### 3. Stages 2–4 stay in `MarshalStartup.StartingAsync`

Database init (stage 2) stays first. Stage 3 resolves every required secret, collects `MissingRequired` across all bindings and throws `StartupFailedException` once. Stage 4 (MCP start, tool registry) is unchanged; any exception there is unexpected.

### 4. `marshal.yaml` parse errors

The YAML configuration provider loads lazily with a plain `ConfigurationBuilder` and wraps parse errors in its own exceptions. `AddMarshalConfiguration` therefore parses `marshal.yaml` once with YamlDotNet before adding the provider and throws `StartupFailedException` with a single `StartupError("marshal.yaml", null, "is not valid YAML (line L, column C).")`. The parser message is not included because it can quote content; it stays available as the inner exception, so it appears in the Debug-level stack trace.

### 5. Key-to-source resolution

Validator messages keep the convention "message starts with the full key path". The reporter splits the leading key path (`embedding.model`, `printers[0].apiKeySecret`) from the rest, converts it to a configuration path (`embedding:model`, `printers:0:apiKeySecret`) and asks the configuration providers, highest priority first, which one supplies it:
- the `MARSHAL__` environment provider: the source is the original variable name (looked up in the environment dictionary that `AddMarshalConfiguration` already receives);
- otherwise, including a missing key: `marshal.yaml`.

A message with no leading key path is reported with source `marshal.yaml` and no key.

`ValidateDataAnnotations()` is replaced by a small `ValidateAttributes()` extension that reports each attribute's `ErrorMessage` unwrapped (3 call sites: `agent`, `confirmation`, `sessions`).

### 6. Output and exit code

`MarshalHost.RunAsync(args, environment, report)` creates and starts the app inside one `try`, then waits for shutdown outside it, so only start failures are reported:

```
marshal: startup failed (2 errors)
  marshal.yaml  embedding.provider  must be ollama or openai-compatible.
  marshal.yaml  embedding.model  is required.
```

- `StartupFailedException` (also when the host wraps a single one in an `AggregateException`): the list as above. The stack trace follows only when the effective `logging:logLevel:default` is `Debug` or `Trace`. For a `marshal.yaml` parse error, where no configuration exists yet, the level is read from the `MARSHAL__` variables. The exception carries the flag, computed where configuration is available.
- Any other exception: `marshal: startup failed: <Type>: <Message>` followed by the stack trace.
- Exit code `1` in both cases; `0` after a normal shutdown.

`WebApplicationFactory` runs `Main` in-process and observes a failed start only through the exception escaping `Main`. `Program` therefore passes `Console.Error` as the report writer only when Marshal is the process entry assembly; under the test host it passes `null` and the failure propagates. Tests call `MarshalHost.RunAsync` directly with a `StringWriter` to check the report and the exit code, so no child process is needed.

The host's own `Hosting failed to start` log (category `Microsoft.Extensions.Hosting.Internal.Host`, level `Error`) would print the trace again. `AddMarshalConfiguration` adds a built-in default layer, below `marshal.yaml`, that sets that category's level to `Critical`. Marshal keeps the default `BackgroundServiceExceptionBehavior.StopHost`, under which a faulted background service is still logged at `Critical` with its exception. Only the duplicate `Error`-level fault line and the `Debug`-level start/stop lines of that category are lost. An operator can lower the level again in `marshal.yaml`.

Alternative: a console-provider wrapper that drops only the one event id. It is more precise but needs custom provider code for one log line.

### 7. Embedding presence

`ModelRoutingOptionsValidator` already reports `embedding.*`. The `?? throw new InvalidOperationException("embedding is not configured.")` in the composition root becomes unreachable after stage 1 and is replaced by `.Value.Embedding!` with a comment that the validator guarantees it.

## Risks / Trade-offs

- [`WebApplicationFactory` interception may not run code placed after `builder.Build()` before it starts the host] → The missing-embedding API test covers it: the factory runs `Main`, including the configuration stage in `MarshalHost`, before it starts the host.
- [Category filter depends on a hosting-internal category name] → A .NET upgrade that renames the category brings back a duplicate trace in the log; the report itself is unaffected.
- [Entry-assembly check decides between report and propagation] → `MarshalHostTests` cover both paths; the in-process API tests fail if the check ever picks the report under the test host.
- [Key-path parsing depends on a message convention] → An architecture test asserts every validator message in the repo's options validators starts with a lowercase key path.
- [Stage 1 misses a check that only fires later] → Later failures still print cleanly as an unexpected failure with a trace; the user only loses aggregation for that one error.

## Migration Plan

No data or config migration. Operators see the new output on the next failing start. Rollback is a revert.
