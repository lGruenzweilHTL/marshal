# Spec Delta

## Purpose

Read-only access to a Proxmox VE cluster: node, guest, storage and task status as model tools, through a narrowly scoped API token.

## ADDED Requirements

### Requirement: Proxmox configuration
Proxmox SHALL be configured as `proxmox: { url, tokenId, tokenSecret, certificateFingerprint? }`, where `url` is the absolute `https` URL of one cluster node's API (e.g. `https://pve.lan:8006/`), `tokenId` is the API token id (`user@realm!name`), and `tokenSecret` is the name of the secret holding the token value. When `certificateFingerprint` (SHA-256, hex with or without colons) is set, the server certificate SHALL be accepted only if its fingerprint matches, regardless of its chain; otherwise standard certificate validation SHALL apply. Without a `proxmox` section, no Proxmox tools SHALL exist.

#### Scenario: Pinned self-signed certificate
- **WHEN** the node presents a self-signed certificate whose SHA-256 fingerprint equals `certificateFingerprint`
- **THEN** the connection is accepted

#### Scenario: Fingerprint mismatch
- **WHEN** the node presents a certificate with another fingerprint
- **THEN** the call fails with `infra.unavailable` and no request data is sent

#### Scenario: Not configured
- **WHEN** `marshal.yaml` has no `proxmox` section
- **THEN** no `infra_*` tool is in any tool list

### Requirement: Read-only access
The Proxmox client SHALL only send `GET` requests. No tool or code path SHALL start, stop, create, change, migrate or delete guests, open consoles or shells, or change cluster configuration. The documented token setup SHALL use the `PVEAuditor` role.

#### Scenario: No write path
- **WHEN** the model asks to stop a VM
- **THEN** no Proxmox tool can do it and no request other than `GET` is sent

### Requirement: Proxmox tools
The system SHALL provide these safe tools, each failing with `infra.unavailable` when the API cannot be reached or rejects the token:
- `infra.overview()`: per node its name, online state, CPU and memory use and uptime; guest counts by status; storages above 80 % use;
- `infra.guests(node?, status?)`: VMs and containers with id, name, type, node, status, CPU, memory and uptime, filtered by node and status (`running`, `stopped`, `paused`);
- `infra.storage()`: per storage and node its name, type, used, total and percent used;
- `infra.tasks(failedOnly?, since?)`: up to 50 newest cluster tasks with type, guest id, node, user, start and end time and status, optionally only failed ones and only after `since`.
Tool output SHALL contain no guest notes or descriptions and SHALL be trusted.

#### Scenario: Running guests
- **WHEN** `infra_guests` is called with `status: running`
- **THEN** only running VMs and containers are returned with their node and resource use

#### Scenario: Failed backup in tasks
- **WHEN** a `vzdump` task ended with status `job errors` last night and `infra_tasks` is called with `failedOnly: true`
- **THEN** that task is returned with its type, node, times and status

#### Scenario: Token revoked
- **WHEN** the API answers 401
- **THEN** the tool fails with `infra.unavailable` and the message says the token was rejected
