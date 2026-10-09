# webhook-ingest Specification

## Purpose
Inbound webhooks from infrastructure tools (e.g. Uptime Kuma), authenticated per source with machine tokens and normalized into event-bus events.

## Requirements

### Requirement: Webhook endpoint
The system SHALL accept webhooks at `POST /api/v1/hooks/{source}`. A request SHALL be authenticated with a machine token (`Authorization: Bearer`) that carries the scope `hooks:<source>`; the event's owner SHALL be the token's owner. A request for a source without a normalizer SHALL get 404, a token without the source's scope SHALL get 403, and a missing, revoked or expired token SHALL get 401. Bodies larger than 64 KB SHALL be rejected with 413. A body the normalizer cannot read SHALL get 400 and publish nothing. An accepted request SHALL get 202, whether or not an event was published (debounced, test notifications).

#### Scenario: Accepted webhook
- **WHEN** Uptime Kuma posts a down heartbeat for monitor `NAS` with a token that has `hooks:uptime-kuma`
- **THEN** the response is 202 and an event `{ source: uptime-kuma, type: monitor.down, entity: NAS }` owned by the token's owner is published

#### Scenario: Wrong source scope
- **WHEN** a token with only `hooks:uptime-kuma` posts to `/api/v1/hooks/proxmox`
- **THEN** the response is 404 because no `proxmox` source exists

#### Scenario: Token without hook scope
- **WHEN** a token with only `conversation:write` posts to `/api/v1/hooks/uptime-kuma`
- **THEN** the response is 403 and nothing is published

#### Scenario: Oversized body
- **WHEN** a request body is 100 KB
- **THEN** the response is 413 and nothing is published

### Requirement: Hook scopes
Each webhook source SHALL have its own token scope `hooks:<source>`. These scopes SHALL be granted to the `admin` role, so admins can create hook tokens with the CLI (`marshal tokens create --name kuma --scope hooks:uptime-kuma`) and the token API.

#### Scenario: CLI token for Uptime Kuma
- **WHEN** an operator runs `marshal tokens create --name kuma --scope hooks:uptime-kuma`
- **THEN** a token with exactly that scope is created and can post to `/api/v1/hooks/uptime-kuma` only

### Requirement: Flood control
The ingest SHALL drop an event whose source, type and entity equal those of an event it published for the same owner within the debounce window (default 60 seconds, configurable as `webhooks.debounceSeconds`). Each token SHALL be limited to a configurable number of requests per minute (default 60); requests beyond it SHALL get 429.

#### Scenario: Flapping monitor
- **WHEN** Uptime Kuma posts `down` for `NAS` twice within 30 seconds
- **THEN** one event is published

#### Scenario: State change is not debounced
- **WHEN** Uptime Kuma posts `down` and 10 seconds later `up` for `NAS`
- **THEN** both events are published

#### Scenario: Rate limit
- **WHEN** a token sends its 61st request within one minute
- **THEN** the response is 429

### Requirement: Uptime Kuma source
The `uptime-kuma` source SHALL read Uptime Kuma's webhook notification body (JSON `{ heartbeat, monitor, msg }`, or form data with a `data` field holding that JSON) and publish:
- a heartbeat with status 0, 1, 2 or 3 as `monitor.down`, `monitor.up`, `monitor.pending` or `monitor.maintenance`, entity = monitor name, payload with monitor id, name, type and URL, the heartbeat time, ping in milliseconds and `important`;
- a notification without heartbeat whose message reports certificate expiry (`[<monitor>][<url>] <type> certificate <CN> will expire in <n> days`; Uptime Kuma sends it without monitor object) as `certificate.expiring`, entity = monitor name (from `monitor.name`, else from the message), payload with the monitor name and the days left;
- nothing for any other notification without heartbeat (Uptime Kuma's test message and unknown notices).
The payload SHALL contain no free-text message from Uptime Kuma (`msg`, `heartbeat.msg`). The source's events SHALL be trusted.

#### Scenario: Monitor down
- **WHEN** the body has `heartbeat.status = 0`, `monitor.name = NAS` and `heartbeat.msg = "connect ECONNREFUSED"`
- **THEN** a trusted `monitor.down` event for `NAS` is published and its payload contains no `msg`

#### Scenario: Certificate expiry
- **WHEN** the body has no heartbeat and `msg = "[Nextcloud][https://cloud.example] server certificate cloud.example will expire in 14 days"`
- **THEN** a `certificate.expiring` event for `Nextcloud` with `days: 14` is published

#### Scenario: Test notification
- **WHEN** the body has neither monitor nor heartbeat and its message is not a certificate notice
- **THEN** the response is 202 and no event is published

#### Scenario: Watch on a monitor
- **WHEN** the owner has a watch `source: uptime-kuma, type: monitor.down, entity: NAS` and the NAS monitor goes down
- **THEN** the watch's task becomes due with the event as trigger payload
