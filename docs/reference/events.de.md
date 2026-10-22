---
icon: fa-solid fa-bolt
description: "Der Event-Bus trägt normalisierte Ereignisse von Home Assistant, Druckern, Mail, Executors, MCP-Server-Tasks und Webhook-Quellen."
---

# Ereignisse

Der Event-Bus trägt normalisierte Ereignisse von Home Assistant, Druckern, Mail, Executors, MCP-Server-Tasks und Webhook-Quellen. Diese Seite listet die Hülle eines Ereignisses, jede Quelle und jeden Ereignistyp mit seinen Feldern in den Nutzdaten und die Filtersyntax, die Beobachtungen nutzen.

## Hülle

Jedes Ereignis hat dieselbe Form.

| Feld | Typ | Beschreibung |
|---|---|---|
| `source` | Zeichenkette | Woher das Ereignis kommt, zum Beispiel `ha`, `printer`, `mail`, `executor`, `uptime-kuma`. |
| `type` | Zeichenkette | Was passiert ist, zum Beispiel `state_changed`. |
| `entity` | Zeichenkette oder null | Woran es passiert ist: eine Entitäts-ID, ein Druckername, ein Alias eines Mailkontos, ein Monitorname. |
| `payload` | Objekt | Quellspezifische Felder, unten gelistet. |
| `ownerId` | ID oder null | Der Eigentümer, dem das Ereignis gehört. Quellen für die ganze Instanz (`ha`, `printer`) tragen keinen. |
| `time` | Zeitstempel | Wann Marshal das Ereignis veröffentlicht hat. |

Ein Durchlauf, den eine Beobachtung startet, bekommt das Ereignis als JSON mit den Feldern `source`, `type`, `entity`, `time` und `payload`. Ein vertrauenswürdiges Ereignis fügt auf oberster Ebene `trusted: true` hinzu. Die eigenen Daten des Ereignisses bleiben immer unter `payload` verschachtelt, Inhalte können diesen Schlüssel also nicht setzen.

## Vertrauen

Nutzdaten von Ereignissen sind standardmäßig nicht vertrauenswürdig. Ein durch ein Ereignis gestarteter Durchlauf bekommt die Nutzdaten als nicht vertrauenswürdige Daten eingepackt und ist mit der Quelle `event` belastet. Eine Ereignisquelle ist nur vertrauenswürdig, wenn ihr Code das sagt; du kannst Vertrauen weder in der Konfiguration noch in Inhalten von Ereignissen setzen.

| Quelle | Vertrauenswürdig |
|---|---|
| `ha` | Nein |
| `printer` | Nein |
| `mail` | Nein |
| `calendar` | Ja |
| `executor` | Nein |
| `mcp` | Nein |
| `uptime-kuma` | Ja |
| `proxmox` | Ja |
| `seerr` | Ja |

Vertrauenswürdige Webhook-Quellen tragen nur die eigene Monitorkonfiguration des Eigentümers, IDs und Zahlen, nie Freitext von entfernten Systemen.

## Home Assistant (`ha`)

Marshal bekommt diese Ereignisse über den Websocket von Home Assistant. Sie tragen keinen Eigentümer.

| Typ | Entität | Felder der Nutzdaten |
|---|---|---|
| `state_changed` | Die Entitäts-ID | `from` (vorheriger Zustand oder null), `to` (neuer Zustand oder null), `attributes` (Attribute des neuen Zustands). Wird nur veröffentlicht, wenn sich der Zustandswert ändert, nicht bei reinen Attributänderungen. |
| `mobile_app_notification_action` | keine | Die Ereignisdaten der Aktion der Companion-App, wie Home Assistant sie schickt. Push-Aktionen zum Freigeben und Ablehnen kommen so an und werden nur mit gültiger, nicht abgelaufener Nonce akzeptiert. |

## Drucker (`printer`)

Marshal leitet Druckerereignisse aus Zustandsübergängen ab. Temperaturänderungen erzeugen nie Ereignisse. Der letzte bekannte Zustand übersteht eine Trennung, ein während der Trennung verpasster Übergang wird also nach dem Wiederverbinden veröffentlicht. Die Entität ist der Druckername.

