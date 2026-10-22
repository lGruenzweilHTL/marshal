---
icon: fa-solid fa-server
description: "Give Marshal read-only access to your Proxmox VE cluster so it can report on nodes, guests, storage and tasks, with a token that cannot change anything."
---

# Watch a Proxmox VE cluster

Give Marshal read-only access to your Proxmox VE cluster so it can report on nodes, guests, storage and tasks, with a token that cannot change anything.

## Before you start

- Proxmox VE with its API reachable over `https` from Marshal's container, usually on port 8006.
- Permission to create users and API tokens in Proxmox.
- For a self-signed node certificate: the certificate's SHA-256 fingerprint.

## Steps

::: steps
1. **Create a read-only API token in Proxmox**

   1. Create a user for Marshal, for example `marshal@pve`, under **Datacenter > Permissions > Users**.
   2. Under **Datacenter > Permissions > API Tokens**, add a token named `marshal` for that user. Keep **Privilege Separation** on. Copy the token value (a UUID) when Proxmox shows it; it appears only once.
   3. Under **Datacenter > Permissions**, add an API token permission: path `/`, token `marshal@pve!marshal`, role `PVEAuditor`, propagate on.

   `PVEAuditor` allows reading only. Marshal also sends `GET` requests only, so a mistake on one side does not open a write path.

2. **Store the token value as a secret**

   Create a secret, for example `proxmox_token`, holding the UUID. See [secrets](../secrets.md). Never put the value into `marshal.yaml`.

3. **Pin the certificate (self-signed nodes)**

   Proxmox nodes use a self-signed certificate by default. Copy its SHA-256 fingerprint from **Node > System > Certificates**, or read it with:

   ```sh
   openssl s_client -connect pve.lan:8006 </dev/null 2>/dev/null | openssl x509 -noout -fingerprint -sha256
   ```

   With a fingerprint set, Marshal accepts the server certificate only if its fingerprint matches, whatever its chain. Without one, Marshal applies standard certificate validation, which fails for a self-signed certificate.

4. **Add the `proxmox` section**

   ```yaml
   proxmox:
     url: https://pve.lan:8006/
     tokenId: marshal@pve!marshal
     tokenSecret: proxmox_token
     certificateFingerprint: "AB:CD:..."   # 64 hex digits, colons optional; omit for a CA-signed certificate
   ```

   `url` is the API URL of one cluster node. Startup fails with these messages when a rule is broken:

   - `proxmox.url must be an absolute https URL.`
   - `proxmox.tokenId must look like user@realm!name.`
   - `proxmox.tokenSecret must name a secret.`
   - `proxmox.certificateFingerprint must be a SHA-256 fingerprint (64 hex digits, colons allowed).`

   Without a `proxmox` section, Marshal registers no `infra_*` tool. Restart Marshal after editing the file.
:::

## Tools

All tools are in the safe tier and fail with `infra.unavailable` when the API cannot be reached or rejects the token.

| Tool | What it does |
|---|---|
| `infra_overview()` | Per node: name, online state, CPU and memory use, uptime. Guest counts by status. Storages above 80 % use. |
| `infra_guests(node?, status?)` | VMs and containers with id, name, type, node, status, CPU, memory and uptime. `status` is `running`, `stopped` or `paused`. |
| `infra_storage()` | Per storage and node: name, type, used, total and percent used. |
| `infra_tasks(failedOnly?, since?)` | Up to 50 newest cluster tasks with type, guest id, node, user, start and end time and status. |

## Limits

- Marshal sends `GET` requests only. It cannot start, stop, create, change, migrate or delete guests, open consoles or shells, or change cluster configuration.
- Output contains no guest notes or descriptions, so the tools are trusted and do not taint a run.
- A rejected token (HTTP 401) fails the call with a message that says the token was rejected. A fingerprint mismatch fails with `infra.unavailable` and no request data is sent.
- Marshal reads the token secret on every request, so you can rotate it without a restart.

## Check that it works

1. Ask "How is the Proxmox cluster doing?" The answer lists your nodes with CPU, memory and uptime.
2. Ask "Which guests are running?" and "Did any backup fail?" The second call uses `infra_tasks` with `failedOnly`.
3. If a call fails, the message tells you whether the API was unreachable, the token was rejected or the certificate did not match. See [troubleshooting](../troubleshooting.md).

To get a message when a backup fails instead of asking, set up Proxmox notifications as described in [Uptime Kuma and Proxmox webhooks](uptime-kuma.md#proxmox-ve-webhooks).

## Related

::: cards
- [`proxmox` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Secrets](../secrets.md)
- [Tool reference](../../reference/tools.md)
- [Security model](../../concepts/security-model.md)
:::
