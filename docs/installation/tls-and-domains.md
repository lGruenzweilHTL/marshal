---
icon: fa-solid fa-lock
order: 50
description: "Choose a hostname and certificate so browsers, passkeys and push links work."
---

# TLS and domains

Choose a hostname and certificate so browsers, passkeys and push links work.

## Why it matters

- Browser sessions need HTTPS ([reverse proxy](reverse-proxy.md)).
- Passkeys bind to a domain. By default Marshal uses the request host. Set `serverDomain` on the `local` provider when the proxy does not validate the host header:

```yaml
auth:
  providers:
    - name: local
      type: local
      serverDomain: marshal.example.com
```

- An OIDC provider needs the redirect URI `https://<host>/signin-oidc/<provider name>`. Include any path prefix.
- Push notification links use `notifications.publicUrl`. Displays fetch media from `displays.mediaBaseUrl`, which a TV must reach; plain HTTP on the LAN is often best for them.
- Changing the domain invalidates existing passkeys.

## Options

| Setup | Certificate |
|---|---|
| Public domain | Let's Encrypt in Caddy or Traefik (HTTP or DNS challenge) |
| Home domain, no public access | DNS challenge with your DNS provider, or a wildcard certificate |
| LAN only | Proxy-local CA (Caddy `tls internal`); import the root certificate into your devices |
| VPN overlay | The overlay's own HTTPS feature |

Home Assistant calls Marshal with an API token, so it can use plain HTTP on the LAN or HTTPS. With a private CA, Home Assistant must trust the root.

## Check that it works

Open `https://<host>/` without a certificate warning, sign in and add a passkey under **Security**.

## Related

::: cards
- [`auth`](../configuration/marshal-yaml/auth.md)
- [Local accounts](../guides/authentication/local-accounts.md)
:::