| Typ | Veröffentlicht, wenn |
|---|---|
| `started` | Ein Druck startet. |
| `paused` | Ein Druck pausiert. |
| `resumed` | Ein pausierter Druck weiterläuft. |
| `complete` | Ein Druck fertig wird. |
| `error` | Der Druck mit einem Fehler endet. |
| `cancelled` | Ein Druck abgebrochen wird. |
| `klippy_shutdown` | Klipper in den Shutdown-Zustand geht. |
| `progress` | Der Fortschritt 25, 50 oder 75 Prozent überschreitet. |
| `first_layer_done` | Der Slicer Schichten meldet und der Druck Schicht 2 erreicht. |

Alle Druckerereignisse teilen sich eine Form der Nutzdaten.

| Feld | Beschreibung |
|---|---|
| `printer` | Druckername. |
| `filename` | Gedruckte Datei oder null. |
| `progress` | Fortschritt von 0 bis 1, auf drei Nachkommastellen gerundet. |
| `layer`, `totalLayers` | Aktuelle und gesamte Schicht, oder null ohne Schichtdaten. |
| `message` | Fehler- oder Statusmeldung. Bei `klippy_shutdown` ist es die Zustandsmeldung von Klipper. |
| `milestone` | 25, 50 oder 75 bei `progress`, sonst null. |

## Mail (`mail`)

| Typ | Entität | Felder der Nutzdaten |
|---|---|---|
| `new_mail` | Der Alias des Kontos | `account` (Alias), `id` (Nachrichten-ID für `mail.read_message`), `from` (Adresse), `subject`, `date`. |

Marshal beobachtet den Posteingang mit IMAP IDLE und fragt ab, wo IDLE nicht verfügbar ist. Das Ereignis trägt den Eigentümer des Kontos.

## Kalender (`calendar`)

Wird nur für CalDAV-Kalender veröffentlicht, nach der Abfrage jedes Kalenders alle `pollMinutes`. Die Entität ist die Kalender-ID (`caldav:<account>/<calendar>`). Das Ereignis trägt den Eigentümer der Instanz. Termine, die beim Start von Marshal schon existieren, erzeugen keine.

| Typ | Veröffentlicht, wenn |
|---|---|
| `event_created` | Ein neuer Termin erschienen ist. |
| `event_updated` | Ein Termin sich geändert hat. |
| `event_removed` | Ein Termin gelöscht wurde. |
| `invitation` | Ein neuer Termin die Adresse des Kontos als Teilnehmer mit RSVP `needs-action` listet. Wird zusammen mit `event_created` veröffentlicht. |

| Feld | Beschreibung |
|---|---|
| `calendar` | Kalender-ID. |
| `eventId` | Termin-ID (die iCalendar-`UID`) für `calendar_update_event`. Null, wenn die UID kein schlichter Bezeichner ist (Buchstaben, Ziffern, `@._:+-`, höchstens 200 Zeichen). |
| `start`, `end` | Beginn und Ende in UTC. Null bei `event_removed`. Bei einem wiederkehrenden Termin die des ersten Vorkommens. |

Die Nutzdaten enthalten keine Titel, Beschreibungen oder Teilnehmer, die Quelle ist also vertrauenswürdig.

## Executors (`executor`)

Marshal veröffentlicht ein Ereignis, wenn eine Executor-Sitzung endet. Die Entität ist die Sitzungs-ID. Das Ereignis trägt den Eigentümer der Sitzung.

| Typ | Bedeutung |
|---|---|
| `completed` | Der Executor ist fertig. Ein Änderungsvorschlag entsteht, wenn der Executor einen Branch und einen Diff meldet. |
| `failed` | Der Executor ist gescheitert oder hat einen unbekannten Typ gemeldet. |
| `cancelled` | Die Sitzung wurde abgebrochen. |

Felder der Nutzdaten: `session`, `executor`, `status`, `target`, `branch` und `proposal`, wo vorhanden.

## MCP-Server-Tasks (`mcp`)

Marshal veröffentlicht ein Ereignis, wenn eine Server-Task endet, die ein MCP-Werkzeugaufruf gestartet hat. Die Entität ist `<server>/<task id>`. Das Ereignis trägt den Eigentümer des Aufrufs.

| Typ | Bedeutung |
|---|---|
| `task_finished` | Der Server hat die Task als abgeschlossen, gescheitert oder abgebrochen gemeldet oder kennt sie nicht mehr. |

Felder der Nutzdaten: `server`, `tool`, `task` und `status` (`completed`, `failed` oder `cancelled`). Die Nutzdaten enthalten nie Ergebnis- oder Fehlertext des Servers; den bekommt die wartende Aufgabe.

## Webhook-Quellen

