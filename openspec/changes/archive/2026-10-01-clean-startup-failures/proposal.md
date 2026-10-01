# Proposal

## Why

When configuration is wrong, Marshal crashes with an unhandled exception and a long DI stack trace (e.g. `OptionsValidationException` thrown from inside service resolution). The actual message is hard to find, and startup stops at the first failing check, so an operator needs one restart per mistake. Voice-first self-hosting means the container log is often the only feedback an operator gets, so startup failures must be readable on their own.

## What Changes

- Startup reports configuration errors as a plain list, one entry per error, naming the file and key, without a stack trace. Values are never printed (already true for secrets; now true for every config error).
- Startup collects all configuration errors of a stage before failing instead of stopping at the first one:
  - **Configuration stage** (no database needed): options validation for every section, `marshal.yaml` parse and literal-secret scan, `risk.yaml` and `satellites.yaml`, literal secrets in MCP server, skill and template files, `[Secret]` properties bound from configuration.
  - **Secret stage** (runs after the database is initialized, because secrets can live in the encrypted database): every unresolved required secret.
  - A later stage runs only when the stages before it found no errors.
- Unexpected startup failures (bugs, database migration errors, port already in use) print a one-line summary followed by the stack trace.
- Stack traces of configuration errors are printed only when the configured log level is `Debug` or `Trace`.
- Every startup failure exits with code `1`. Docker's restart loop is accepted.
- The "embedding is not configured" fallback in the composition root becomes an options validation error, so it is reported like every other missing key.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `configuration`: the "Key naming" requirement's startup validation gains reporting rules; a new requirement defines startup failure reporting (all errors per stage, file and key, no stack trace for configuration errors, exit code `1`).

## Non-goals

- `Marshal.ExecutorHost` startup. It has its own deployment and a single option; it can adopt the same reporting later.
- Graceful shutdown on `SIGTERM` (draining runs and task workers).
- Docker restart policy changes, a "degraded mode" that keeps the server up with a config error page, or distinct exit codes per failure kind.
- Hints pointing at example files; errors name only the file and key.
- Hot-reload failures. The spec already keeps the previous configuration and logs the error.
- Rewording existing error messages beyond what is needed to name the file and key.

## Impact

- `src/Marshal.Api/Program.cs`, `src/Marshal.Api/Hosting/MarshalApp.cs`, `src/Marshal.Api/Hosting/MarshalStartup.cs`: top-level failure handling, staged checks run before the host starts serving.
- `src/Marshal.Core/Configuration/YamlConfigFile.cs` and the secret checks: report errors instead of throwing a bare `InvalidOperationException`.
- `src/Marshal.Core/CoreServiceCollectionExtensions.cs`, `src/Marshal.Core/Models/ModelRoutingOptionsValidator.cs`: embedding presence validated as an option.
- Options validators across integration projects: messages name the key with its full config path.
- Tests in `tests/Marshal.Api.Tests` (startup output and exit code) and `tests/Marshal.Core.Tests`.
- No API, database or dependency changes.
