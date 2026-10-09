# Design

## Context

See proposal.md (Why). Facts about Proxmox VE (checked against the PVE API viewer and the notifications docs):

- API tokens authenticate with `Authorization: PVEAPIToken=<user>@<realm>!<tokenname>=<uuid>`. A token can be privilege-separated and get its own ACL; `PVEAuditor` on `/` gives read access to nodes, guests, storage and tasks.
- `GET /api2/json/cluster/resources` returns one list with entries of `type` `node` (`node`, `status`, `cpu`, `mem`, `maxmem`, `uptime`), `qemu` / `lxc` (`vmid`, `name`, `node`, `status`, `cpu`, `maxcpu`, `mem`, `maxmem`, `uptime`, `template`) and `storage` (`storage`, `node`, `plugintype`, `disk`, `maxdisk`, `shared`). One call answers overview, guests and storage.
- `GET /api2/json/cluster/tasks` returns recent tasks (`upid`, `type`, `id`, `node`, `user`, `starttime`, `endtime`, `status`); `status` is `OK` for success, otherwise an error text (e.g. `job errors`).
- Default PVE installs use a self-signed certificate.
- Webhook notification targets (PVE 8.3+) send a Handlebars-templated body. Template variables: `title`, `message`, `severity` (`info`, `notice`, `warning`, `error`, `unknown`), `timestamp` (epoch seconds), `fields.*` (`type`: `vzdump`, `replication`, `package-updates`, `fencing`, `system-mail`; `hostname`; `job-id`), `secrets.*`; helpers `json`, `escape`, `url-encode`. Headers are templated too, so `Authorization: Bearer {{ secrets.token }}` keeps the Marshal token in PVE's protected secret store.
- The webhook ingest (`IWebhookNormalizer`, `WebhookIngest`, `UptimeKumaNormalizer` in `Marshal.Tasks/Webhooks`) handles scopes, size limit, rate limit and debounce; a source is a normalizer plus a scope constant.

## Goals / Non-Goals

**Goals:**
- Status questions answered from at most one or two GET calls.
- Events without free text, so they can be trusted and need no taint.

**Non-Goals:**
- Caching resources; every tool call reads PVE (a homelab cluster answers in milliseconds).
- Per-guest backup history (needs storage content listing per node); failed backups come from tasks and events.

## Decisions

### 1. Project and interfaces

New project `Marshal.Integrations.Proxmox` (references `Marshal.Abstractions` only), registered by `Marshal.Api` when `proxmox` is configured.

| Interface | v1 implementation | Purpose |
|---|---|---|
| `IProxmoxClient` | `ProxmoxRestClient` (named `HttpClient`, resilience handler) | `GetResourcesAsync()`, `GetTasksAsync()`; typed records, `Result<T>` with `proxmox.unavailable` |

The tool source (`ProxmoxToolSource : IToolSource`, built-in, namespace `proxmox`) depends only on `IProxmoxClient`, so tool tests use a hand-written fake client; the REST client is tested against an in-process fake server (`Microsoft.AspNetCore.TestHost`) with recorded-shape JSON.

Alternative: a community Proxmox MCP server. Rejected in the roadmap discussion: it holds an infrastructure token in third-party code, its tool surface drifts, and tests would need the server.

### 2. Tools

| Tool | Tier | Untrusted output | Hidden when |
|---|---|---|---|
| `proxmox.overview()` | safe | no | `proxmox` not configured |
| `proxmox.guests(node?, status?)` | safe | no | same |
| `proxmox.storage()` | safe | no | same |
| `proxmox.tasks(failedOnly?, since?)` | safe | no | same |

Output is trusted: names, ids and statuses are set by the cluster's admin (the owner) or by PVE itself, and notes/descriptions are never returned. Task `status` texts are PVE-generated. Templates (`template: 1`) are excluded from guests. Sizes are returned in GB with one decimal, CPU and memory as percent.

The tools stay visible while PVE is unreachable and fail with `proxmox.unavailable` on call, like other integrations.

### 3. Certificate pinning

`certificateFingerprint` installs a `ServerCertificateCustomValidationCallback` on the named client's primary handler that compares `SHA-256(cert.RawData)` with the configured value (normalized: hex, case-insensitive, colons removed) and ignores chain errors only in that case. Without it, default validation. There is no "skip TLS verification" option.

Alternative: `insecureSkipVerify: true`. Rejected: it accepts any MITM on the LAN with an auditor token.

### 4. Webhook source

`ProxmoxNormalizer : IWebhookNormalizer` (source `proxmox`, trusted) in `Marshal.Tasks/Webhooks`. The body is our own template (see spec), so the normalizer reads only `severity`, `timestamp` and `fields.{type,hostname,job-id}`; unknown keys are ignored and never copied. `ApiScopes.HooksProxmox` joins `ApiScopes.All`; the admin role gets it automatically (hook scopes are admin-only).

Recommended PVE setup (documented in the example config):

```
Datacenter → Notifications → Add → Webhook
  URL:     https://marshal.lan/api/v1/hooks/proxmox     (POST)
  Header:  Authorization = Bearer {{ secrets.token }}
           Content-Type  = application/json
  Body:    {"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}
  Secret:  token = <marshal tokens create --name proxmox --scope hooks:proxmox>
Matcher: severity >= info for type vzdump, replication, fencing, package-updates
```

Debounce key is source + type + entity (existing rule): two failed runs of the same backup job within 60 s collapse, a later run does not.

### 5. Configuration

```yaml
proxmox:
  url: https://pve.lan:8006/
  tokenId: marshal@pve!marshal
  tokenSecret: proxmox_token            # secret name
  certificateFingerprint: "AB:CD:…"     # optional, for the self-signed default certificate
```

Validation: `url` absolute https; `tokenId` matches `^[^@\s]+@[^!\s]+![A-Za-z0-9_.-]+$`; fingerprint is 64 hex digits after normalization; `tokenSecret` is a required secret when the section exists.

## Risks / Trade-offs

- [The user gives the token more than `PVEAuditor`] → Marshal still has no write path; the example config and docs say which role to use.
- [PVE's webhook body template is mistyped] → The normalizer returns 400 for unreadable bodies, and the event sources list shows no received time; PVE's "Test" notification is expected to carry no known `fields.type`, so it gets 202 and publishes nothing, which confirms connectivity; check its actual fields during implementation and add a fixture.
- [`cluster/resources` misses details of a stopped node] → Offline nodes show as `offline` with no metrics, which is the useful answer.
