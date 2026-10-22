---
icon: fa-solid fa-right-to-bracket
description: "The login providers for the web UI. Several providers can be active at once. Changes need a restart."
---

# `auth`

The login providers for the web UI. Several providers can be active at once. Changes need a restart.

## Example

```yaml
auth:
  providers:
    - name: local
      type: local
    - name: authentik
      type: oidc
      displayName: Authentik
      authority: "https://auth.example.com/application/o/marshal/"
      clientId: marshal
      clientCredentialSecret: authentik_client
      scopes: [openid, profile, email]
      claims: { subject: sub, name: name, groups: groups }
      roleMappings:
        marshal-admins: admin
        marshal-users: user
```

## Keys

Each entry of `auth.providers`:

| Key | Type | Default | Description |
|---|---|---|---|
| `auth.providers[].name` | string | none | Stable id used in URLs (`/api/v1/auth/login/{name}`, `/signin-oidc/{name}`). Unique. |
| `auth.providers[].type` | string | none | `oidc` or `local`. At most one `local` provider. |
| `auth.providers[].displayName` | string | `Local account` for `local`, else the name | Button label on the login page. |
| `auth.providers[].authority` | URL | none | `oidc`: issuer URL (discovery at `.well-known/openid-configuration`). |
| `auth.providers[].clientId` | string | none | `oidc`: client id. |
| `auth.providers[].clientCredentialSecret` | secret name | none | `oidc`: name of the secret holding the client secret. |
| `auth.providers[].scopes` | list of strings | `[openid, profile, email]` | `oidc`: requested scopes. |
| `auth.providers[].useUserInfo` | boolean | `false` | `oidc`: also read claims from the userinfo endpoint, for IdPs that keep groups out of the ID token. |
| `auth.providers[].claims.subject` | string | `sub` | Claim that carries the subject. |
| `auth.providers[].claims.name` | string | `name` | Claim that carries the display name. |
| `auth.providers[].claims.groups` | string | `groups` | Claim that carries the groups. |
| `auth.providers[].roleMappings` | map group to role | empty | `oidc`: IdP group to `admin` or `user`. A login without a mapped group is denied. Roles are re-synced on every login. |
| `auth.providers[].serverDomain` | string | unset | `local`: WebAuthn relying party id (for example `marshal.example.com`). Unset: the request host. Set it when the proxy does not validate the host header. |

An empty provider list is valid but leaves no way to log in.

## Validation

- `auth.providers[i].name must be lowercase letters, digits or '-'.`
- `auth.providers[i].type must be 'oidc' or 'local'.`
- `auth.providers uses the name '<name>' more than once.`
- `auth.providers allows only one 'local' provider.`
- For `oidc`: `...authority must be an absolute URL.`, `...clientId is required.`, `...clientCredentialSecret is required.`, `...roleMappings needs at least one group.`, `...roleMappings values must be 'admin' or 'user'.`
- The client secret must resolve at startup; otherwise startup fails naming `auth.providers[i].clientCredentialSecret`.

## Related

::: cards
- [Local accounts](../../guides/authentication/local-accounts.md)
- [OIDC](../../guides/authentication/oidc.md)
- [Machine tokens](../../guides/authentication/machine-tokens.md)
:::
