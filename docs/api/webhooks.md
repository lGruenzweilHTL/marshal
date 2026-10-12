# Webhooks

`POST /api/v1/hooks/{source}` receives webhooks from infrastructure tools and turns them into events that [event triggers](../guides/tasks/event-triggers.md) can react to.

## Request

| Part | Value |
|---|---|
| Path | `/api/v1/hooks/{source}` |
| Authentication | `Authorization: Bearer <token>` with the scope `hooks:<source>` |
| Body | Up to 64 KB: JSON, `multipart/form-data` or `application/x-www-form-urlencoded`, depending on the source |
| Owner | The event belongs to the token's owner |

Create a token with only the scope you need. Hook scopes belong to the `admin` role:

```sh
docker compose exec marshal marshal tokens create --name kuma --scope hooks:uptime-kuma
```

## Sources

| Source | Scope | Setup guide |
|---|---|---|
| `uptime-kuma` | `hooks:uptime-kuma` | [Uptime Kuma](../guides/integrations/uptime-kuma.md) |
| `proxmox` | `hooks:proxmox` | [Proxmox](../guides/integrations/proxmox.md) |
| `seerr` | `hooks:seerr` | [Media](../guides/integrations/media.md) |

### uptime-kuma

Reads Uptime Kuma's webhook body: JSON `{ heartbeat, monitor, msg }`, or form data with a `data` field holding that JSON.

| Input | Event type | Entity |
|---|---|---|
| Heartbeat status 0, 1, 2, 3 | `monitor.down`, `monitor.up`, `monitor.pending`, `monitor.maintenance` | Monitor name |
| Notice "`[<monitor>][<url>] <type> certificate <CN> will expire in <n> days`" | `certificate.expiring` | Monitor name |
| Any other notice (test message) | none | |

The payload holds monitor id, name, type and URL, heartbeat time, ping in milliseconds and `important`. It never contains Uptime Kuma's free-text messages.

### proxmox

Configure the Proxmox VE webhook target with this body template:

```json
{"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}
```

| `fields.type` | Event type |
|---|---|
| `vzdump`, severity `error` | `backup.failed` |
| `vzdump`, other severity | `backup.ok` |
| `replication` | `replication.failed` |
| `fencing` | `node.fenced` |
| `package-updates` | `updates.available` |
| anything else, or no `type` | none |

The entity is `fields.job-id` when present, otherwise `fields.hostname`. The payload holds `type`, `severity`, `hostname`, `jobId` and the notification time and nothing else from the body.

### seerr

Configure Seerr's webhook JSON payload with `notification_type` and a `media` object (`media_type`, `tmdbId`, `status`, `jellyfinMediaId`), plus optionally `request.request_id`.

| `notification_type` | Event type |
|---|---|
| `MEDIA_AVAILABLE` | `media.available` |
| `MEDIA_APPROVED`, `MEDIA_AUTO_APPROVED` | `media.approved` |
| `MEDIA_DECLINED` | `media.declined` |
| `MEDIA_FAILED` | `media.failed` |
| other (for example `MEDIA_PENDING`, `TEST_NOTIFICATION`) | none |

The entity is `<media_type>:<tmdbId>`, for example `movie:438631`. Free text such as `subject` and `message` is dropped.

Events from all three sources are trusted: Marshal does not treat their payloads as untrusted content.

## Responses

| Status | Meaning |
|---|---|
| `202` | Accepted. Returned whether or not an event was published (debounced, test notifications, ignored types). |
| `400` | The body is unreadable for the source. Nothing is published. |
| `401` | Missing, revoked or expired token. |
| `403` | The token lacks `hooks:<source>`, or has no owner. |
| `404` | No such source. |
| `413` | Body over 64 KB (`webhook.too_large`). |
| `429` | Rate limit exceeded (`webhook.rate_limited`). |

Marshal checks the source before the scope, so a `403` does not reveal which sources exist and a `404` does not reveal a token's scopes.

## Flood control

- **Debounce.** An event with the same owner, source, type and entity as one published within the last `webhooks.debounceSeconds` (default `60`) is dropped, and the request still gets `202`. A state change, for example `down` then `up`, is not a repeat.
- **Rate limit.** Each token may send `webhooks.requestsPerMinute` requests per minute (default `60`, fixed window). Further requests get `429` until the window ends.

Both keys are documented in [webhooks configuration](../configuration/marshal-yaml/webhooks.md).

## Example

```sh
curl -X POST https://marshal.example.com/api/v1/hooks/uptime-kuma \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"monitor":{"id":1,"name":"NAS","type":"http","url":"https://nas.example"},"heartbeat":{"status":0,"time":"2026-10-10 12:00:00","ping":null,"important":true},"msg":"NAS is down"}'
```

The response is `202` with an empty body.

## Related

- [Event triggers](../guides/tasks/event-triggers.md)
- [Events reference](../reference/events.md)
