# Proposal

## Why

The homelab runs on Proxmox VE, but Marshal cannot see it: "is the NAS VM running?", "how full is the backup storage?" or a briefing line about last night's failed backup are impossible. Proxmox VE 8.3+ can push notifications to a webhook target, and Marshal now has a generic webhook ingest, so events cost one normalizer instead of polling.

## What Changes

- New read-only Proxmox client over the Proxmox VE REST API, authenticated with an API token (`proxmox.url`, `proxmox.tokenId`, `proxmox.tokenSecret` as secret name), optionally pinned to the server certificate's SHA-256 fingerprint for self-signed setups.
- New tools, all safe: `proxmox.overview` (nodes, guest counts, storage usage), `proxmox.guests(node?, status?)`, `proxmox.storage()`, `proxmox.tasks(failedOnly?, since?)`. No write paths exist: no start, stop, create, delete, console or shell.
- New webhook source `proxmox` on `POST /api/v1/hooks/proxmox` with scope `hooks:proxmox`. It reads a fixed JSON body that the user sets as the webhook target's body template (documented), and publishes trusted events: `backup.failed`, `backup.ok`, `replication.failed`, `node.fenced`, `updates.available`. Free-text title and message are not part of the body, so no free text enters the event.

## Capabilities

### New Capabilities

- `proxmox`: Proxmox configuration, the read-only client and the `proxmox.*` tools.

### Modified Capabilities

- `webhook-ingest`: new `proxmox` source (body format, event mapping, trust).

## Non-goals

- Any write operation (VM start/stop/create), even at confirm tier; a later change with a separate token.
- Several clusters; one `proxmox.url` per instance.
- Polling `/cluster/tasks` for events; events come only through the webhook.
- Proxmox Backup Server.
- Guest notes and descriptions; they are free text and not needed for status questions.

## Impact

- New project `src/Marshal.Integrations.Proxmox` with `tests/Marshal.Integrations.Proxmox.Tests`; referenced by `Marshal.Api`.
- `src/Marshal.Tasks/Webhooks`: `ProxmoxNormalizer` next to the Uptime Kuma one; `ApiScopes` gains `hooks:proxmox`.
- `deploy/config.example/marshal.yaml`: `proxmox` section and the webhook target setup (body template, header with the token).
- CLAUDE.md repo map and `docs/diagrams/packages.puml`.
