---
icon: fa-solid fa-paperclip
description: "Limits of the artifact store in /data/artifacts: uploads, executor output, kept snapshots and saved attachments."
---

# `artifacts`

Limits of the artifact store in `/data/artifacts`: uploads, executor output, kept snapshots and saved attachments.

## Example

```yaml
artifacts:
  quotaMb: 5120
  maxUploadMb: 200
  signedLinkMinutes: 15
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `artifacts.quotaMb` | integer (MB) | `5120` | Storage per user. A write that would exceed it fails with a quota error and stores nothing. |
| `artifacts.maxUploadMb` | integer (MB) | `200` | Largest single file for uploads, executor output and attachments. A larger web upload gets 413. |
| `artifacts.signedLinkMinutes` | integer (minutes) | `15` | Lifetime of signed content links, for example for a phone notification. |

Rendered 3D pages for displays are cached in `/data/cache/renditions`, at most 512 MB. This cache is disposable and not configurable.

## Validation

- `artifacts.quotaMb must be at least 1.`
- `artifacts.maxUploadMb must be at least 1.`
- `artifacts.signedLinkMinutes must be between 1 and 1440.`

## Related

::: cards
- [Artifacts guide](../../guides/artifacts.md)
- [`displays`](displays.md)
:::
