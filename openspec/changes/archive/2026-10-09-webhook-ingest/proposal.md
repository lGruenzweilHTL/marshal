# Proposal

## Why

Marshal's event bus only hears Home Assistant, printers, mail and executors. Infrastructure tools such as Uptime Kuma (and Proxmox next) can push events by webhook, but Marshal has no inbound endpoint for them, so "tell me when the NAS goes down" or a briefing line about failed monitors is impossible. A generic, token-scoped webhook ingest lets each new source be a small normalizer instead of a new integration with its own polling.

## What Changes

- New endpoint `POST /api/v1/hooks/{source}` that accepts a webhook, authenticates it with a machine token carrying the scope `hooks:<source>`, normalizes the body through that source's normalizer and publishes the result on the event bus. The event's owner is the token's owner.
- Sources are defined in code, one normalizer per source. Unknown sources get 404. The first source is `uptime-kuma`: monitor `down`, `up`, `pending` and `maintenance` heartbeats, certificate-expiry notices (days parsed from the message), and the "test" notification (accepted, not published).
- Each normalizer declares whether its events are trusted. A trusted event's payload is not wrapped as untrusted data and does not taint the run it triggers. Every other event (all existing sources included) stays untrusted, as today. Uptime Kuma is trusted; its normalizer drops every free-text message field (`msg`, `heartbeat.msg`), because those carry remote error text or page content.
- Flood control: an event with the same source, type and entity as one published within the debounce window (default 60 s, `webhooks.debounceSeconds`) is dropped; each token is rate-limited (default 60 requests per minute); bodies over 64 KB are rejected.
- New token scopes `hooks:uptime-kuma` (one per source), granted to the `admin` role, creatable with the existing CLI and token API.
- The event-sources list in the web UI shows each webhook source with its last received time.

## Capabilities

### New Capabilities

- `webhook-ingest`: the inbound webhook endpoint, token scopes per source, normalizers, debounce, limits, and the Uptime Kuma source.

### Modified Capabilities

- `event-bus`: "Event sources" gains webhook sources; new requirement on event payload trust (untrusted by default, trusted only when the source declares it).

## Non-goals

- A generic "raw" source that accepts any JSON.
- Proxmox (its own change, reusing this ingest).
- Request signatures (HMAC). Uptime Kuma cannot sign; a Bearer token over TLS on the LAN is the trust boundary.
- Polling Uptime Kuma's API or status pages.
- Configurable normalizers in `/config`; sources are code.
- Notifications on events without a watch or task (e.g. a default "monitor down" alert). Users create watches.

## Impact

- `src/Marshal.Api`: new `Hooks` endpoint group on the versioned route group, rate limiter (ASP.NET Core built-in), request size limit.
- `src/Marshal.Abstractions`: `MarshalEvent` gains a trust flag; new `IWebhookNormalizer` interface; `ApiScopes` gains `hooks:uptime-kuma`.
- `src/Marshal.Tasks`: `EventPayloads` and `AgentTaskRunner.PayloadTaint` honor the trust flag; debounce in the webhook publisher.
- New `UptimeKumaNormalizer` (location in design).
- `deploy/config.example/marshal.yaml`: `webhooks` section (debounce, rate limit) with defaults.
- Tests in `tests/Marshal.Api.Tests` (endpoint, auth, limits) and `tests/Marshal.Tasks.Tests` (trust and taint), web test for the sources list.
- No database changes.
