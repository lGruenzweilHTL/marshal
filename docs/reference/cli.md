# Command line

Every command and flag of the `marshal` executable. The container ships `/app/marshal` and links it to `marshal` on the `PATH`, so you can run it inside the running container.

```sh
docker compose exec marshal marshal tokens list
```

With no arguments the executable starts the web server. The first argument selects a mode.

| Invocation | Mode |
|---|---|
| `marshal` | Starts the web server. |
| `marshal tokens <command>` | Manages API tokens without a web login. |
| `marshal --export-openapi <file>` | Writes the OpenAPI document and exits. |

Any other arguments go to the ASP.NET Core host as configuration arguments. Marshal does not define further flags.

## `marshal tokens`

Manages API tokens for machine clients. The commands read `/config/marshal.yaml` and the environment like the server does, open the database in `/data`, and create the database schema if it does not exist yet. Tokens belong to the instance owner. Each create and revoke is written to the audit log with actor kind `system` and actor `cli`.

> [!NOTE]
> Run these commands against the same `/config` and `/data` as the server. In the container they do this by default.

### `marshal tokens create`

Creates a token and prints its plaintext once.

```sh
marshal tokens create --name <name> --scope <scope> [--scope <scope>...] [--expires-in-days <days>]
```

| Flag | Required | Description |
|---|---|---|
| `--name <name>` | yes | A label for the token. |
| `--scope <scope>` | yes, repeatable | A scope to grant. At least one is required. |
| `--expires-in-days <days>` | no | Days until the token expires. A positive whole number. Without it the token does not expire. |

Known scopes:

| Scope | Allows |
|---|---|
| `conversation:write` | Send conversation requests (the Home Assistant component). |
| `tasks:read` | Read tasks. |
| `tasks:write` | Create and change tasks. |
| `admin` | Administrative endpoints. |
| `hooks:uptime-kuma` | Post Uptime Kuma webhooks. |
| `hooks:proxmox` | Post Proxmox VE webhooks. |
| `hooks:seerr` | Post Seerr webhooks. |

An unknown scope fails with `token.invalid_scope` and the list of known scopes.

Output on success:

```text
Created token '<name>' (<token-id>) with scopes: <scopes>
<plaintext token>
Store it now; it is not shown again.
```

Marshal stores only a hash of the token. Use the plaintext in an `Authorization: Bearer <token>` header. See [API authentication](../api/authentication.md).

### `marshal tokens list`

```sh
marshal tokens list
```

Prints one line per token: id, name, scopes, creation time and a state, which is `active`, `expires <time>` or `revoked`.

### `marshal tokens revoke`

```sh
marshal tokens revoke <token-id>
```

Revokes a token by its id (from `marshal tokens list`). Prints `Revoked token <token-id>.`

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | The operation failed. The error message goes to standard error. |
| `2` | The arguments were wrong. The usage text goes to standard error. |

## `marshal --export-openapi`

```sh
marshal --export-openapi <file>
```

Writes the v1 OpenAPI document to `<file>` without starting the server, and creates missing parent directories. The `servers` list is left out because the web UI calls the API on its own origin. The web build uses this to generate its API types (`npm run gen:api`). Without a file argument it prints `Usage: marshal --export-openapi <file>` and exits with `2`. See [OpenAPI](../api/openapi.md).

## Environment

The CLI honors the same environment variables as the server, for example `MARSHAL__Paths__Config`, `MARSHAL__Paths__Data` and `MARSHAL__Paths__Secrets`. See [environment variables](../configuration/environment-variables.md).

## Related

- [Machine tokens](../guides/authentication/machine-tokens.md)
- [API authentication](../api/authentication.md)
- [Data directory](data-directory.md)