Webhook-Quellen kommen über `POST /api/v1/hooks/<source>`, jeweils mit einem Token mit dem Scope `hooks:<source>`. Siehe [Webhooks](../api/webhooks.md). Jede Quelle erscheint auf der Systemseite mit der Zeit ihres letzten empfangenen Webhooks. Entität und Nutzdaten setzt der Normalisierer der Quelle. Posts, die der Normalisierer nicht abbildet (Testmeldungen, unbekannte Typen), veröffentlichen nichts.

### `uptime-kuma`

| Typ | Entität | Felder der Nutzdaten |
|---|---|---|
| `monitor.down`, `monitor.up`, `monitor.pending`, `monitor.maintenance` | Monitorname | `monitorId`, `name`, `type` (Monitortyp), `url` (wenn der Monitor eine hat), `time` (UTC), `pingMs`, `important`. |
| `certificate.expiring` | Monitorname | `name`, `days` (Tage bis zum Ablauf). |

### `proxmox`

Marshal liest das Objekt `fields` des Benachrichtigungs-Bodys. Die Entität ist das Feld `job-id`, ohne es der Hostname.

| Typ | Gesetzt durch `fields.type` | Felder der Nutzdaten |
|---|---|---|
| `backup.ok` | `vzdump`, Schweregrad außer `error` | `type`, `severity`, `hostname`, `jobId`, `time`. |
| `backup.failed` | `vzdump`, Schweregrad `error` | Ebenso. |
| `replication.failed` | `replication` | Ebenso. |
| `node.fenced` | `fencing` | Ebenso. |
| `updates.available` | `package-updates` | Ebenso. |

### `seerr`

Die Entität ist `<mediaType>:<tmdbId>`, zum Beispiel `movie:438631`.

| Typ | Benachrichtigungstyp in Seerr | Felder der Nutzdaten |
|---|---|---|
| `media.available` | `MEDIA_AVAILABLE` | `mediaType` (`movie` oder `tv`), `tmdbId`, `requestId`, `jellyfinItemId`, `jellyfin` (Link). |
| `media.approved` | `MEDIA_APPROVED`, `MEDIA_AUTO_APPROVED` | Ebenso. |
| `media.declined` | `MEDIA_DECLINED` | Ebenso. |
| `media.failed` | `MEDIA_FAILED` | Ebenso. |

## Filter von Beobachtungen

Eine Beobachtung (`tasks.watch` oder eine Aufgabe mit Ereignis-Auslöser) wählt Ereignisse mit einem deklarativen Filter. Marshal gleicht Filter deterministisch ab, ohne Modellaufruf, bevor ein Aufgabenlauf startet.

| Schlüssel | Typ | Beschreibung |
|---|---|---|
| `source` | Zeichenkette, Pflicht | Ereignisquelle. Ohne Beachtung der Groß-/Kleinschreibung. |
| `type` | Zeichenkette | Glob auf den Ereignistyp. |
| `entity` | Zeichenkette | Glob auf die Entität des Ereignisses. Ein Ereignis ohne Entität trifft nie. |
| jeder andere Schlüssel | Text, Zahl, Boolean, null oder eine Liste davon | Eine Bedingung an die Nutzdaten. Der Schlüssel ist ein Pfad mit Punkten in die Nutzdaten, zum Beispiel `to` oder `attributes.battery`. Eine Liste trifft, wenn ein Wert trifft. |

Regeln für den Abgleich:

- `*` in einem Glob trifft eine beliebige Zeichenfolge. Textvergleiche ignorieren Groß-/Kleinschreibung.
- Eine Zahl trifft numerische Zeichenketten, weil Zustände in Home Assistant Zeichenketten sind.
- `null` trifft ein fehlendes Feld.
- Ein Bedingungswert kann kein Objekt und keine leere Liste sein.

Eine Beobachtung feuert höchstens einmal pro Scharfschaltung. Eine mit `once: true` angelegte Beobachtung feuert beim ersten passenden Ereignis und endet dann.

Beispiel: nach Hause kommen.

```json
{ "source": "ha", "entity": "person.lukas", "to": "home" }
```

Beispiel: ein gescheiterter Druck auf irgendeinem Drucker.

```json
{ "source": "printer", "type": "error" }
```

## Verwandte Seiten

::: cards
- [Anleitung zu Ereignis-Auslösern](../guides/tasks/event-triggers.md)
- [Webhooks-API](../api/webhooks.md)
- [Uptime Kuma](../guides/integrations/uptime-kuma.md)
- [Werkzeuge](tools.md)
  `tasks.watch`
:::
