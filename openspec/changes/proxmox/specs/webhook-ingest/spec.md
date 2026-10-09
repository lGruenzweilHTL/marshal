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
