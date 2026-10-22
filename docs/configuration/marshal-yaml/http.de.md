---
icon: fa-solid fa-globe
description: "Adresse zum Lauschen, Basispfad hinter dem Reverse Proxy und Umgang mit Forwarded-Headern."
---

# `http`

Adresse zum Lauschen, Basispfad hinter dem Reverse Proxy und Umgang mit Forwarded-Headern.

## Beispiel

```yaml
http:
  pathBase: /marshal
  trustForwardedHeaders: true
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `http.pathBase` | Zeichenkette | nicht gesetzt (leer) | Präfix jeder Route hinter einem Reverse Proxy, zum Beispiel `/marshal`. Es steht auch vor der versionierten API: `/marshal/api/v1/conversation/turns`. Die API-Version selbst ist nie konfigurierbar. |
| `http.trustForwardedHeaders` | Boolean | `true` | `X-Forwarded-Proto`, `-Host` und `-For` vom Reverse Proxy beachten, damit sichere Cookies, OIDC-Redirect-URIs und die Passkey-Relying-Party den öffentlichen https-Ursprung sehen. Allen Proxys wird vertraut: Mach Marshal nur für den Proxy erreichbar (LAN oder VPN). |
| `http.urls` | Zeichenkette | `http://0.0.0.0:8080` | Kestrel-URLs zum Lauschen (durch Semikolon getrennt). |

## Validierung

Kein Validator. Marshal prüft diese Werte nicht; ein falsches `http.urls` scheitert beim Start des Listeners mit einer einzeiligen Zusammenfassung und Stacktrace.

## Verwandte Seiten

::: cards
- [Reverse Proxy](../../installation/reverse-proxy.md)
- [TLS und Domains](../../installation/tls-and-domains.md)
:::
