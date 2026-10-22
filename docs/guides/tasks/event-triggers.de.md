---
icon: fa-solid fa-bolt
description: "Führe eine Aufgabe aus, wenn etwas passiert: eine Person kommt an, ein Druck ist fertig, ein Monitor fällt aus."
---

# Ereignis-Auslöser

Führe eine Aufgabe aus, wenn etwas passiert: eine Person kommt an, ein Druck ist fertig, ein Monitor fällt aus.

## Bevor du anfängst

- Die Ereignisquelle ist eingerichtet: Home Assistant (`ha`), ein Drucker (`printer`), Mail oder eine Webhook-Quelle wie `uptime-kuma`, `proxmox` oder `seerr` (siehe [Uptime Kuma](../integrations/uptime-kuma.md) und [`webhooks`](../../configuration/marshal-yaml/webhooks.md)).
- Referenz der Ereignisse und Felder: [Ereignisse](../../reference/events.md).

## Schritte

::: steps
1. **Leg eine Beobachtung an**

   Frag Marshal, oder übergib die Argumente von `tasks.watch`:

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

   Eine Beobachtung ist eine Aufgabe mit einem Auslöser `event`. Nach jedem Durchlauf wird sie neu scharf geschaltet und wartet auf das nächste passende Ereignis. Füge `once: true` für eine einmalige Erinnerung hinzu: Sie löst beim ersten Treffer aus und endet mit `done`.

   ```text
   Erinnere mich daran, Dübel zu kaufen, wenn ich beim Hornbach bin.
   ```

   Für einen Ort schlägt der Agent ihn mit `places_find` nach und filtert auf deine Personen-Entität mit `to` (ankommen) oder `from` (weggehen).

2. **Schreib den Filter**

   Der Filter ist ein JSON-Objekt. `source` ist Pflicht.

   | Schlüssel | Bedeutung |
   |---|---|
   | `source` | `ha`, `printer`, `mail`, `uptime-kuma`, `proxmox`, `seerr`, ... |
   | `type` | Optionales Glob-Muster auf den Ereignistyp, zum Beispiel `state_changed`, `printer.complete`, `monitor.down`. |
   | `entity` | Optionales Glob-Muster auf die Entität, zum Beispiel `person.lukas` oder `binary_sensor.*`. |
   | jeder andere Schlüssel | Eine Bedingung auf ein Feld der Nutzdaten. Ein Pfad mit Punkten erreicht verschachtelte Felder (`attributes.battery`). |

   Ein Bedingungswert ist ein Glob-Muster als Zeichenkette (`*` passt auf beliebigen Text, ohne Beachtung der Groß- und Kleinschreibung), eine Zahl, `true` / `false`, `null` (Feld fehlt) oder eine Liste davon (einer davon muss passen). Zahlen passen auch auf Zeichenketten mit Zahlen, weil Zustände in Home Assistant Zeichenketten sind.

   | Ziel | Filter |
   |---|---|
   | Du kommst nach Hause | `source: ha`, `entity: person.lukas`, `to: home` |
   | Du verlässt die Arbeit | `source: ha`, `entity: person.lukas`, `from: Work` |
   | NAS-Monitor ausgefallen | `source: uptime-kuma`, `type: monitor.down`, `entity: NAS` |
   | Batterie unter einem Wert | `source: ha`, `entity: sensor.door_battery`, dann die passende Bedingung auf die Nutzdaten |

   Ein ungültiger Filter scheitert mit: `An event filter is a JSON object with a source, optional type and entity, and payload conditions (text, number, true/false or a list of them).`

3. **Wisse, wie das Abgleichen funktioniert**

   Das Abgleichen ist deterministisch und läuft, bevor eine Aufgabe startet; ein Ereignis, das auf keinen Filter passt, kostet keinen Modellaufruf. Eine Beobachtung löst pro Scharfschaltung höchstens einmal aus. Das passende Ereignis wird zu den Auslöser-Nutzdaten des Durchlaufs.
:::

## Vertrauen und Belastung

Nutzdaten von Ereignissen sind standardmäßig nicht vertrauenswürdig. Ein Durchlauf, den ein Ereignis startet, bekommt die Nutzdaten als nicht vertrauenswürdige Daten eingepackt und ist mit der Quelle `event` belastet: Schreibvorgänge in Gedächtnis und Aufgaben brauchen in diesem Durchlauf deine Bestätigung. Nur Quellen, die sich im Code selbst als vertrauenswürdig erklären (die Webhook-Quellen `uptime-kuma`, `proxmox` und `seerr`), reichen die Nutzdaten unverpackt weiter und lassen den Durchlauf unbelastet. Vertrauen lässt sich weder über die Konfiguration noch über den Inhalt des Ereignisses setzen, und ein Feld `trusted: true` in den Nutzdaten ändert nichts. Siehe [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md).

## Ruhezeiten

Eine Beobachtung, die dich nachts benachrichtigt, folgt deinen Ruhezeiten. Eine Ausnahme davon (`overrideQuietHoursUntil`) braucht deine Bestätigung.

## Prüfen, ob es funktioniert

Löse das Ereignis aus oder sieh unter **Aufgaben** nach: Die Beobachtung erscheint als `watch` (oder `once`) mit ihrem Status. Die Systemseite listet die Ereignisquellen, und jede Webhook-Quelle zeigt die Zeit ihres zuletzt empfangenen Webhooks.

## Verwandte Seiten

::: cards
- [Geplante Aufgaben](scheduled-tasks.md)
- [Freigaben](approvals.md)
- [Referenz der Ereignisse](../../reference/events.md)
- [Aufgabendefinitionen](../../configuration/tasks-format.md)
:::
