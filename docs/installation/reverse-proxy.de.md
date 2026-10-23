---
icon: fa-solid fa-shuffle
order: 40
description: "Stell einen TLS-Reverse-Proxy vor Marshal, mit oder ohne Pfadpräfix, für Traefik, Caddy oder nginx."
---

# Reverse Proxy

Stell einen TLS-Reverse-Proxy vor Marshal, mit oder ohne Pfadpräfix, für Traefik, Caddy oder nginx.

## Bevor du anfängst

- Marshal ist vom Proxy auf Port 8080 erreichbar.
- Ein Hostname und ein Zertifikat ([TLS und Domains](tls-and-domains.md)).

## Was Marshal erwartet

- **HTTPS im Browser.** Die Sitzungs- und Antiforgery-Cookies sind `Secure`. Ohne HTTPS schlagen Browser-Anfragen mit `auth.https_required` fehl.
- **Forwarded-Header.** Mit `http.trustForwardedHeaders: true` (Standard) beachtet Marshal `X-Forwarded-For`, `-Proto` und `-Host` von jedem Absender, damit Cookies, OIDC-Redirect-URIs und die Passkey-Relying-Party den öffentlichen HTTPS-Ursprung sehen. Weil Marshal jedem Absender vertraut, darfst du Port 8080 nie in nicht vertrauenswürdige Netze öffnen. Reich den ursprünglichen `Host` weiter.
- **Keine Pufferung von Antworten** für die Server-Sent-Event-Streams: `POST /api/v1/conversation/turns` und `GET /api/v1/events`. Pufferung verzögert gesprochene Antworten und Live-Updates.
- **Keine WebSockets.** Marshal bietet Clients keinen WebSocket-Endpunkt an, Upgrade-Header sind also nicht nötig.

## Pfadpräfix

So stellst du Marshal unter `https://example.com/marshal` bereit:

```yaml
http:
  pathBase: /marshal
```

Marshal setzt das Präfix vor jede Route, der Konversationsendpunkt wird also zu `/marshal/api/v1/conversation/turns`. Entferne das Präfix nicht im Proxy: Marshal braucht es, um das richtige `<base href>` für die Web-App zu schreiben. Der Health-Check wandert nach `/marshal/health`. Nimm das Präfix in die Marshal-URL in Home Assistant auf. Ohne Präfix lässt du `pathBase` leer.

## Proxy-Konfiguration

::: tabs
== Caddy

```text
marshal.example.com {
	reverse_proxy marshal:8080 {
		flush_interval -1
	}
}
```

Mit Präfix nimmst du `handle /marshal/* { reverse_proxy marshal:8080 { flush_interval -1 } }` und nicht `handle_path`, das das Präfix entfernt.

== nginx

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

Für ein Präfix änderst du `location /` in `location /marshal/` und lässt `proxy_pass http://marshal:8080;` ohne Pfad am Ende, damit nginx das Präfix weiterreicht. Uploads von Artefakten bis `artifacts.maxUploadMb` (Standard 200) brauchen ein passendes `client_max_body_size 200m;`.

== Traefik

Labels am Marshal-Dienst (Docker-Provider):

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

Traefik setzt die Forwarded-Header selbst. Füg keine Strip-Prefix-Middleware hinzu, wenn du `pathBase` nutzt.
:::

## Prüfen, ob es funktioniert

```sh
curl -i https://marshal.example.com/health
curl -N -H "Authorization: Bearer <token>" "https://marshal.example.com/api/v1/events?topics=<topic>"
```

Der erste Aufruf liefert 200 mit einem JSON-Body. Der zweite streamt Ereignisse, sobald sie passieren, nicht in einem Block.

## Verwandte Seiten

::: cards
- [Konfiguration von `http`](../configuration/marshal-yaml/http.md)
- [API für Live-Ereignisse](../api/live-events.md)
:::
