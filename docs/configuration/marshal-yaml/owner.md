# `owner`

The profile of the instance owner that Marshal creates on first start.

## Example

```yaml
owner:
  displayName: "Your name"
  language: en
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `owner.displayName` | string | `Owner` | Display name of the instance owner. Required, not empty. |
| `owner.language` | string | `en` | Interface and reply language: `de` or `en`. |

The owner's time zone is `instance.timeZone`. Later changes are made per user in the web UI. The first run setup or the first OIDC login claims the owner (see [auth](auth.md)).

## Validation

- `owner.displayName is required.`
- `owner.language is invalid.` (anything but `de` or `en`)

## Related

- [instance](instance.md)
- [Local accounts](../../guides/authentication/local-accounts.md)
