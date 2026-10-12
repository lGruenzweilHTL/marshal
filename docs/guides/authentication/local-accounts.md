# Sign in with a local account

Claim the instance owner on first start, then sign in with a passkey or a password.

## Before you start

- Marshal is reachable over HTTPS. Browser sessions and the antiforgery token require HTTPS; over plain HTTP the setup fails with the `auth.https_required` problem.
- `auth.providers` in `marshal.yaml` has a `local` provider:

```yaml
auth:
  providers:
    - name: local
      type: local
```

Only one `local` provider is allowed. Provider names use lowercase letters, digits and `-`. Changes to `auth` need a restart.

## Steps

### 1. Run the first-run setup

Open Marshal in the browser. While the instance owner is unclaimed, the setup page appears.

1. Enter a user name, an optional display name and a password of at least 12 characters.
2. Submit.

The setup sets the owner's credentials, gives the owner the role `admin`, keeps the owner's id and signs you in. After that the setup is unavailable and its endpoint answers 404.

### 2. Add a passkey

1. Open **Settings > Security**.
2. Under **Passkeys**, optionally name the passkey (for example "Phone") and choose **Add a passkey**.
3. Confirm with your fingerprint, face or device PIN.

From then on you can sign in with the passkey instead of the password. The password stays as fallback.

## Check that it works

Sign out and sign in again with the passkey, then with the password.

## Sessions and lockout

- The session cookie is `marshal_session`: HttpOnly, Secure, `SameSite=Lax`. It lasts 14 days with sliding renewal.
- Five failed password attempts lock the account for a time.
- Passkeys are bound to the host name. If your reverse proxy does not validate the host header, set `serverDomain` (for example `marshal.example.com`) on the `local` provider.

## Other users

The `local` provider covers the instance owner. Marshal prepares for several users, but additional people sign in through [OIDC](oidc.md). A user can have both an OIDC login and local credentials, which gives you break-glass access if the identity provider is down.

## Troubleshooting

- "Marshal must be opened over HTTPS": use the HTTPS address ([reverse proxy](../../installation/reverse-proxy.md)).
- Setup page missing: the owner is already claimed (password, passkey or external login).
- Passkey fails on a new domain: passkeys are tied to the host name; add a new one.

## Related

- [`auth` reference](../../configuration/marshal-yaml/auth.md)
- [Machine tokens](machine-tokens.md)
- [Security model](../../concepts/security-model.md)
