---
title: API
icon: fa-solid fa-code
order: 70
description: "Marshal stellt unter /api/v{n} eine versionierte REST-API bereit."
---

# API-Überblick

Marshal stellt unter `/api/v{n}` eine versionierte REST-API bereit. Diese Seite beschreibt Basispfad, Versionierung, Authentifizierung, Fehlerformat und Ratenbegrenzung, die für jeden Endpunkt gelten.

Die öffentlichen, für Integrationen relevanten Endpunkte sind der Unterhaltungsendpunkt, AI-Tasks, der Strom der Live-Ereignisse, eingehende Webhooks und die Erkennung der Versionen. Alle anderen Endpunkte existieren für Marshals eigene Weboberfläche; siehe [interne Endpunkte der SPA](#interne-endpunkte-der-spa).

## Basispfad

Alle versionierten Endpunkte liegen unter `/api/v1`. Bedienst du Marshal unter einem Pfadpräfix (`http.pathBase`), setz das Präfix vor jeden Pfad, zum Beispiel `https://example.com/marshal/api/v1/conversation/turns`.

JSON ist camelCase. Zeitstempel sind ISO-8601-Zeichenketten.

| Pfad | Versioniert | Authentifizierung | Zweck |
|---|---|---|---|
| `POST /api/v1/conversation/turns` | ja | Token oder Sitzung | [Ein Turn einer Unterhaltung, gestreamt](conversation.md) |
| `POST /api/v1/ai-tasks` | ja | Token | [Ein AI-Task aus einer Automatisierung, als JSON beantwortet](ai-tasks.md) |
| `GET /api/v1/events` | ja | Token oder Sitzung | [Strom der Live-Aktualisierungen](live-events.md) |
| `POST /api/v1/hooks/{source}` | ja | Token | [Eingehende Webhooks](webhooks.md) |
| `GET /api/v1/me` | ja | Token oder Sitzung | Aktueller Principal: ID, Anzeigename, Rollen, Scopes, Demo-Modus |
| `POST /mcp` | nein (das MCP-Protokoll versioniert sich selbst) | Token mit `mcp:read` oder `mcp:write` | [Marshals MCP-Server](../guides/integrations/mcp-clients.md), Streamable HTTP, zustandslos |
| `GET /api/versions` | nein | keine | Unterstützte und veraltete API-Versionen |
| `GET /openapi/v1.json` | nein | keine | [OpenAPI-Dokument](openapi.md) |
| `GET /health` | nein | keine | Liveness und Readiness; siehe [Endpunkte für Zustand und Erkennung](../reference/health-endpoints.md) |

Zu den unversionierten Endpunkten gehören außerdem die Callbacks des OIDC-Protokolls (`/signin-oidc`, `/signout-callback-oidc`) und die Routen der Weboberfläche.

## Versionierung

Die API-Version ist Teil der URL (`/api/v1/...`). Marshal legt die Versionen, die es bedient, im Code fest; in der Konfiguration kannst du sie nicht ändern.

- **Veröffentlichte Versionen ändern sich nicht nachträglich.** Eine inkompatible Änderung kommt als neue Version (`/api/v2/...`), die neben der alten läuft.
- **Veraltete Versionen** funktionieren weiter und stehen in der Erkennung der Versionen unter `deprecated`, bevor sie entfernt werden.
- Jede versionierte Antwort trägt einen Header `api-supported-versions`.

Erkenne die Versionen ohne Authentifizierung:

```http
GET /api/versions
```

```json
{ "supported": ["1.0"], "deprecated": [] }
```

Eine Anfrage an eine unbekannte Route oder eine nicht unterstützte Version, etwa `/api/v9/conversation/turns`, gibt `404` mit dem Problem-Code `api.unsupported_version` und der Liste der unterstützten Versionen in der Erweiterung `supportedVersions` zurück.

Die [Home-Assistant-Komponente](../home-assistant-component/how-it-works.md) ruft `/api/versions` bei der Einrichtung auf und verweigert die Verbindung, wenn Version `1.0` fehlt.

## Authentifizierung

Es gibt zwei Arten von Aufrufern. Beide werden über dieselben Scope-Richtlinien autorisiert.

| Aufrufer | Zugangsdaten | Genutzt von |
|---|---|---|
| Maschinen-Client | `Authorization: Bearer <token>` mit einem API-Token mit Scopes | Home-Assistant-Komponente, Skripte, Absender von Webhooks |
| Person im Browser | Cookie `marshal_session` nach der Anmeldung, dazu ein Antiforgery-Header bei Schreibzugriffen | Marshals Weboberfläche |

Eine Anfrage mit einem Header `Authorization: Bearer` gilt immer als Token-Anfrage. Details, Scopes und die Verwaltung der Tokens stehen unter [Authentifizierung](authentication.md).

## Fehler

Erwartete Fehler geben [Problem Details](https://www.rfc-editor.org/rfc/rfc9457) zurück (`application/problem+json`). `title` und die Erweiterung `code` tragen einen stabilen, maschinenlesbaren Fehlercode. Prüfe auf `code`, nicht auf `detail`: `detail` ist Text für Menschen und kann sich ändern.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "conversation.text_required",
  "status": 400,
  "detail": "text is required.",
  "code": "conversation.text_required"
}
```

| Status | Bedeutung |
|---|---|
| `400` | Die Anfrage ist ungültig (fehlendes Feld, unbekanntes Thema, nicht lesbarer Body). |
| `401` | Keine Zugangsdaten, oder das Token ist unbekannt, widerrufen oder abgelaufen. |
| `403` | Den Zugangsdaten fehlt der nötige Scope. |
| `404` | Die Ressource, Quelle oder API-Version existiert nicht oder gehört einem anderen Benutzer. |
| `409` | Die Anfrage widerspricht dem aktuellen Zustand. |
| `413` | Der Body der Anfrage ist zu groß (Webhooks: über 64 KB; AI-Tasks: siehe `aiTasks`). |
| `422` | Die Anfrage ist korrekt aufgebaut, aber die Operation ist gescheitert. |
| `429` | Ratenbegrenzung überschritten (Webhooks) oder die tägliche Kostengrenze erreicht (AI-Tasks). |
| `503` | Eine nötige Abhängigkeit ist nicht verfügbar. |

Ein nicht behandelter Serverfehler gibt `500` mit dem Code `server.unexpected`, einem allgemeinen `detail` und der Erweiterung `traceId` zurück. Nimm die Trace-ID, um den passenden Eintrag im Server-Log zu finden; die Antwort enthält nie den Text einer Exception.

## Ratenbegrenzung

Nur der Webhook-Endpunkt hat eine Ratenbegrenzung: Jedes Token darf `webhooks.requestsPerMinute` Anfragen pro Minute senden (Standard `60`, festes Fenster von einer Minute). Überzählige Anfragen bekommen `429` mit dem Code `webhook.rate_limited`. Siehe [Webhooks](webhooks.md#schutz-vor-überflutung).

Der Unterhaltungsendpunkt hat keine Begrenzung der Anfragerate. Stattdessen hat jeder Turn eine Latenzgrenze: Dauert ein Turn zu lange, antwortet Marshal, dass es daran arbeitet, und erledigt die Arbeit als Hintergrundaufgabe. Siehe [Unterhaltung](conversation.md#latenzgrenze).

## Interne Endpunkte der SPA

Die Weboberfläche nutzt die folgenden Gruppen. Sie brauchen eine angemeldete Person (Cookie-Sitzung), sind nicht für Maschinen-Clients gedacht und haben über die Regeln der Versionierung oben hinaus kein Stabilitätsversprechen. Ihre Anfragen und Antworten stehen im [OpenAPI-Dokument](openapi.md).

| Gruppe | Pfade unter `/api/v1` | Zweck |
|---|---|---|
| Anmeldung | `/auth/providers`, `/auth/login/{provider}`, `/auth/logout`, `/auth/antiforgery`, `/auth/local/*` | Anmeldung, Abmeldung, Ersteinrichtung, Passkeys |
| Tokens und Secrets | `/me/tokens`, `/me/secrets`, `/secrets/instance` | API-Tokens und in der Oberfläche verwaltete Secrets verwalten |
| Präferenzen | `/me/preferences` | Einstellungen pro Benutzer |
| Durchläufe und Kosten | `/runs`, `/runs/{id}`, `/runs/{id}/rating`, `/cost` | Aufzeichnung der Durchläufe, Bewertungen und Kosten |
| Evals | `/runs/{id}/eval-case`, `/evals/*` | Eval-Fälle, Kostenschätzungen und Eval-Läufe (Admin). Siehe [Evals](../guides/evals.md) |
| Unterhaltungen | `/conversations`, `/conversations/{id}` | Chatverlauf: Web-, Sprach- und API-Unterhaltungen mit ihren Turns und Ergebnissen aus dem Hintergrund |
| Aufgaben | `/tasks`, `/tasks/{id}`, `/tasks/kill-switch` | Aufgabenliste und Not-Aus |
| Freigaben | `/approvals` | Freigabe-Warteschlange |
| Benachrichtigungen | `/notifications` | Verlauf der Benachrichtigungen |
| Gedächtnis | `/memory/*` | Einträge, Projekte, Graph, Export |
| Drucker | `/printers/*` | Status, Dateien, Steuerung, Snapshots, Webcam |
| Mail | `/mail/*` | Einrichtung der Mailkonten und OAuth |
| Artefakte | `/artifacts/*` | Hochladen, Liste, Inhalt, Vorschauen |
| Displays | `/displays/*` | Anzeigeflächen |
| Änderungsvorschläge | `/proposals/*` | Änderungsvorschläge des Agenten prüfen und mergen |
| Vorschläge | `/suggestions`, `/suggestions/{id}/accept`, `/suggestions/{id}/dismiss`, `/suggestions/detectors/*` | Offene Vorschläge, annehmen und verwerfen, Zähler der Detektoren, stummschalten und zurücksetzen. Siehe [Vorschläge](../guides/suggestions.md) |
| Skill-Vorschläge | `/skill-proposals`, `/skill-proposals/{id}`, `/skill-proposals/{id}/download`, `/skill-proposals/{id}/dismiss` | Entworfene Skills: Vorschau, Download von `SKILL.md`, verwerfen. Siehe [Skill-Vorschläge](../guides/skill-proposals.md) |
| Wartung | `/maintenance`, `/maintenance/{id}/done` | Wartungseinträge des Eigentümers der Instanz und einen als erledigt markieren. Siehe [Wartung](../guides/maintenance.md) |
| System und Konfiguration | `/system`, `/config` | Systemstatus und Zusammenfassung der Konfiguration (Admin) |
| Callbacks der Executors | `/executor/callbacks/*` | Vom Executor-Host mit seinem eigenen Callback-Token genutzt |

## Verwandte Seiten

::: cards
- [Authentifizierung](authentication.md)
- [Unterhaltungsendpunkt](conversation.md)
- [AI-Tasks](ai-tasks.md)
- [Live-Ereignisse](live-events.md)
- [Webhooks](webhooks.md)
- [OpenAPI und Client-Generierung](openapi.md)
- [Beispiele](examples.md)
- [API-Versionierung (für Mitwirkende)](../development/api-versioning.md)
:::
