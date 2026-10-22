---
icon: fa-solid fa-flask
description: "Demo mode: simulated integrations and sample history for an instance that has only a model configured."
---

# `demo`

Demo mode: simulated integrations and sample history for an instance that has only a model configured.

## Example

```yaml
demo:
  enabled: true
  showBadge: true
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `demo.enabled` | boolean | `false` | Simulates each of Home Assistant, printers, mail, FreshRSS and notes that is not configured, and seeds sample history for the instance owner once. |
| `demo.showBadge` | boolean | `true` | Shows the "Demo" badge in the navigation. Set it to `false` for screenshots. The system view marks simulated integrations either way. |

An integration counts as configured when its real client would use it:

| Integration | Configured when |
|---|---|
| Home Assistant | `ha.url` is set |
| Printers | `printers` has at least one entry |
| Mail | the owner has a mail account (the demo account `demo` shows until then) |
| FreshRSS | `freshrss.url`, `freshrss.userName` and the API password resolve |
| Notes | `notes.remote` is set or the notes directory exists |

Demo mode does not replace the model: startup still fails without `models`. Simulated integrations never contact a network host, and the simulated mail has no send path.

## Validation

The section has no validator.

## Related

::: cards
- [Try Marshal in demo mode](../../guides/demo-mode.md)
- [`marshal demo`](../../reference/cli.md#marshal-demo)
- [`integrations`](integrations.md)
:::
