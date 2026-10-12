# `recording`

Redaction of run recordings: which tool results are stored in full.

## Example

```yaml
recording:
  redactTools: ["mail.*"]
  keepContentTools: []
  maxContentLength: 8000
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `recording.redactTools` | list of tool id globs | `[]` | Results of matching tools are stored as references only, in addition to tools flagged sensitive (which always are). |
| `recording.keepContentTools` | list of tool id globs | `[]` | Results of matching tools are stored in full even when the tool is flagged sensitive. |
| `recording.maxContentLength` | integer | `8000` | Longer contents are truncated in run step payloads. |

Globs use `*` against the tool id (for example `mail.*`). Logs never contain secrets, message bodies, note contents or full prompts regardless of these settings. The section has no validator.

## Related

- [maintenance](maintenance.md) (payload retention)
- [Observability](../../guides/observability.md)
- [Privacy](../../concepts/privacy.md)
