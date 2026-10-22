---
icon: fa-solid fa-list-check
description: "Die Dateien in deploy/config.example/tasks/ sind Beispielargumente für die Werkzeuge tasks.create und tasks.watch."
---

# Aufgabendefinitionen (`tasks/*.yaml`)

Die Dateien in `deploy/config.example/tasks/` sind Beispielargumente für die Werkzeuge `tasks.create` und `tasks.watch`. Sie zeigen die Form einer Aufgabe; Marshal liest kein Verzeichnis `/config/tasks`.

> [!IMPORTANT]
> Marshal lädt `tasks/*.yaml` nicht aus `/config`. Leg die Aufgabe an, indem du Marshal bittest („leg die Aufgabe für das Morgen-Briefing mit diesen Einstellungen an“) oder indem du die Argumente aus der Weboberfläche an das Werkzeug übergibst. Danach liegt die Aufgabe in der Datenbank und wird wie jede andere Aufgabe mit `tasks.update` bearbeitet.

## Beispiel: geplante Aufgabe (`tasks.create`)

```yaml
name: briefing
template: briefing
goal: >-
  Prepare today's morning briefing. Follow the briefing-format skill.
trigger:
  kind: cron
  expression: "0 6 * * *"
prepare:
  before: 3h
  goal: >-
    Collect today's appointments and the RSS digest for the morning briefing.
deliver:
  - channel: voice
    followUp: true
    when:
      - state($person = home)
      - state(binary_sensor.kitchen_occupancy = on)
    expires: "11:00"
    fallback: push
  - channel: inbox
```

## Argumente von `tasks.create`

