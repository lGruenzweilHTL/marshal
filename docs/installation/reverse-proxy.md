# Reverse proxy

Put a TLS reverse proxy in front of Marshal, with or without a path prefix, for Traefik, Caddy or nginx.

## Before you start

- Marshal reachable from the proxy on port 8080.
- A hostname and certificate ([TLS and domains](tls-and-domains.md)).

## What Marshal expects

- **HTTPS at the browser.** The session and antiforgery cookies are `Secure`. Without HTTPS, browser requests fail with `auth.https_required`.
- **Forwarded headers.** With `http.trustForwardedHeaders: true` (default) Marshal honours `X-Forwarded-For`, `-Proto` and `-Host` from any sender, so cookies, OIDC redirect URIs and the passkey relying party see the public https origin. Because it trusts every sender, never expose port 8080 to untrusted networks. Pass the original `Host`.
- **No response buffering** for the server-sent event streams: `POST /api/v1/conversation/turns` and `GET /api/v1/events`. Buffering delays spoken replies and live updates.
- **No WebSockets.** Marshal exposes no WebSocket endpoint to clients, so no upgrade headers are needed.

## Path prefix

To serve Marshal under `https://example.com/marshal`:

```yaml
http:
  pathBase: /marshal
```

Marshal prefixes every route, so the conversation endpoint becomes `/marshal/api/v1/conversation/turns`. Do not strip the prefix in the proxy: Marshal needs it to write the correct `<base href>` for the web app. The health check moves to `/marshal/health`. Use the prefix in Home Assistant's Marshal URL. Without a prefix leave `pathBase` empty.

## Caddy

```text
marshal.example.com {
	reverse_proxy marshal:8080 {
		flush_interval -1
	}
}
```

With a prefix, use `handle /marshal/* { reverse_proxy marshal:8080 { flush_interval -1 } }`, and do not use `handle_path`, which strips the prefix.

## nginx

```text
server {
    listen 443 ssl;
    server_name marshal.example.com;
    ssl_certificate     /etc/ssl/marshal.crt;
    ssl_certificate_key /etc/ssl/marshal.key;

    location / {
        proxy_pass http://marshal:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Connection "";
        proxy_buffering off;
        proxy_read_timeout 1h;
    }
}
```

For a prefix, change `location /` to `location /marshal/` and keep `proxy_pass http://marshal:8080;` without a trailing path, so nginx forwards the prefix. Artifact uploads up to `artifacts.maxUploadMb` (default 200) need a matching `client_max_body_size 200m;`.

## Traefik

Labels on the Marshal service (Docker provider):

```yaml
services:
  marshal:
    labels:
      - traefik.enable=true
      - traefik.http.routers.marshal.rule=Host(`marshal.example.com`)
      - traefik.http.routers.marshal.entrypoints=websecure
      - traefik.http.routers.marshal.tls.certresolver=letsencrypt
      - traefik.http.services.marshal.loadbalancer.server.port=8080
      - traefik.http.services.marshal.loadbalancer.responseforwarding.flushinterval=100ms
```

Traefik sets the forwarded headers itself. Do not add a strip-prefix middleware when you use `pathBase`.

## Check that it works

```sh
curl -i https://marshal.example.com/health
curl -N -H "Authorization: Bearer <token>" "https://marshal.example.com/api/v1/events?topics=<topic>"
```

The first returns 200 with a JSON body. The second streams events as they happen, not in one block.

## Related
- [`http` configuration](../configuration/marshal-yaml/http.md)
- [Live events API](../api/live-events.md)
