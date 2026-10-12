# `webhooks`

Limits of inbound webhooks (`POST /api/v1/hooks/<source>`).

## Example

```yaml
webhooks:
  debounceSeconds: 60
  requestsPerMinute: 60
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `webhooks.debounceSeconds` | integer (seconds) | `60` | A repeat of an event (same owner, source, type and entity) within this window is dropped. `0` turns it off. |
| `webhooks.requestsPerMinute` | integer | `60` | Requests per token and minute. More get 429. |

Sources are code, not configuration: `uptime-kuma`, `proxmox` and `seerr`. Each needs a machine token with scope `hooks:<source>`. Bodies over 64 KB get 413.

## Validation

- `webhooks.debounceSeconds must be between 0 and 3600.`
- `webhooks.requestsPerMinute must be between 1 and 6000.`

## Related

- [Webhooks API](../../api/webhooks.md)
- [Uptime Kuma](../../guides/integrations/uptime-kuma.md)
- [Machine tokens](../../guides/authentication/machine-tokens.md)
