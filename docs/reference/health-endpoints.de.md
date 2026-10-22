---
icon: fa-solid fa-heart-pulse
description: "Die unversionierten Endpunkte von Marshal und vom Executor-Host, die den Zustand und die unterstützten Versionen melden."
---

# Endpunkte für Zustand und Erkennung

Die unversionierten Endpunkte von Marshal und vom Executor-Host, die den Zustand und die unterstützten Versionen melden.

## Marshal

### `GET /health`

Liveness und Readiness. Keine Authentifizierung und keine Secret-Werte in der Antwort. Nutze den Endpunkt für Healthchecks von Containern und für Uptime-Monitore.

```sh
curl -s http://<marshal>:8080/health
```

| HTTP-Status | Gesamt-`status` | Bedeutung |
|---|---|---|
| `200` | `healthy` | Alle Prüfungen sind gesund. |
| `200` | `degraded` | Mindestens eine Prüfung hat Warnungen. Marshal bedient weiter Anfragen. |
| `503` | `unhealthy` | Mindestens eine Prüfung ist gescheitert. |

Antwort:

```json
{
  "status": "degraded",
  "checks": {
    "database": { "status": "healthy", "description": "reachable", "warnings": null },
    "config": { "status": "healthy", "description": "loaded", "warnings": null },
    "secrets": { "status": "degraded", "description": "optional secrets missing", "warnings": ["secret reference mail_work does not resolve"] },
    "mcp": { "status": "healthy", "description": "1 of 1 servers connected", "warnings": null },
    "memory": { "status": "healthy", "description": "sqlite-vec <version>", "warnings": null },
    "homeassistant": { "status": "healthy", "description": "connected", "warnings": null }
  }
}
```

Jede Prüfung hat einen `status` (`healthy`, `degraded` oder `unhealthy`), eine `description` und `warnings`, eine Liste von Sätzen oder null.

### Prüfungen

| Prüfung | Gesund | Eingeschränkt oder ungesund |
|---|---|---|
| `database` | `reachable`. | `unhealthy`: `unreachable`. |
| `config` | `loaded`. | `degraded` mit `reload failed`. Die Warnungen nennen jede Datei, deren letztes Neuladen gescheitert ist (`risk.yaml`, `satellites.yaml`: `last reload failed, previous version active`), und Fehler in Skills und Templates (`skills/...`, `templates/...`). Die vorherige Konfiguration bleibt aktiv. |
| `secrets` | `resolved`. | `degraded` mit `optional secrets missing`. Die Warnungen nennen die Referenzen: `secret reference <name> does not resolve`. Nur Namen, nie Werte. Ein Secret, das der Start braucht (API-Schlüssel der Modelle, OIDC-Client-Secret, das Home-Assistant-Token), lässt stattdessen den Start scheitern. |
| `mcp` | `<n> of <m> servers connected`. | `degraded` mit derselben Zählung. Die Warnungen nennen Server, die ausgefallen sind (`MCP server <id> is down: <reason>`) oder sich gerade verbinden, `untrustedTools`-Globs ohne passendes Werkzeug und Konfigurationsfehler in `/config/mcp` (`mcp/<file>: <message>`). Die Werkzeuge eines ausgefallenen Servers sind verborgen; andere Werkzeuge funktionieren weiter. |
| `memory` | `sqlite-vec <version>`. | `unhealthy` mit dem Fehler, wenn die Erweiterung `sqlite-vec` nicht lädt. Die Vektorsuche scheitert, bis sie lädt. |
| `homeassistant` | `not configured` oder `connected`. | `degraded` mit `not connected` und dem letzten Fehler, wenn `ha.url` gesetzt ist und der Cache keine Verbindung hat. |

> [!NOTE]
> Die Weboberfläche zeigt dieselben Informationen auf der Seite **System** und schiebt das Zustands-Widget auf **Start** nach oben, wenn eine Prüfung Warnungen hat.

### `GET /api/versions`

Listet die API-Versionen, die dieser Build bedient. Keine Authentifizierung. Die Home-Assistant-Komponente ruft den Endpunkt bei der Einrichtung auf.

```json
{ "supported": ["1.0"], "deprecated": [] }
```

Eine Anfrage an eine unbekannte `/api/...`-Route oder eine nicht unterstützte Version gibt `404` mit dem Problem-Code `api.unsupported_version`, dem Header `api-supported-versions` und der Liste `supportedVersions` zurück.

