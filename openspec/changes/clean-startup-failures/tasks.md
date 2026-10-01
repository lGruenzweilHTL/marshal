# Tasks

## 1. Error model and reporter

- [x] 1.1 Write failing tests for the startup reporter: a list of `StartupError`s prints as `marshal: startup failed (N errors)` plus one `source  key  message` line each; no stack trace unless verbose; an unexpected exception prints a one-line summary and its stack trace. Verify the tests fail.
- [x] 1.2 Add `StartupError` to `Marshal.Abstractions.Configuration`, `StartupFailedException` (message lists every error, carries the verbosity flag) and the reporter in `Marshal.Api.Hosting`. Verify the 1.1 tests pass.
- [x] 1.3 Write failing tests for key-to-source resolution: a leading key path such as `printers[0].apiKeySecret` maps to `printers:0:apiKeySecret`; a key set by a `MARSHAL__` variable reports the original variable name; a key from `marshal.yaml` or a missing key reports `marshal.yaml`; a message without a key path reports `marshal.yaml` and no key. Verify the tests fail.
- [x] 1.4 Implement key-to-source resolution over the configuration providers and the environment dictionary. Verify the 1.3 tests pass.

## 2. Configuration stage (runs after `Build()`)

- [x] 2.1 Write a failing API test for the original failure: config without `embedding` fails startup with a `StartupFailedException` that lists `embedding.provider` and `embedding.model`, both with source `marshal.yaml`, and no `OptionsValidationException` from DI resolution. Verify it fails. If `WebApplicationFactory` starts the host before code after `Build()` runs, apply the fallback in design.md (Risks) before continuing.
- [x] 2.2 Run options validation through `IStartupValidator` after `MarshalApp.Create` and before the host starts (in `MarshalHost`, so `--export-openapi` keeps building without configuration), and throw `StartupFailedException` with all failures. Verify 2.1 and the `OpenApiExportTests` pass.
- [x] 2.3 Write a failing test that `agent.maxSteps: 0` reports the message `agent.maxSteps must be between 1 and 50.` unwrapped (not the DataAnnotations wrapper text). Replace `ValidateDataAnnotations()` with a `ValidateAttributes()` extension at the `agent`, `confirmation` and `sessions` registrations. Verify the test passes.
- [x] 2.4 Write a failing architecture test that every options validation message under `src/` starts with a lowercase key path. Fix any message that does not. Verify the test passes.
- [x] 2.5 Write a failing test that invalid `risk.yaml` plus a missing `embedding.model` reports both errors in one failure. Add a non-throwing `YamlConfigFile` load, run `risk.yaml` and `satellites.yaml` in the configuration stage, and register the loaded instances. Verify the test passes and the existing hot-reload tests still pass.
- [x] 2.6 Move the `marshal.yaml` literal-secret scan and the MCP / skill / template literal-secret check into the configuration stage. Verify the existing literal-secret API tests (`ConversationApiTests`, `PrinterHostTests`) pass and a test with a literal secret plus an invalid option reports both.
- [x] 2.7 Write a failing `SecretBinder` test that the "set in configuration" scan runs without any `ISecretStore` call. Split the scan out of `BindAsync` and run it in the configuration stage. Verify the test and the existing `printers[0].apiKey` test pass.
- [x] 2.8 Write a failing test that a failing configuration stage never opens or migrates the database (no SQLite file in the data directory). Verify it passes with the stage ordering from 2.2–2.7.
- [x] 2.9 Replace the `?? throw new InvalidOperationException("embedding is not configured.")` fallback in `CoreServiceCollectionExtensions` with the validator-guaranteed value and a comment. Verify 2.1 still passes.

## 3. `marshal.yaml` parse errors

- [x] 3.1 Write a failing test that a `marshal.yaml` with invalid YAML fails startup with exactly one `StartupError` naming `marshal.yaml`, the line and the column, and no offending value. Verify it fails.
- [x] 3.2 Catch the YAML provider's parse exception in `MarshalApp.Create` and convert it. Confirm the exception type against the installed `NetEscapades.Configuration.Yaml` version. Verify 3.1 passes.

## 4. Secret stage

- [x] 4.1 Write a failing test that a missing `ha.tokenSecret` and a missing `anthropic` model key are both reported in one failure, each naming its key and reference. Verify it fails.
- [x] 4.2 Collect `MissingRequired` across all bindings in `MarshalStartup.StartingAsync` after database init and throw one `StartupFailedException`. Verify 4.1 and the existing `HomeAssistantHostTests` / `ConversationApiTests` secret tests pass.

## 5. Process output and exit code

- [x] 5.1 Write failing tests for `MarshalHost.RunAsync` with a `StringWriter` report (in-process; `WebApplicationFactory` cannot observe the exit code): a missing `embedding.model` returns `1`, the report lists the error and contains no `   at ` stack frame; with `MARSHAL__Logging__LogLevel__Default=Debug` the stack trace appears; without a report writer the failure propagates. Verify they fail.
- [x] 5.2 Add `MarshalHost.RunAsync` and route `Program.cs` through it (report to `Console.Error` only when Marshal is the entry assembly). Add the built-in default `Logging:LogLevel:Microsoft.Extensions.Hosting.Internal.Host: Critical` layer with a test that `marshal.yaml` can override it. Verify 5.1 passes.
- [x] 5.3 Write a test that an unexpected startup failure (HTTP port already bound) returns `1` and reports a one-line summary followed by the stack trace. Verify it passes.

## 6. Documentation and integration

- [x] 6.1 Update README (getting started / troubleshooting, if present) with what a failed start looks like and how to get the stack trace (`logging.logLevel.default: Debug`). Verify the documented setting produces the trace by running 5.1's debug case.
- [x] 6.2 Run the published `marshal.dll` (`dotnet src/Marshal.Api/bin/Debug/net10.0/marshal.dll`) with a config missing `embedding`, or build the Docker image and start it with that config. Verify the output shows only the error list and the process exits with `1`.
- [ ] 6.3 Run the full test suite (`dotnet test`) and `openspec validate clean-startup-failures --strict`. Verify both pass.
