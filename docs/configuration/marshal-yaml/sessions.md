# `sessions`

When an idle conversation session closes or rests.

## Example

```yaml
sessions:
  idleTtl: "00:05:00"
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `sessions.idleTtl` | duration | `00:05:00` | A voice or API session without a turn for this long is closed. A later turn with the same client id starts a new session with a new owner resolution. A web chat session rests instead: its pending action is dropped and its new turns are summarized into memory, but you can continue it at any time. Range `00:00:30` to `1.00:00:00`. |

## Validation

- `sessions.idleTtl must be between 00:00:30 and 1.00:00:00.`

## Related

- [Data ownership](../../concepts/data-ownership.md)
- [Voice follow-ups](../../guides/voice/follow-ups.md)
