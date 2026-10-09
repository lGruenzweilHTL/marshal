## MODIFIED Requirements

### Requirement: Webhook endpoint
The system SHALL accept webhooks at `POST /api/v1/hooks/{source}`. A request SHALL be authenticated with a machine token (`Authorization: Bearer`) that carries the scope `hooks:<source>`; the event's owner SHALL be the token's owner. A request for a source without a normalizer SHALL get 404, a token without the source's scope SHALL get 403, and a missing, revoked or expired token SHALL get 401. Bodies larger than 64 KB SHALL be rejected with 413. A body the normalizer cannot read SHALL get 400 and publish nothing. An accepted request SHALL get 202, whether or not an event was published (debounced, test notifications).

#### Scenario: Accepted webhook
- **WHEN** Uptime Kuma posts a down heartbeat for monitor `NAS` with a token that has `hooks:uptime-kuma`
- **THEN** the response is 202 and an event `{ source: uptime-kuma, type: monitor.down, entity: NAS }` owned by the token's owner is published

#### Scenario: Unknown source
- **WHEN** a token with only `hooks:uptime-kuma` posts to `/api/v1/hooks/gitlab`
- **THEN** the response is 404 because no `gitlab` source exists

#### Scenario: Wrong source scope
- **WHEN** a token with only `hooks:uptime-kuma` posts to `/api/v1/hooks/proxmox`
- **THEN** the response is 403 and nothing is published

#### Scenario: Token without hook scope
- **WHEN** a token with only `conversation:write` posts to `/api/v1/hooks/uptime-kuma`
- **THEN** the response is 403 and nothing is published

#### Scenario: Oversized body
- **WHEN** a request body is 100 KB
- **THEN** the response is 413 and nothing is published

## ADDED Requirements

### Requirement: Proxmox source
The `proxmox` source SHALL read the JSON body `{ "severity": <string>, "timestamp": <number>, "fields": <object> }`, which the user configures as the Proxmox VE webhook target's body template (`{"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}`). From `fields` it SHALL read `type`, `hostname` and `job-id`, and publish:
- `type: vzdump` with severity `error` as `backup.failed`, with any other severity as `backup.ok`;
- `type: replication` as `replication.failed`;
- `type: fencing` as `node.fenced`;
- `type: package-updates` as `updates.available`;
- nothing for other types (e.g. `system-mail`) and for a body without `fields.type`.
The entity SHALL be `job-id` when present, else `hostname`. The payload SHALL hold `type`, `severity`, `hostname`, `jobId` and the notification time, and no other field from the body. The source's events SHALL be trusted. Its scope SHALL be `hooks:proxmox`.

#### Scenario: Failed backup job
- **WHEN** the body has `severity: error` and `fields: { type: vzdump, hostname: pve1, job-id: backup-nightly }`
- **THEN** a trusted `backup.failed` event with entity `backup-nightly` and hostname `pve1` is published

#### Scenario: Forwarded system mail
- **WHEN** the body has `fields.type: system-mail`
- **THEN** the response is 202 and no event is published

#### Scenario: Extra fields are dropped
- **WHEN** the body also has `title` and `message` keys
- **THEN** the published payload contains neither

#### Scenario: Unreadable body
- **WHEN** the body is not JSON or `fields` is not an object
- **THEN** the response is 400 and nothing is published
