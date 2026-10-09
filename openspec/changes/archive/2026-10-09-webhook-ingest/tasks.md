# Tasks

Groups 1 and 2 are independent. Group 3 needs both.

## 1. Event trust

- [x] 1.1 Write failing `Marshal.Tasks.Tests`: an untrusted event's run gets the payload wrapped and is tainted with `event`; a trusted event's run gets it unwrapped and is not tainted; an untrusted event whose payload contains `trusted: true` is still tainted. Verify they fail.
- [x] 1.2 Add `Trusted` (default `false`) to `MarshalEvent`, the top-level `trusted` key in `EventPayloads.Serialize`, and the check in `AgentTaskRunner.PayloadTaint`. Verify the 1.1 tests and the existing event and task tests pass.

## 2. Uptime Kuma normalizer

- [x] 2.1 Add recorded Uptime Kuma bodies as test fixtures: JSON and form-data down, up, pending and maintenance heartbeats; certificate-expiry notice; test notification; an unknown notice; invalid JSON. Verify the fixture loader test passes. (Bodies are built from Uptime Kuma's webhook provider source, not recorded from a live instance.)
- [x] 2.2 Add `IWebhookNormalizer`, `WebhookRequest` and `WebhookEvent` to `Marshal.Abstractions/Events`. Write failing `UptimeKumaNormalizer` tests over the fixtures: status mapping, entity and payload fields, `certificate.expiring` with `days`, nothing for test and unknown notices, failure for invalid JSON, no `msg` text anywhere in any payload, `Trusted = true`. Verify they fail.
- [x] 2.3 Implement `UptimeKumaNormalizer`. Verify the 2.2 tests pass.

## 3. Ingest, endpoint and scopes

- [x] 3.1 Write failing ingest-service tests with `FakeTimeProvider`: publishes normalized events with owner, source, time and trust; drops a repeat of the same owner, source, type and entity within the window; publishes a different type within the window; publishes again after the window; updates the source's last received time. Verify they fail, then implement the service in `src/Marshal.Tasks/Webhooks` with `webhooks.debounceSeconds`. Verify they pass.
- [x] 3.2 Write failing tests that `ApiScopes.All` contains `hooks:uptime-kuma` when the Kuma normalizer is registered, that the `admin` role grants it, and that `marshal tokens create --scope hooks:uptime-kuma` succeeds. Implement them. Verify they pass.
- [x] 3.3 Write failing `Marshal.Api.Tests` for `POST /api/v1/hooks/{source}`: 202 and a published event for a valid Kuma body; 404 for an unknown source; 403 without the source scope; 401 for a revoked token; 413 for a 100 KB body; 400 for invalid JSON; 202 and no event for the test notification; 429 after the per-token limit (`webhooks.requestsPerMinute`). Verify they fail, then add `HookEndpoints` on the v1 route group with the size limit and the rate-limiter policy. Verify they pass and the OpenAPI document lists the endpoint.
- [x] 3.4 Write a failing test that the event sources response includes `uptime-kuma` with its last received time, and a web component test that the sources list shows it (German and English). Implement the additive field and the UI. Verify `dotnet test` for the API project, `npm test` and `npm run build` pass.
- [x] 3.5 Add the `webhooks` section (debounce, requests per minute) and the Uptime Kuma setup steps as comments to `deploy/config.example/marshal.yaml`. Verify the example-config test passes.
- [x] 3.6 Update the CLAUDE.md repository map (`Marshal.Tasks`: webhook ingest) and `docs/diagrams/packages.puml` only if a project reference changed. Verify the entries match the code.

## 4. Integration check

- [x] 4.1 Write an end-to-end test with a fake `IChatClient`: a watch on `source: uptime-kuma, type: monitor.down, entity: NAS`; a POST of a Kuma down body fires the watch once, the run is not tainted and its model input contains the unwrapped payload. Run the full .NET and web suites and `openspec validate webhook-ingest --strict`. Verify all pass.
