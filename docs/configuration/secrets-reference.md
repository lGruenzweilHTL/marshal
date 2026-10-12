# Secrets reference

Every secret name that a configuration key can reference, the key that references it, and which stores can supply the value. `/config` files hold secret names only; values come from the secret store.

## Secret names

A secret name matches `^[a-z][a-z0-9_]{0,63}$`: lower-case letters, digits and underscores, starting with a letter, at most 64 characters. Choose the names yourself; the tables below show the keys that take a name, with the names the example config uses.

## Stores

| Store | Form | Scope | Notes |
|---|---|---|---|
| Environment | `MARSHAL_SECRET_<NAME>` (name upper-cased) | Instance | Read-only. Takes precedence over the other stores. |
| Secret file | `/run/secrets/<name>` (directory set by `paths.secrets`) | Instance | Read-only. Trailing line breaks are removed. |
| Database, instance | Encrypted row with no owner | Instance | Managed in the web UI by an `admin`, through `/api/v1/secrets/instance`. Encrypted with Data Protection keys in `/data/keys`. |
| Database, user | Encrypted row with an owner | One user | Managed through `/api/v1/me/secrets`. |

Values are write-only: no endpoint returns them.

### Lookup order

- A name referenced from `/config` resolves environment, then file, then instance database rows. It never reads a user's rows.
- A name referenced from a user-owned row (a mail account) resolves environment and file, then that owner's rows, then instance rows.

A missing name fails with `secret.not_found`.

## References from `marshal.yaml`

All of these resolve from environment, file or instance database only.

| Key | Used for | Resolved | If unresolved |
|---|---|---|---|
| `models.<key>.apiKeySecret` | API key of a model | At startup | Startup fails. Required for `anthropic` models; optional for other providers. |
| `embedding.apiKeySecret` | API key of the embedding server | At startup | Startup fails when set. |
| `ha.tokenSecret` | Home Assistant long-lived access token | At startup | Startup fails. Required when `ha.url` is set. |
| `auth.providers[<i>].clientCredentialSecret` | OIDC client secret (`type: oidc`) | At startup | Startup fails. |
| `printers[<i>].apiKeySecret` | Moonraker API key | At startup | `/health` warns, naming the reference; the integration stays unavailable. Omit the key if Moonraker trusts the network. |
| `freshrss.apiPasswordSecret` | FreshRSS API password | At startup | `/health` warns, naming the reference; the integration stays unavailable. |
| `notes.credentialSecret` | HTTPS token or SSH deploy key (private key) for the notes remote | At startup | `/health` warns, naming the reference; the integration stays unavailable. |
| `oauth.google.clientCredentialSecret` | Google OAuth client secret for mail accounts | At startup | `/health` warns, naming the reference; the integration stays unavailable. |
| `oauth.microsoft.clientCredentialSecret` | Microsoft OAuth client secret for mail accounts | At startup | `/health` warns, naming the reference; the integration stays unavailable. |
| `proxmox.tokenSecret` | Proxmox API token secret (the UUID) | On each request | The `infra.*` tools report the integration unavailable; `/health` warns. |
| `media.seerr.apiKeySecret` | Seerr API key | On each request | Tools report the service unavailable. |
| `media.sonarr.apiKeySecret` | Sonarr API key | On each request | Same. |
| `media.radarr.apiKeySecret` | Radarr API key | On each request | Same. |
| `github.tokenSecret` | Fine-grained GitHub personal access token | On each request | Tools report the integration unavailable. Required (as a valid name) when `github.repos` is set. |
| `executors.claudeCode.tokenSecret` | Token for the executor host | On each request | Starting a session fails: `The executor host token '<name>' is not set.` |

Keys that are read on each request pick up a changed value without a restart.

Only the secrets startup needs fail it: model and embedding API keys, the OIDC client secret and the Home Assistant token. If one of them fails, startup prints every unresolved reference, naming the key, for example `ha.tokenSecret (ha_token)`, and exits with code `1`.

## References from `/config/mcp/*.yaml`

| Key | Used for | If unresolved |
|---|---|---|
| `authTokenSecret` | Bearer token for an HTTP server | The server is unavailable: `<file>: authTokenSecret (<name>) does not resolve.` |
| `envSecrets[].secret` | Environment variable of a stdio server | The server is unavailable: `<file>: envSecrets.<variable> (<name>) does not resolve.` |

Resolved from environment, file or instance database.

## References from user-owned rows

| Reference | Used for | Stores |
|---|---|---|
| Mail account credential (set when connecting a mail account in the web UI) | IMAP app password, or OAuth refresh credentials | Environment, file, then the owner's database rows, then instance rows. |

## Not configurable as secrets

- The executor host token file and sandbox environment file are paths ([environment variables](environment-variables.md#executor-host)), not secret names.
- Webhook and API tokens are managed by Marshal itself (machine tokens), not referenced from configuration.

## Literal secret check

Marshal rejects literal secrets in every `/config` file at startup and on every reload (a failed reload keeps the previous configuration). A file is rejected when:

- a key ending in `Secret` has a value that is not a valid secret name;
- the last camelCase word of a key is `password`, `passphrase`, `token`, `credential` or `credentials`, or its last two words are `api key`, `private key` or `access key`;
- any value looks like a credential: a PEM header, a JWT (`eyJ`), `sk-`, `sk-ant-`, `ghp_`, `xox?-`, a hex string of 32 or more characters, a base64 or base64url string of 32 or more characters, or another long high-entropy string.

`maxTokens: 4096` passes. `apiKeySecret: 0a1b2c3d4e5f60718293a4b5c6d7e8f9` fails, because the value scan matches even though the name pattern allows it. The error names the file and key, never the value. Secret properties set directly in `marshal.yaml` (for example `ha.token`) are rejected too.

## Related

- [Secrets guide](../guides/secrets.md)
- [Environment variables](environment-variables.md)
- [Security model](../concepts/security-model.md)
