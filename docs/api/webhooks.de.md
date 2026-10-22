---
icon: fa-solid fa-satellite-dish
order: 50
description: "POST /api/v1/hooks/{source} empfängt Webhooks von Infrastrukturwerkzeugen und macht daraus Ereignisse, auf die Ereignis-Auslöser reagieren können."
---

# Webhooks

`POST /api/v1/hooks/{source}` empfängt Webhooks von Infrastrukturwerkzeugen und macht daraus Ereignisse, auf die [Ereignis-Auslöser](../guides/tasks/event-triggers.md) reagieren können.

## Anfrage

| Teil | Wert |
|---|---|
| Pfad | `/api/v1/hooks/{source}` |
| Authentifizierung | `Authorization: Bearer <token>` mit dem Scope `hooks:<source>` |
| Body | Bis zu 64 KB: JSON, `multipart/form-data` oder `application/x-www-form-urlencoded`, je nach Quelle |
| Eigentümer | Das Ereignis gehört dem Eigentümer des Tokens |

Leg ein Token mit nur dem Scope an, den du brauchst. Hook-Scopes gehören zur Rolle `admin`:

```sh
docker compose exec marshal marshal tokens create --name kuma --scope hooks:uptime-kuma
```

## Quellen

| Quelle | Scope | Anleitung |
|---|---|---|
| `uptime-kuma` | `hooks:uptime-kuma` | [Uptime Kuma](../guides/integrations/uptime-kuma.md) |
| `proxmox` | `hooks:proxmox` | [Proxmox](../guides/integrations/proxmox.md) |
| `seerr` | `hooks:seerr` | [Medien](../guides/integrations/media.md) |

### uptime-kuma

Liest den Webhook-Body von Uptime Kuma: JSON `{ heartbeat, monitor, msg }` oder Formulardaten mit einem Feld `data`, das dieses JSON enthält.

| Eingabe | Ereignistyp | Entität |
|---|---|---|
| Heartbeat-Status 0, 1, 2, 3 | `monitor.down`, `monitor.up`, `monitor.pending`, `monitor.maintenance` | Name des Monitors |
| Hinweis „`[<monitor>][<url>] <type> certificate <CN> will expire in <n> days`“ | `certificate.expiring` | Name des Monitors |
| Jeder andere Hinweis (Testmeldung) | keiner | |

Die Nutzdaten enthalten ID, Name, Typ und URL des Monitors, die Zeit des Heartbeats, den Ping in Millisekunden und `important`. Sie enthalten nie die Freitextmeldungen von Uptime Kuma.

### proxmox

Konfiguriere das Webhook-Ziel in Proxmox VE mit dieser Body-Vorlage:

```json
{"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}
```

| `fields.type` | Ereignistyp |
|---|---|
| `vzdump`, Schwere `error` | `backup.failed` |
| `vzdump`, andere Schwere | `backup.ok` |
| `replication` | `replication.failed` |
| `fencing` | `node.fenced` |
| `package-updates` | `updates.available` |
| alles andere oder kein `type` | keiner |

Die Entität ist `fields.job-id`, wenn vorhanden, sonst `fields.hostname`. Die Nutzdaten enthalten `type`, `severity`, `hostname`, `jobId` und die Zeit der Benachrichtigung, sonst nichts aus dem Body.

### seerr

Konfiguriere die JSON-Nutzdaten des Webhooks in Seerr mit `notification_type` und einem Objekt `media` (`media_type`, `tmdbId`, `status`, `jellyfinMediaId`), optional dazu `request.request_id`.

| `notification_type` | Ereignistyp |
|---|---|
| `MEDIA_AVAILABLE` | `media.available` |
| `MEDIA_APPROVED`, `MEDIA_AUTO_APPROVED` | `media.approved` |
| `MEDIA_DECLINED` | `media.declined` |
| `MEDIA_FAILED` | `media.failed` |
| andere (zum Beispiel `MEDIA_PENDING`, `TEST_NOTIFICATION`) | keiner |

Die Entität ist `<media_type>:<tmdbId>`, zum Beispiel `movie:438631`. Freitext wie `subject` und `message` fällt weg.

Ereignisse aus allen drei Quellen sind vertrauenswürdig: Marshal behandelt ihre Nutzdaten nicht als nicht vertrauenswürdige Inhalte.

## Antworten

| Status | Bedeutung |
|---|---|
| `202` | Angenommen. Kommt zurück, egal ob ein Ereignis veröffentlicht wurde oder nicht (entprellt, Testbenachrichtigungen, ignorierte Typen). |
| `400` | Der Body ist für die Quelle nicht lesbar. Nichts wird veröffentlicht. |
| `401` | Token fehlt, ist widerrufen oder abgelaufen. |
| `403` | Dem Token fehlt `hooks:<source>`, oder es hat keinen Eigentümer. |
| `404` | Diese Quelle gibt es nicht. |
| `413` | Body über 64 KB (`webhook.too_large`). |
| `429` | Ratenbegrenzung überschritten (`webhook.rate_limited`). |

Marshal prüft die Quelle vor dem Scope, ein `403` verrät also nicht, welche Quellen es gibt, und ein `404` verrät nicht die Scopes eines Tokens.

## Schutz vor Überflutung

- **Entprellen.** Ein Ereignis mit demselben Eigentümer, derselben Quelle, demselben Typ und derselben Entität wie eines, das in den letzten `webhooks.debounceSeconds` (Standard `60`) veröffentlicht wurde, fällt weg; die Anfrage bekommt trotzdem `202`. Eine Zustandsänderung, zum Beispiel `down` und dann `up`, ist keine Wiederholung.
- **Ratenbegrenzung.** Jedes Token darf `webhooks.requestsPerMinute` Anfragen pro Minute senden (Standard `60`, festes Fenster). Weitere Anfragen bekommen `429`, bis das Fenster endet.

Beide Schlüssel stehen in der [Konfiguration von `webhooks`](../configuration/marshal-yaml/webhooks.md).

## Beispiel

```sh
curl -X POST https://marshal.example.com/api/v1/hooks/uptime-kuma \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"monitor":{"id":1,"name":"NAS","type":"http","url":"https://nas.example"},"heartbeat":{"status":0,"time":"2026-10-10 12:00:00","ping":null,"important":true},"msg":"NAS is down"}'
```

Die Antwort ist `202` mit leerem Body.

## Verwandte Seiten

::: cards
- [Ereignis-Auslöser](../guides/tasks/event-triggers.md)
- [Ereignisse](../reference/events.md)
:::
