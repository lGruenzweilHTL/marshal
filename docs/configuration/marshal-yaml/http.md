# `http`

Listen address, reverse proxy base path and forwarded header handling.

## Example

```yaml
http:
  pathBase: /marshal
  trustForwardedHeaders: true
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `http.pathBase` | string | unset (empty) | Prefix of every route behind a reverse proxy, for example `/marshal`. It prefixes the versioned API too: `/marshal/api/v1/conversation/turns`. The API version itself is never configurable. |
| `http.trustForwardedHeaders` | boolean | `true` | Honor `X-Forwarded-Proto`, `-Host` and `-For` from the reverse proxy, so secure cookies, OIDC redirect URIs and the passkey relying party see the public https origin. All proxies are trusted: expose Marshal only to the proxy (LAN or VPN). |
| `http.urls` | string | `http://0.0.0.0:8080` | Kestrel listen URLs (semicolon separated). |

## Validation

No validator. Marshal does not check these values; a bad `http.urls` fails at listener start with a one-line summary and stack trace.

## Related

- [Reverse proxy](../../installation/reverse-proxy.md)
- [TLS and domains](../../installation/tls-and-domains.md)
