# Manage secrets

Give Marshal API keys, tokens and passwords without ever writing them into `/config`.

## Before you start

- You know the secret names your configuration references. A reference is a key ending in `Secret`, such as `apiKeySecret: moonraker_voron` or `tokenSecret: ha_token`.
- A secret name matches `^[a-z][a-z0-9_]{0,63}$`: lowercase letters, digits and `_`, starting with a letter.

`/config` holds secret names only. Marshal refuses to start with a literal secret in it.

## Where values come from

| Source | How | Managed by |
|---|---|---|
| Docker or file secret | File `/run/secrets/<name>`; trailing newline is removed | Compose, read-only |
| Environment variable | `MARSHAL_SECRET_<NAME>` (name in upper case) | Compose, read-only |
| Database | Encrypted with ASP.NET Core Data Protection, keys in `/data/keys` | Web UI |

## Steps

### Option A: file secret

```yaml
# docker-compose.yml (excerpt)
services:
  marshal:
    secrets: [moonraker_voron]
secrets:
  moonraker_voron:
    file: ./secrets/moonraker_voron
```

### Option B: environment variable

```yaml
services:
  marshal:
    environment:
      MARSHAL_SECRET_MOONRAKER_VORON: "${MOONRAKER_VORON_KEY}"
```

Pass the value from an `.env` file that is not in git.

### Option C: the web UI

1. Open **Settings > Security**, tab **Secrets**.
2. Choose **Set value**. Enter the name, the value and the scope.
3. Choose **Store**.

Scopes: **Only me** (a user secret) or **Instance** (all users and configuration; admins only). Values are write-only: Marshal uses them and never shows them again. To change one, set it again. Deleting a secret stops the integrations that use it until it has a value again.

> [!IMPORTANT]
> Back up `/data/keys` together with the database. Without the Data Protection keys, encrypted database secrets cannot be read. See [backups and restore](backups-and-restore.md).

## Lookup order

- A name referenced from `/config` resolves environment or file first, then an instance database secret. It never reads a user's secrets.
- A name referenced from a user-owned row (for example a mail account) resolves environment or file, then that user's secret, then the instance secret.

So a user's own secret overrides an instance secret of the same name, but only for that user's data.

## Check that it works

The Secrets tab lists every secret with name, scope (only me or instance), origin (environment or encrypted) and last update. Environment and file secrets are read-only with no value. It also flags every referenced name that no source provides and says where it is referenced, for example `freshrss_api` wanted by `marshal.yaml`.

[`/health`](../reference/health-endpoints.md) reports an unresolved optional secret as a warning that names the reference, never the value.

## What fails at startup

Secrets that startup needs fail it when unresolved, naming the reference: model API keys, the OIDC client secret and the Home Assistant token. Other unresolved references only produce a warning.

## Audit

Every create, replace and delete is audited without the value.

## Related

- [`secrets-reference`](../configuration/secrets-reference.md)
- [Environment variables](../configuration/environment-variables.md)
- [Security model](../concepts/security-model.md)
