# Design

## Context

See proposal.md (Why). Current state:

- `MarshalEvent(Source, Type, Entity, Payload, OwnerId?, Time)`; sources implement `IEventSource` (name + connected) and publish through `IEventPublisher`.
- `DeclarativeTriggerMatcher` makes watches due with `EventPayloads.Serialize(event)` as trigger payload: `{ source, type, entity, time, payload }`.
- `AgentTaskRunner.PayloadTaint` wraps every event trigger payload as untrusted (`EventTaint = "event"`); approval decisions, spoken replies and executor outcomes have their own rules. This rule is in code only; the change writes it into the `event-bus` spec.
- Machine tokens: `ApiScopes` is a fixed set (`conversation:write`, `tasks:read`, `tasks:write`, `admin`); roles grant scopes to cookie principals (`ScopeClaimsTransformation`); token creation only grants scopes the creator holds.
- `ExecutorCallbackEndpoints` is the existing inbound machine endpoint (own auth scheme, excluded from OpenAPI).
- Uptime Kuma's webhook provider posts `{ heartbeat, monitor, msg }` as JSON (or form-data with a `data` field, or a custom template body), with optional "Additional Headers" as JSON, so a Bearer token header works.

## Goals / Non-Goals

**Goals:**
- Adding the next source (Proxmox) is one normalizer class, one scope constant and tests.
- Trust is decided by code that a reviewer can read in one place per source.

**Non-Goals:**
- Persisting received webhooks or replaying them.
- Uptime Kuma's "custom body" mode; JSON and form-data only.

## Decisions

### 1. `IWebhookNormalizer` per source

```csharp
public interface IWebhookNormalizer
{
    string Source { get; }          // "uptime-kuma"; also the scope suffix and route value
    bool Trusted { get; }
    Result<IReadOnlyList<WebhookEvent>> Normalize(WebhookRequest request);  // 0..n events
}
```
`WebhookRequest` carries content type and body bytes; `WebhookEvent` is type, entity and payload (the ingest adds source, owner, time and trust). Normalizers are pure (no I/O), so they are tested with recorded bodies. v1 implementation: `UptimeKumaNormalizer`.

Location: new folder `src/Marshal.Tasks/Webhooks` for the ingest service (debounce, publish, source state) and the Uptime Kuma normalizer; the interface in `Marshal.Abstractions/Events`. A project per webhook source would be heavier than the code it holds; when a source needs its own API client (Proxmox), that client goes to its integration project and the normalizer can live there.

Alternatives: one generic normalizer configured by JSONPath in `/config` — flexible, but trust would then come from config, which the spec forbids.

### 2. Trust carried on the event, not in the payload

`MarshalEvent` gains `bool Trusted = false` (last positional parameter with default, so existing sources compile unchanged). `EventPayloads.Serialize` adds a top-level `trusted: true` only for trusted events; the event's own data stays nested under `payload`, so content cannot reach the top-level key. `PayloadTaint` returns `null` for an event trigger whose top-level `trusted` is `true`, else `EventTaint` as today.

Alternative: a `trigger_payload_trusted` column — spoof-proof by construction, but a migration for one bit that the serializer already controls.

### 3. Endpoint, auth and limits

`POST hooks/{source}` on the versioned v1 route group, in `src/Marshal.Api/Hooks/HookEndpoints.cs`, authenticated with the standard token scheme. Authorization is a per-request check: resolve the normalizer by `source` (404 if none), then require the claim `scope = hooks:<source>` (403). Unknown source is checked first, so the response does not reveal which scopes a token has. Included in the OpenAPI document (it is a public, versioned contract).

`ApiScopes` gains `HooksPrefix = "hooks:"` and `Hooks(string source)`; `All` is built from the fixed scopes plus `hooks:<source>` for every registered normalizer. The `admin` role grants the hook scopes.

Limits: `RequestSizeLimit(64 KB)` on the endpoint; ASP.NET Core's built-in rate limiter with a fixed-window policy partitioned by token id (`webhooks.requestsPerMinute`, default 60). Debounce: an in-memory dictionary keyed by owner + source + type + entity with the last publish time (`TimeProvider`), entries older than the window pruned on access. Lost on restart; acceptable.

### 4. Uptime Kuma mapping

| Body | Event |
|---|---|
| `heartbeat.status` 0 / 1 / 2 / 3 | `monitor.down` / `up` / `pending` / `maintenance` |
| no heartbeat, monitor set, msg matches `Certificate will expire in (\d+) days` | `certificate.expiring` with `days` |
| no heartbeat, monitor set, other msg | nothing (unknown notice; logged at debug) |
| neither | nothing (test notification) |

Entity = `monitor.name`. Payload: `monitorId`, `name`, `type`, `url` (when present), `time`, `pingMs`, `important`. No `msg` anywhere. The regex matches Uptime Kuma's English message; the language of these messages is not localized server-side (verify against the installed version when building).

### 5. Event source state

The ingest registers one `IEventSource` per normalizer: `Source` = name, `IsConnected` = a webhook was received in the last 24 hours. The UI's event sources list gets a "last received" time (additive field on the existing response).

New interfaces: `IWebhookNormalizer` (v1: `UptimeKumaNormalizer`).

## Risks / Trade-offs

- [Trusted events relax the existing rule] → only code can declare trust, and the Kuma normalizer is the single trusted source; its tests assert that no `msg` text appears in the payload. Monitor names and URLs are the owner's own Kuma configuration.
- [Monitor names are owner-controlled but stored in Kuma, a second system] → someone with access to Kuma could name a monitor with instructions. Accepted: Kuma admin access is equivalent to infrastructure access.
- [Debounce state lost on restart] → at most one duplicate event after a restart.
- [Kuma message wording changes] → certificate notices stop being recognized; the debug log shows the dropped notice. Covered by a fixture test per known wording.

## Migration Plan

None (no stored data). After deploy: create a token with `marshal tokens create --name kuma --scope hooks:uptime-kuma`, add a Webhook notification in Uptime Kuma with URL `https://<marshal>/api/v1/hooks/uptime-kuma`, content type JSON and Additional Headers `{"Authorization": "Bearer <token>"}`. Document this in `deploy/config.example/marshal.yaml` next to the `webhooks` section.
