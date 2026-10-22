# Sign in with OpenID Connect

Let people sign in through your identity provider (Authentik, Keycloak, Pocket ID or another OIDC provider) and map provider groups to Marshal roles.

## Before you start

- Marshal is reachable over HTTPS at a stable address.
- An OIDC provider that supports the authorization code flow with PKCE.
- A groups claim in the ID token (or in the userinfo response) that lists the user's groups.
- Two groups at the provider, for example `marshal-admins` and `marshal-users`.

Marshal acts as a confidential client and keeps the provider's tokens on the server. The browser only gets an HttpOnly, Secure, `SameSite=Lax` session cookie.

## Steps

::: steps
1. **Register Marshal at the provider**

   Create an OIDC client with:

   - Redirect URI: `https://<marshal-host>/signin-oidc/<provider-name>`. The name is the `name` you choose below. If Marshal runs under a base path (`http.pathBase`), include it.
   - Flow: authorization code with PKCE; client type confidential.
   - Scopes: `openid`, `profile`, `email`, plus whatever the provider needs to put groups in the token.

   Note the client id and client secret.

2. **Store the client secret**

   Store the client secret under a name, for example `authentik_client`, as a file `/run/secrets/authentik_client`, as `MARSHAL_SECRET_AUTHENTIK_CLIENT`, or in the web UI under **Settings > Security > Secrets**. Never put the value into `marshal.yaml`. See [secrets](../secrets.md).

3. **Add the provider**

   ```yaml
   auth:
     providers:
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

   Restart Marshal. Startup fails if the secret does not resolve.

   | Key | Default | Meaning |
   |---|---|---|
   | `name` | required | Lowercase letters, digits, `-`. Used in URLs. |
   | `displayName` | the name | Button label on the login page. |
   | `authority` | required | Issuer URL. Marshal reads `.well-known/openid-configuration` from it. |
   | `clientId` | required | Client id. |
   | `clientCredentialSecret` | required | Secret name of the client secret. |
   | `scopes` | `[openid, profile, email]` | Requested scopes. |
   | `useUserInfo` | `false` | Also read claims from the userinfo endpoint, for providers that keep groups out of the ID token. |
   | `claims.subject` / `name` / `groups` | `sub` / `name` / `groups` | Claim names. |
   | `roleMappings` | required, at least one | Provider group to `admin` or `user`. |

   Full list: [`auth` reference](../../configuration/marshal-yaml/auth.md).
:::

## Provider examples

Check each provider's current documentation; menus change between versions.

### Authentik

1. Create a provider of type OAuth2/OpenID with the redirect URI `https://<marshal-host>/signin-oidc/authentik` and an application with slug `marshal`.
2. Authority: `https://<authentik-host>/application/o/marshal/`.
3. Make sure the `groups` claim is included (the default profile scope mapping provides it in current versions). If groups are missing, set `useUserInfo: true`.
4. Create the groups `marshal-admins` and `marshal-users` and bind them to the application.

### Keycloak

1. Create a client in your realm: client authentication on, standard flow on, valid redirect URI `https://<marshal-host>/signin-oidc/keycloak`.
2. Authority: `https://<keycloak-host>/realms/<realm>`.
3. Add a **Group Membership** mapper to the client scope with token claim name `groups` and **Full group path** off, added to the ID token. Without the mapper the token has no groups.
4. Create groups `marshal-admins` and `marshal-users` and add users.

```yaml
auth:
  providers:
    - name: keycloak
      type: oidc
      displayName: Keycloak
      authority: "https://kc.example.com/realms/home"
      clientId: marshal
      clientCredentialSecret: keycloak_client
      roleMappings:
        marshal-admins: admin
        marshal-users: user
```

### Pocket ID

1. Create an OIDC client with the callback URL `https://<marshal-host>/signin-oidc/pocketid` and note id and secret.
2. Authority: your Pocket ID base URL, for example `https://id.example.com`.
3. Create user groups and allow them for the client. Request the `groups` scope so the groups claim is issued; if it is not in the ID token set `useUserInfo: true`.

```yaml
auth:
  providers:
    - name: pocketid
      type: oidc
      displayName: Pocket ID
      authority: "https://id.example.com"
      clientId: "<client id>"
      clientCredentialSecret: pocketid_client
      scopes: [openid, profile, email, groups]
      useUserInfo: true
      roleMappings:
        marshal-admins: admin
        marshal-users: user
```

## Roles and users

- Roles come from group claims through `roleMappings`, re-synced on every login. Removing a user from `marshal-admins` removes `admin` at the next login.
- A login without any mapped group is denied. No user is created or linked.
- `admin` gets all scopes; `user` gets all scopes except `admin` and the webhook scopes. Admin-only views (such as Configuration) are hidden from `user`.
- Marshal matches users by the subject claim, never by email. A changed email maps to the same user.
- While the instance owner is unclaimed, the first OIDC subject with a mapped group claims it and keeps its id, sessions and tokens. Later subjects create new users.
- You can enable several providers at once, for example `local` plus one OIDC provider.

## Check that it works

Open the login page. A button with the provider's display name appears (`GET /api/v1/auth/providers` lists it). Sign in. **Settings** shows your name and role in the navigation footer.

## Troubleshooting

- Back on the login page with an error: the redirect URI at the provider does not match, the client secret is wrong, or the user is in no mapped group.
- Groups missing: check the claim name in `claims.groups`; try `useUserInfo: true`.
- Startup error naming `auth.providers[<n>]...`: a required key is missing or a role is neither `admin` nor `user`.

## Related

- [Local accounts](local-accounts.md) as break-glass access
- [Secrets](../secrets.md)
- [Security model](../../concepts/security-model.md)
