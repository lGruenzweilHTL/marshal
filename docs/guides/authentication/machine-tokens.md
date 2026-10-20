# Create and manage API tokens

Create scoped, revocable tokens for machine clients such as the Home Assistant integration, scripts and webhook senders.

## Before you start

- For the web UI route: sign in with a browser session. Token management is not available to token clients.
- For the CLI route: shell access to the Marshal container.

## Steps

### Create a token in the web UI

1. Open **Settings > Security**.
2. Under **API tokens**, choose to create a token. Enter a name (for example "Home Assistant"), the scopes and an optional expiry date.
3. Copy the token. Marshal shows it once and stores only its hash.

You can grant only scopes you hold yourself. A `user` cannot create an `admin` token.

### Create a token with the CLI

The CLI works without a web login. Tokens belong to the instance owner.

```sh
docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write --scope aitasks:write
docker compose exec marshal marshal tokens create --name ci --scope tasks:read --scope tasks:write --expires-in-days 90
docker compose exec marshal marshal tokens list
docker compose exec marshal marshal tokens revoke <token-id>
```

`create` prints the token once, followed by "Store it now; it is not shown again." The service name in your compose file may differ. See the [CLI reference](../../reference/cli.md).

### Use the token

```http
POST /api/v1/conversation/turns HTTP/1.1
Authorization: Bearer <token>
```

See [API authentication](../../api/authentication.md).

### Revoke a token

In **Settings > Security** choose **Revoke** on the token, or run `marshal tokens revoke <token-id>`. The next request with that token gets 401. Revokes and creations are audited.

## Scopes

| Scope | Allows |
|---|---|
| `conversation:write` | `POST /api/v1/conversation/turns`. Needed by the Home Assistant integration. |
| `aitasks:write` | `POST /api/v1/ai-tasks`. Needed by the Home Assistant integration's AI Task entity. |
| `mcp:read` | Marshal's MCP server at `/mcp`: memory, notes and task read tools. See [MCP clients](../integrations/mcp-clients.md). |
| `mcp:write` | Marshal's MCP server: also `tasks_create` and `ask_marshal`. |
| `tasks:read` | Read tasks. |
| `tasks:write` | Create and change tasks. |
| `admin` | Admin endpoints. |
| `hooks:uptime-kuma`, `hooks:proxmox`, `hooks:seerr` | Post webhooks of that source. Only `admin` users can grant them. See [webhooks](../../api/webhooks.md). |

A token with too few scopes gets 403. Revoked or expired tokens get 401.

## Executor callback tokens

Tokens of kind `executor_callback` belong to coding sessions. They appear read-only in the list, cannot be created through the API or the UI and cannot be revoked there.

## Check that it works

```sh
curl -sS -H "Authorization: Bearer <token>" https://<marshal-host>/api/v1/me
```

> [!NOTE]
> `/api/v1/me` shows the principal's scopes. Token clients cannot reach endpoints reserved for browser sessions, such as token and secret management.

## Related

- [Assist pipeline](../voice/assist-pipeline.md)
- [Webhooks](../../api/webhooks.md)
- [Secrets](../secrets.md)
