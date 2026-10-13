# API authentication

Marshal accepts two kinds of credentials: scoped API tokens for machine clients and a cookie session for people using the web interface. This page describes both and lists the token scopes.

## Machine tokens

Send the token in the `Authorization` header:

```http
Authorization: Bearer <token>
```

A token has an owner, a name, a set of scopes, an optional expiry and a revocation time. Marshal stores only a hash of the token. The plaintext is shown once, at creation. A revoked, expired or unknown token gets `401`.

A token is authorized by its own scopes only. A token with `tasks:read` that calls an endpoint requiring `conversation:write` gets `403`.

### Scopes

| Scope | Allows |
|---|---|
| `conversation:write` | `POST /api/v1/conversation/turns` |
| `tasks:read` | Reading tasks |
| `tasks:write` | Changing tasks |
| `admin` | Administrative endpoints |
| `hooks:uptime-kuma` | Posting Uptime Kuma [webhooks](webhooks.md) |
| `hooks:proxmox` | Posting Proxmox VE webhooks |
| `hooks:seerr` | Posting Seerr webhooks |

Any valid token can call `GET /api/v1/me` and `GET /api/v1/events`. Most other endpoints are for the web interface and need a cookie session; a token never reaches them regardless of its scopes. See [API overview](index.md#spa-internal-endpoints).

Grant a token only the scopes its client needs. The Home Assistant component needs `conversation:write` and nothing else.

### Create a token

On the Marshal host, in the directory of your `docker-compose.yml`:

```sh
docker compose exec marshal marshal tokens create --name home-assistant --scope conversation:write
```

The command works without a web login, creates the token for the instance owner and prints the plaintext once. Repeat `--scope` for more scopes and add `--expires-in-days <days>` for an expiring token.

```sh
docker compose exec marshal marshal tokens list
docker compose exec marshal marshal tokens revoke <token-id>
```

In the web interface, open **Security** to list, create and revoke your own tokens. You can grant only scopes you hold yourself, so a user with the `user` role cannot create an `admin` token. Every create and revoke is recorded in the audit log with you as the actor.

Tokens of kind `executor_callback` belong to the coding executor. They show up read-only in the list and you cannot create or revoke them.

The full walkthrough is in [machine tokens](../guides/authentication/machine-tokens.md).

> [!IMPORTANT]
> Treat a token like a password. Store it in a secret store or environment variable of the client, never in a repository, and revoke it when the client is retired.

### Check a token

```sh
curl -H "Authorization: Bearer <token>" https://marshal.example.com/api/v1/me
```

```json
{ "id": "…", "displayName": "Lukas", "roles": [], "scopes": ["conversation:write"], "demo": { "enabled": false, "showBadge": true } }
```

`roles` is empty for token principals and `scopes` lists the token's own scopes.

## Cookie sessions (browser)

The web interface logs in through a backend-for-frontend (BFF) flow. For the `oidc` provider, Marshal is the confidential client (authorization code with PKCE) and keeps the identity provider's tokens on the server. The browser receives only a session cookie:

- `marshal_session`, `HttpOnly`, `Secure`, `SameSite=Lax`.

Login starts at `/api/v1/auth/login/{provider}` and ends at `/api/v1/auth/logout`. `GET /api/v1/auth/providers` lists the active providers without authentication. Set up providers in [OIDC](../guides/authentication/oidc.md) or [local accounts](../guides/authentication/local-accounts.md).

Browser sessions require HTTPS. A session request over plain HTTP fails with the problem code `auth.https_required`.

### Antiforgery

Every state-changing request (anything except `GET`, `HEAD`, `OPTIONS`, `TRACE`) that is not bearer-authenticated needs an antiforgery token in the `X-XSRF-TOKEN` header. Fetch one from `GET /api/v1/auth/antiforgery` after every login and logout. A missing or invalid token returns `400` with the code `auth.antiforgery_invalid`.

Bearer requests skip this check, because a browser cannot attach a bearer token to a cross-site request.

### Roles and scopes

A cookie session carries the scopes of its roles. The same scope policies apply as for tokens.

| Role | Scopes |
|---|---|
| `user` | `conversation:write`, `tasks:read`, `tasks:write` |
| `admin` | all scopes, including `admin` and the `hooks:*` scopes |

A user with the `user` role who calls an endpoint that needs `admin` gets `403`.

## Related

- [Machine tokens guide](../guides/authentication/machine-tokens.md)
- [Webhooks](webhooks.md)
- [Security model](../concepts/security-model.md)
