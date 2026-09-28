## 1. Solution setup

- [x] 1.1 Create `Marshal.sln`, `Directory.Build.props` (net10.0, nullable, warnings as errors, analyzers, `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`) and `Directory.Packages.props`
- [x] 1.2 Create projects `Marshal.Abstractions`, `Marshal.Core`, `Marshal.Data`, `Marshal.Api` with references as in `packages.puml`
- [x] 1.3 Create test projects `Marshal.{Abstractions,Core,Data,Api}.Tests` (xunit.v3) and `Marshal.Testing`
- [x] 1.4 Add `FakeChatClient` (scripted replies and tool calls), `FakeEmbeddingGenerator`, `FakeTool` variants and a `TestExecutionContext` to `Marshal.Testing`
- [x] 1.5 Update `CLAUDE.md` repository map and `docs/diagrams/packages.puml` for the created projects; re-render SVG

## 2. Abstractions and Result

- [x] 2.1 Write tests for `Result` / `Result<T>` (success, failure code + message, mapping)
- [x] 2.2 Implement `Result` / `Result<T>` and error type in `Marshal.Abstractions`
- [x] 2.3 Write tests for strongly typed ids (equality, parsing, JSON round trip)
- [x] 2.4 Implement `OwnerId`, `SessionId`, `RunId`, `PendingActionId`, `ApiTokenId`, `SecretName` and the execution context types

## 3. Persistence and ownership

- [x] 3.1 Write ownership tests: two owners, each store returns only the current owner's rows; query without context throws
- [x] 3.2 Implement `MarshalDbContext`, value converters, snake_case naming and global owner filters (Secret: owner or null)
- [x] 3.3 Add entities `User`, `UserPreferences`, `ApiToken`, `Session`, `Run`, `RunStep`, `PendingAction`, `AuditEvent`, `Secret` with the indexes from `erd.puml` (incl. the two partial unique indexes on `Secret`)
- [x] 3.4 Write test: pending migration triggers a timestamped DB copy before migrating
- [x] 3.5 Implement startup migration with backup and instance-owner seeding; create the initial migration
- [x] 3.6 Update `docs/diagrams/erd.puml` if any column differs from the plan; re-render SVG

## 4. Configuration and secrets

- [x] 4.1 Write tests for the config secret scanner (reference-key pattern, forbidden key words incl. false-positive cases `maxTokens`, `tokenUrl`, value patterns incl. 32-hex and base64)
- [x] 4.2 Implement `ConfigSecretScanner` over the raw YAML tree
- [x] 4.3 Write tests for layering (YAML < `MARSHAL__` env) and that `MARSHAL_SECRET_*` never enters `IConfiguration`
- [ ] 4.4 Implement configuration loading (`NetEscapades.Configuration.Yaml`, env prefix `MARSHAL__`), options with `ValidateOnStart`
- [x] 4.5 Write tests for hot reload keeping the last good value on invalid files
- [ ] 4.6 Implement validating `IOptionsMonitor` wrapper and file watching for `risk.yaml` and `satellites.yaml`
- [x] 4.7 Write tests for `ISecretStore`: env / file source, DB source encryption, lookup order for config vs user-owned references, not-found result
- [x] 4.8 Implement `SecretStore`, `EnvironmentFileSecretSource`, `DatabaseSecretSource` (Data Protection, keys in `/data/keys`)
- [x] 4.9 Write test: required secret missing fails startup; optional missing yields health warning
- [ ] 4.10 Implement `[Secret]` binding and required-secret check at startup

## 5. Model routing

- [x] 5.1 Write router tests: tier + capability selection, no match → failed result
- [x] 5.2 Implement `ConfiguredModelRouter` and model options (tier, provider, flags, price)
- [x] 5.3 Write `PriceTableCostCalculator` tests (priced, cached tokens, unpriced = 0)
- [x] 5.4 Implement `PriceTableCostCalculator`
- [x] 5.5 Wire provider clients in `Marshal.Api` (Ollama, OpenAI-compatible, Anthropic); add a build test asserting no other project references provider packages

## 6. Tool registry and visibility

- [x] 6.1 Write registry tests: id ↔ name mapping, namespace pattern, reserved namespaces, sanitizing, 64-char truncation with hash, collision rejects the later source
- [x] 6.2 Implement `ITool`, tool descriptors (`defaultTier`, `untrustedOutput`, `persists`, `sensitive`), `IToolSource`, `ToolRegistry`
- [x] 6.3 Write visibility tests: forbidden hidden, `denyTools` hidden, confirm-only tool hidden under `denyConfirm`, arg-aware tool visible, tool-specific visibility conditions
- [x] 6.4 Implement `ToolVisibilityFilter`