Kein anderes Argument wird akzeptiert. Eigentümer, Budget und Ursprungssatellit lassen sich nicht setzen.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `goal` | Zeichenkette | Pflicht | Was erreicht werden soll, mit jedem Detail, das ein späterer Durchlauf braucht. |
| `trigger` | Objekt | `{ kind: now }` | Wann die Aufgabe läuft. Siehe unten. |
| `name` | Zeichenkette | keiner | Eindeutig pro Eigentümer. In Kleinbuchstaben umgewandelt; Kleinbuchstaben, Ziffern, `-` und `_`, höchstens 64 Zeichen, keine GUID. |
| `template` | Zeichenkette | `default` | [Agent-Template](templates-format.md) der Durchläufe. |
| `notes` | Zeichenkette | keiner | Notizen, die an die Durchläufe gehen. |
| `prepare` | Objekt | keiner | Ein Vorbereitungslauf vor jedem geplanten Durchlauf, nur bei `cron`-Auslösern. Siehe [Vorbereitung](#vorbereitung). `tasks.update` akzeptiert es auch; `null` entfernt es. |
| `deliver` | Liste von Zustellungen | `[]` | Wohin das Ergebnis geht. Ohne Zustellung ist das Ergebnis nur in der Weboberfläche sichtbar. |
| `overrideQuietHoursUntil` | Ortszeit | keiner | Erlaubt der Aufgabe, bis dahin während der Ruhezeiten zu benachrichtigen. Macht den Aufruf zu `confirm`. |

## Auslöser

| `kind` | Zusätzliche Schlüssel | Bedeutung |
|---|---|---|
| `now` | keine | Laufen, sobald ein Worker frei ist. |
| `at` | `time` | Einmal zu einer ISO-8601-Zeit laufen. Eine Zeit ohne Offset gilt in der Zeitzone des Eigentümers, zum Beispiel `2026-09-30T07:00`. |
| `cron` | `expression` | cron-Ausdruck mit fünf Feldern, ausgewertet in der Zeitzone des Eigentümers (mit Sommerzeit). Die Aufgabe wartet nach jedem Durchlauf auf das nächste Vorkommen. |
| `after` | `task` | Laufen, wenn die genannte Aufgabe (ID oder Name) mit `done` endet. Die Aufgabe muss existieren. |

Ereignis-Auslöser gibt es nur über `tasks.watch`.

## Vorbereitung

Eine `cron`-Aufgabe kann langsame oder umfangreiche Eingaben vorab sammeln. Um `before` vor jedem geplanten Durchlauf arbeitet ein Vorbereitungslauf am `goal` der Vorbereitung, mit Template, Werkzeugrichtlinie und Budget der Aufgabe. Sein Ergebnis wird als vorbereitete Notizen gespeichert, und der nächste Hauptlauf bekommt sie zusammen mit dem Zeitpunkt ihrer Sammlung in seine Eingabe.

| Schlüssel | Typ | Beschreibung |
|---|---|---|
| `prepare.before` | Dauer | Wie lange vor jedem geplanten Durchlauf: `3h`, `90m`, `PT3H` oder `03:00:00`. Mehr als null, höchstens 12 Stunden. |
| `prepare.goal` | Zeichenkette | Was der Vorbereitungslauf sammelt. |

- Der Hauptlauf startet mit der Belastung des Vorbereitungslaufs, vorbereitete Notizen aus Mail oder RSS halten den Hauptlauf also belastet.
- Notizen, die beim Hauptlauf älter als `before` plus eine Stunde sind, werden verworfen. Notizen werden verworfen, nachdem der Hauptlauf sie genutzt hat.
- Eine gescheiterte oder übersprungene Vorbereitung blockiert den Hauptlauf nicht; er läuft ohne Notizen und kann trotzdem Werkzeuge aufrufen.
- Der Verbrauch der Vorbereitung zählt gegen das Budget der Aufgabe, aber nicht als Durchlauf.

## Beispiel: Beobachtung (`tasks.watch`)

```yaml
name: media-available
filter:
  source: seerr
  type: media.available
goal: >-
  A title requested in Seerr has become available. Tell me in one short sentence which title is ready to watch.
deliver:
  - channel: push
```

### Argumente von `tasks.watch`

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `filter` | Objekt | Pflicht | Ereignisfilter. Muss `source` enthalten. |
| `goal` | Zeichenkette | Pflicht | Was zu tun ist, wenn das Ereignis trifft. |
| `name` | Zeichenkette | keiner | Wie bei `tasks.create`. |
| `once` | Boolean | `false` | Nur beim ersten passenden Ereignis feuern, dann mit `done` enden. Jeder Wert, der kein Boolean ist, ist ein Validierungsfehler. |
| `deliver` | Liste | `[]` | Wie bei `tasks.create`. |
| `overrideQuietHoursUntil` | Ortszeit | keiner | Wie bei `tasks.create`. |

### Ereignisfilter

Der Filter wird deterministisch abgeglichen, ohne Modellaufruf. Eine Beobachtung feuert einmal pro Scharfschaltung.

| Schlüssel | Typ | Beschreibung |
|---|---|---|
| `source` | Zeichenkette | Pflicht. Ereignisquelle, ohne Beachtung der Groß-/Kleinschreibung abgeglichen (`ha`, `printer`, `mail`, `calendar`, `uptime-kuma`, `seerr`, ...). |
| `type` | Glob (Zeichenkette) | Optionaler Ereignistyp, zum Beispiel `state_changed`, `complete`, `monitor.down`. |
| `entity` | Glob (Zeichenkette) | Optionale Entität, zum Beispiel `person.lukas`. |
| jeder andere Schlüssel | Text, Zahl, Boolean, `null` oder eine Liste davon | Bedingung an die Nutzdaten. Ein Schlüssel mit Punkten (`attributes.battery`) geht in die Nutzdaten hinein. Text ist ein Glob ohne Beachtung der Groß-/Kleinschreibung (`*` trifft alles). Zahlen treffen auch numerische Zeichenketten. `null` trifft ein fehlendes Feld. Eine Liste trifft jeden ihrer Werte. |

Nutzdaten von Ereignissen sind standardmäßig nicht vertrauenswürdig: Ein durch ein Ereignis gestarteter Durchlauf bekommt die Nutzdaten als nicht vertrauenswürdige Daten eingepackt und ist mit der Quelle `event` belastet. Nur Quellen, die das im Code deklarieren, sind vertrauenswürdig (Webhook-Quellen wie `uptime-kuma` und `seerr`). Konfiguration und Inhalt der Nutzdaten können das nicht ändern.

## Zustellungen

Jede Zustellung geht unabhängig hinaus.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `channel` | `voice`, `push`, `inbox`, `silent` | Pflicht | Voice spricht das `speech` des Ergebnisses. Push schickt `speech` plus einen Link. Eingang und Weboberfläche zeigen den `body`. |
| `target` | Zeichenkette | wo der Benutzer ist | Nur Voice: Entitäts-ID des Satelliten. |
| `priority` | `normal` oder `low` | `normal` | `critical` ist nie erlaubt. |
| `followUp` | Boolean | `false` | Nur Voice: Der Satellit hört auf eine Antwort. |
| `when` | Liste von Bedingungen | keiner | Alle müssen gelten. Geprüft, wenn das Ergebnis fertig ist, und bei jeder passenden Zustandsänderung. |
| `expires` | `HH:mm` oder lokales Datum mit Uhrzeit | keiner | Frist in der Zeitzone des Eigentümers. `HH:mm` ist die nächste solche Uhrzeit. Braucht `fallback`. |
| `fallback` | `voice`, `push`, `inbox`, `silent` | keiner | Kanal, der genutzt wird, wenn `expires` zuerst abläuft. Braucht `expires`. |

### Bedingungen

`state(<entity_id> <op> <value>)` mit `=`, `!=` (Text, ohne Beachtung der Groß-/Kleinschreibung) oder `>`, `<` (numerisch). `$person` steht für die `person`-Entität des Eigentümers. Bereiche sind keine Entitäten; nutze einen Anwesenheitssensor. Unbekannte oder nicht verfügbare Zustände erfüllen nie eine Bedingung.

### Validierung von Zustellungen

- `A delivery cannot be critical; only system sources send critical notifications.`
- `Low-priority deliveries never use voice; use inbox or push.` (auch für eine `low`-Zustellung, deren `fallback` `voice` ist)
- `followUp needs the voice channel.`
- `Only voice deliveries have a target.`
- `'<text>' is not a condition like state(binary_sensor.kitchen_occupancy = on).`
- `'<entity>' is an area, not an entity; use an occupancy sensor such as binary_sensor.kitchen_occupancy.`
- `'<text>' compares with > or < but '<value>' is not a number.`
- `'<text>' is not a deadline like 11:00 or 2026-09-30T11:00.`
- `A delivery with expires needs a fallback channel.` / `A fallback channel needs expires.`
- `Each delivery needs a channel: voice, push, inbox or silent.`

## Weitere Validierung

- `goal is required.`
- `Only scheduled (cron) tasks can have a preparation run.`, `prepare.before must be more than zero and at most 12 hours.`, `prepare.before must be a duration like 3h, 90m or PT3H.`, `prepare needs a goal.`
- `trigger.kind must be now, at, cron or after.`, `An 'at' trigger needs a time.`, `A 'cron' trigger needs an expression.`, `An 'after' trigger needs a task.`
- `'<x>' is not a five-field cron expression.`, `'<x>' is not a time like 2026-09-30T07:00.`
- `An event filter is a JSON object with a source, optional type and entity, and payload conditions ...`
- `'<name>' is not a valid task name ...` und ein doppelter Name (Namen sind pro Eigentümer eindeutig).
- `Unknown argument '<x>'.`
- Grenze der Verschachtelung `tasks.maxDepth` (3) und unfertige Kinder `tasks.maxActiveChildren` (5). Das kleinste Weckintervall ist `tasks.minWakeupInterval` (5 Minuten) oder `minWakeupInterval` des Templates.

Die Aufgabenwerkzeuge sind `safe`, speichern aber Arbeit: In einem belasteten Durchlauf werden `tasks.create`, `tasks.watch` und `tasks.update` zu `confirm`.

## Verwandte Seiten

::: cards
- [Geplante Aufgaben](../guides/tasks/scheduled-tasks.md)
- [Ereignis-Auslöser](../guides/tasks/event-triggers.md)
- [Tägliches Briefing](../guides/tasks/daily-briefing.md)
- [Referenz der Ereignisse](../reference/events.md)
- [Aufgaben und Zeitplanung](../concepts/tasks-and-scheduling.md)
:::
