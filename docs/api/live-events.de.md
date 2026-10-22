---
icon: fa-solid fa-tower-broadcast
order: 40
description: "GET /api/v1/events ist ein Strom von Server-Sent Events mit Live-Aktualisierungen für die Weboberfläche."
---

# Live-Ereignisse

`GET /api/v1/events` ist ein Strom von Server-Sent Events mit Live-Aktualisierungen für die Weboberfläche. Er bündelt mehrere Themen über eine Verbindung und liefert nur Ereignisse des Eigentümers des Aufrufers.

## Anfrage

```http
GET /api/v1/events?topics=runs,tasks
Accept: text/event-stream
```

| Parameter | Beschreibung |
|---|---|
| `topics` | Kommagetrennte Namen von Themen. Pflicht. |

Jeder authentifizierte Principal (Cookie-Sitzung oder gültiges Token) darf abonnieren. Ein leeres `topics` gibt `400` `events.topics_required` zurück; ein unbekanntes Thema gibt `400` `events.unknown_topic` mit seinem Namen zurück.

## Format des Stroms

Der Name des SSE-Ereignisses ist der `type` des Ereignisses. Jede `data`-Zeile ist eine Hülle:

```json
{ "topic": "runs", "type": "run.completed", "id": "…", "payload": { } }
```

Das SSE-Feld `id` wiederholt die `id` der Hülle. Der Strom spielt verpasste Ereignisse nicht nach; lade nach einer Neuverbindung den aktuellen Zustand vom REST-Endpunkt neu.

Marshal schickt alle 25 Sekunden ein Ereignis `ping` (`{"topic":"system","type":"ping","id":"","payload":{}}`), damit Proxys die Verbindung offen halten. Ignoriere es.

## Themen und Ereignisse

| Thema | Ereignis | Nutzdaten |
|---|---|---|
| `runs` | `run.started` | `runId`, `source` (`voice`, `web`, `task`, `api`), `trigger`, `startedAt` |
| `runs` | `run.completed` | `runId`, `outcome` (`ok`, `error`, `backgrounded`, `cancelled`), `cost`, `endedAt` |
| `printer` | `printer.state` | `printer` (Name), `state` (neuer Zustand des Druckers, `null`, wenn er offline gegangen ist) |
| `tasks` | `task.changed` | `taskId`, `name`, `status`, `statusReason`, `nextRunAt`, `resultAt` |
| `notifications` | `notification.created`, `notification.updated` | `id`, `source`, `priority`, `text`, `outcome`, `channel`, `reason`, `taskId`, `createdAt` |
| `artifacts` | `artifact.created` | `id`, `name`, `kind`, `source` |
| `artifacts` | `artifact.deleted` | `id` |
| `health` | `health.changed` | `warnings`: die aktuellen [Warnungen zum Zustand](../reference/health-endpoints.md#warnungen-zum-zustand), jeweils mit `component`, `code`, `severity`, `message` (`de`, `en`), `link`, `since` |
| `conversations` | `conversation.result` | `sessionId`, `taskId`, `resultAt`, `body`: Eine Hintergrundaufgabe hat Arbeit beendet, die diese Sitzung abgegeben hat |

Feldnamen sind camelCase. Die genauen Typen der Enum-Werte stehen im [OpenAPI-Dokument](openapi.md); die OpenAPI-Beschreibung deckt nur die Hülle ab, und die Nutzdaten definiert `web/src/live/events.ts` der Weboberfläche.

## Verwandte Seiten

::: cards
- [Ereignisse](../reference/events.md)
- [Beispiele](examples.md)
:::