## 7. Risk engine and guard

- [x] 7.1 Write risk rule tests: glob matching, first match wins, target conditions evaluated per resolved target with max tier, unresolvable / empty / no resolver → confirm with `unresolved_target`
- [x] 7.2 Implement `RuleBasedRiskEngine` and `IRiskTargetResolver` seam
- [x] 7.3 Write guard tests: satellite `denyConfirm` denial on arg-aware call, taint escalation of `persists` tools, tiers only raised, reasons collected and localized, `taint` spoken first
- [x] 7.4 Implement `Guard` pipeline and reason localization (de / en)
- [x] 7.5 Write pending-action tests: frozen before question, one per session, superseded, expiry, frozen args unchanged on execute
- [x] 7.6 Implement `EfPendingActionStore` and freeze / execute logic
- [x] 7.7 Write keyword tests: normalization, full-utterance only, per-language lists
- [x] 7.8 Implement `ConfiguredConfirmationKeywords`
- [x] 7.9 Write judge tests with a fake client: positive executes, neutral drops and continues as a normal turn, negative cancels, timeout and parse errors drop
- [x] 7.10 Implement `LlmConfirmationJudge` (fast tier, structured output, 3 s timeout)
- [ ] 7.11 Write test: tool output containing a confirmation keyword never confirms
- [ ] 7.12 Verify 100 % branch coverage for risk engine, visibility filter and guard

## 8. Context builder

- [x] 8.1 Write tests: contributor order, user profile from owner, voice vs text output rules, reply language, time in owner's time zone, untrusted-data wrapping
- [x] 8.2 Implement `ContextBuilder` and base contributors
- [x] 8.3 Write satellite tests: profile notes, defaults for unknown satellites, area name in context
- [x] 8.4 Implement `YamlSatelliteContextProvider`

## 9. Agent loop

- [x] 9.1 Write loop tests with scripted fake client: tool call then reply, step limit, failed tool result passed to model, unknown tool name denied, taint set after untrusted tool result
- [x] 9.2 Implement `AgentRunner`
- [x] 9.3 Write advisor tests: no tools on advisor call, hidden in voice turns, per-run limit, daily cap, hidden without advisor model
- [x] 9.4 Implement `agent.ask_advisor`

## 10. Sessions and identity

- [x] 10.1 Write session tests: lookup by `(api_token_id, external_id)`, same id with different token = different session, owner pinned, per-turn data on run, idle close drops pending action
- [x] 10.2 Implement `EfSessionStore`, `InstanceOwnerIdentityResolver`, `AsyncLocalExecutionContextAccessor`, idle-close hosted service

## 11. Tokens

- [ ] 11.1 Write token tests: hash only stored, revoked / expired rejected, scope policy check
- [ ] 11.2 Implement `ApiTokenService` and bearer authentication handler
- [ ] 11.3 Write CLI tests: `tokens create` prints once and audits, `list`, `revoke`
- [ ] 11.4 Implement the `marshal tokens` command mode

## 12. Conversation API

- [ ] 12.1 Write API tests (`WebApplicationFactory`): versioned route, unsupported version, `/api/versions`, OpenAPI v1 document, camelCase JSON
- [ ] 12.2 Implement API versioning (`ApiVersions.V1`, versioned route group), OpenAPI per version, base path
- [ ] 12.3 Write turn endpoint tests: missing scope 403, SSE `delta` / `status` / final events, id echo, `continueConversation` on pending action, keyword confirmation turn, unknown `followupRef` ignored
- [ ] 12.4 Implement `POST /api/v1/conversation/turns` with `TypedResults.ServerSentEvents`
- [ ] 12.5 Write latency cap tests with `FakeTimeProvider` (voice and text caps, outcome `cancelled` / `latency_cap`)
- [ ] 12.6 Implement the latency cap
- [ ] 12.7 Write `/health` tests (DB, config, required secrets, warnings without values)
- [ ] 12.8 Implement `/health`

## 13. Run recording

- [ ] 13.1 Write recorder tests: step order and fields, cost aggregation, redaction of sensitive tool results, audit events
- [ ] 13.2 Implement `DbRunRecorder`, `DbAuditLog`, OpenTelemetry sink and `[LoggerMessage]` logging at boundaries

## 14. Packaging

- [ ] 14.1 Add multi-stage `Dockerfile` (API only for now) and `deploy/docker-compose.yml` with Ollama
- [ ] 14.2 Add example `deploy/config/marshal.yaml`, `risk.yaml`, `satellites.yaml` (no secrets; passes the scanner in a test)
- [ ] 14.3 Update `CLAUDE.md` package index if packages changed
