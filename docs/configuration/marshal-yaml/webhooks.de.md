---
icon: fa-solid fa-satellite-dish
description: "Grenzen eingehender Webhooks (POST /api/v1/hooks/<source>)."
---

# `webhooks`

Grenzen eingehender Webhooks (`POST /api/v1/hooks/<source>`).

## Beispiel

```yaml
webhooks:
  debounceSeconds: 60
  requestsPerMinute: 60
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `webhooks.debounceSeconds` | Ganzzahl (Sekunden) | `60` | Eine Wiederholung eines Ereignisses (gleicher Eigentümer, Quelle, Typ und Entität) innerhalb dieses Fensters wird verworfen. `0` schaltet das aus. |
| `webhooks.requestsPerMinute` | Ganzzahl | `60` | Anfragen pro Token und Minute. Weitere bekommen 429. |

Quellen sind Code, keine Konfiguration: `uptime-kuma`, `proxmox` und `seerr`. Jede braucht ein Machine-Token mit dem Scope `hooks:<source>`. Bodies über 64 KB bekommen 413.

## Validierung

- `webhooks.debounceSeconds must be between 0 and 3600.`
- `webhooks.requestsPerMinute must be between 1 and 6000.`

## Verwandte Seiten

::: cards
- [Webhooks-API](../../api/webhooks.md)
- [Uptime Kuma](../../guides/integrations/uptime-kuma.md)
- [Machine-Tokens](../../guides/authentication/machine-tokens.md)
:::
