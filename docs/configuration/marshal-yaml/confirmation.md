# `confirmation`

Settings for confirming a confirm-tier action inside a conversation. Changes need a restart.

## Example

```yaml
confirmation:
  ttl: "00:01:00"
  judgeTimeout: "00:00:03"
  keywords:
    en: [confirm, confirmed, approved, "yes do it"]
    de: [bestätigt, bestätigen, "ja mach", "ja mach das"]
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `confirmation.ttl` | duration | `00:01:00` | How long a pending action can be confirmed. Range `00:00:05` to `01:00:00`. |
| `confirmation.judgeTimeout` | duration | `00:00:03` | Timeout of the confirmation judge. On expiry the action is dropped. Range `00:00:00.5` to `00:00:30`. |
| `confirmation.keywords.<language>` | list of strings | `en` and `de` lists below | Exact-match confirmation phrases per language code. A configured language replaces its built-in list. |

Built-in keywords: `en`: `confirm`, `confirmed`, `approved`, `yes do it`. `de`: `bestätigt`, `bestätigen`, `ja mach`, `ja mach das`.

Matching normalizes the utterance and the keywords (trim, lowercase, strip punctuation, collapse spaces) and compares the full text. The language is the first two letters of the request language. Other languages have no keywords; only the judge confirms.

## Validation

- `confirmation.ttl must be between 00:00:05 and 01:00:00.`
- `confirmation.judgeTimeout must be between 00:00:00.5 and 00:00:30.`

## Related

- [Risk and confirmation](../../concepts/risk-and-confirmation.md)
- [Risk tiers](../../reference/risk-tiers.md)
- [tasks](tasks.md) (`tasks.approvalTtl` for background approvals)