### `GET /api/v1/system`

Braucht Authentifizierung. Gibt die Version der Anwendung (`version`), `supportedApiVersions`, `deprecatedApiVersions`, `eventSources` und `simulatedIntegrations` zurück. `eventSources` nennt für jede Ereignisquelle ihren Namen `source`, den Verbindungsstatus `connected`, ob sie eine Webhook-Quelle ist (`webhook`), und `lastReceivedAt`. Bei Webhook-Quellen bedeutet `connected`, dass in den letzten 24 Stunden ein Webhook angekommen ist. Siehe [Ereignisse](events.md). `simulatedIntegrations` nennt die Integrationen, die der [Demo-Modus](../guides/demo-mode.md) simuliert (`home_assistant`, `printers`, `mail`, `rss`, `notes`, `web`); ohne Demo-Modus ist die Liste leer.

### `GET /api/v1/system/health`

Braucht einen angemeldeten Benutzer oder ein Token mit dem Scope `admin`. Gibt `warnings` zurück: die aktuellen Warnungen zum Zustand, zuerst Fehler, dann nach Komponente und Code. Ist alles in Ordnung, ist die Liste leer. Änderungen erscheinen auf dem Live-Thema `health` (Ereignis `health.changed`, siehe [Live-Ereignisse](../api/live-events.md)). `/health` bleibt unverändert.

#### Warnungen zum Zustand

Jede Warnung hat eine Komponente `component` (zum Beispiel `database`, `mcp:paperless`), einen stabilen `code`, eine `severity` (`warning` oder `error`), eine `message` auf Deutsch und Englisch, einen optionalen `link` in die Weboberfläche und `since`, den Zeitpunkt ihres ersten Auftretens. Marshal schreibt die Meldungen selbst; sie enthalten nie Secret-Werte oder Text aus externen Systemen. Details zu Fehlern bleiben im Log und in der Konfigurationsansicht.

| Code | Schwere | Bedeutung |
|---|---|---|
| `database.unreachable` | error | Die Datenbank antwortet nicht. |
| `memory.vector_search_unavailable` | error | Die Vektorsuche im Gedächtnis ist nicht verfügbar. |
| `secrets.missing` | warning | Ein Secret, auf das `/config` verweist, lässt sich nicht auflösen. |
| `config.reload_failed` | warning | Das Neuladen einer Datei in `/config` ist gescheitert; die vorherige Version bleibt aktiv. |
| `config.skill_rejected` | warning | Ein Skill wurde abgelehnt. |
| `config.template_rejected` | warning | Ein Template wurde abgelehnt. |
| `homeassistant.disconnected` | warning | Der Websocket zu Home Assistant ist getrennt. |
| `mcp.unreachable` | warning | Ein MCP-Server ist ausgefallen; seine Werkzeuge sind verborgen. |
| `mcp.config_rejected` | warning | Eine Datei eines MCP-Servers wurde abgelehnt. |
| `mcp.untrusted_tools_unmatched` | warning | Ein `untrustedTools`-Glob passt auf kein Werkzeug des Servers. |
| `mcp.tool_pending` | warning | Ein Werkzeug eines MCP-Servers wartet auf Freigabe ([Werkzeug-Pinning](../configuration/mcp-servers.md#werkzeug-pinning)). |
| `tools.unknown_core_namespace` | warning | `tools.core` nennt einen Namensraum, den keine Werkzeugquelle hat. |

Die Weboberfläche zeigt die Warnungen in einem Banner auf jeder Seite, und das Modell kann sie mit `system.health` lesen (siehe [Werkzeuge](tools.md)).

### Andere unversionierte Pfade

`/openapi/{document}.json`, die Callbacks der Authentifizierung und die Routen der Weboberfläche sind ebenfalls unversioniert. Siehe [OpenAPI](../api/openapi.md).

## Executor-Host

Der eigene [Executor-Host](../installation/executor-host.md) beantwortet `GET /health` ohne Authentifizierung.

```json
{ "status": "ok" }
```

## Verwandte Seiten

::: cards
- [Beobachtbarkeit](../guides/observability.md)
- [Metriken und Traces](metrics-and-traces.md)
- [API-Überblick](../api/index.md)
- [Fehlerbehebung](../guides/troubleshooting.md)
:::
