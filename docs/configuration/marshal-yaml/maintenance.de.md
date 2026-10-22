---
icon: fa-solid fa-wrench
description: "Zeitpläne und Aufbewahrung der Systemaufgaben, die Marshal für den Eigentümer der Instanz anlegt (nächtliche Sicherung, Konsolidierung des Gedächtnisses, Skill-Vorschläge und die Bereinigung), und die nutzungsbasierten Wartungseinträge, die Marshal verfolgt."
---

# `maintenance`

Zeitpläne und Aufbewahrung der Systemaufgaben, die Marshal für den Eigentümer der Instanz anlegt (nächtliche Sicherung, Konsolidierung des Gedächtnisses, Skill-Vorschläge und die Bereinigung), und die nutzungsbasierten Wartungseinträge, die Marshal verfolgt.

## Beispiel

```yaml
maintenance:
  backupSchedule: "0 3 * * *"
  backupCopies: 7
  consolidationSchedule: "30 2 * * *"
  retentionSchedule: "30 3 * * *"
  skillProposalSchedule: "0 4 * * *"
  payloadRetention: "30.00:00:00"
  metadataRetention: "365.00:00:00"
  items:
    voron_nozzle:
      name: Voron nozzle
      source: { printer: voron, metric: printHours }
      every: 300
      warnBefore: 20
    dishwasher_filter:
      name: Dishwasher filter
      source: { ha: sensor.dishwasher_cycles }
      every: 60
```

## Schlüssel

Zeitpläne sind cron-Ausdrücke mit fünf Feldern in der Zeitzone des Eigentümers.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `maintenance.backupSchedule` | cron | `0 3 * * *` | Sicherung: SQLite-Kopie (`VACUUM INTO`), eine Kopie von `/data/keys` und der NDJSON-Export des Gedächtnisses jedes Eigentümers, nach `/data/backups/<stamp>`. |
| `maintenance.backupCopies` | Ganzzahl | `7` | Behaltene Sicherungen. Ältere werden nach jeder Sicherung gelöscht. |
| `maintenance.consolidationSchedule` | cron | `30 2 * * *` | Gedächtniszusammenfassungen der geschlossenen Sitzungen des Tages, die der Weg beim Schließen verpasst hat. |
| `maintenance.retentionSchedule` | cron | `30 3 * * *` | Bereinigung nach Aufbewahrungsfristen. |
| `maintenance.skillProposalSchedule` | cron | `0 4 * * *` | [Skill-Vorschläge](../../guides/skill-proposals.md) aus wiederholten Abläufen, für Eigentümer mit eingeschaltetem „Marshal beim Lernen helfen“. |
| `maintenance.payloadRetention` | Dauer | `30.00:00:00` | Vollständige Nutzdaten der Schritte werden danach gelöscht. Die Metadaten der Schritte bleiben. |
| `maintenance.metadataRetention` | Dauer | `365.00:00:00` | Durchläufe und die Metadaten ihrer Schritte werden danach gelöscht. Gedächtniseinträge sind nie betroffen. |
| `maintenance.items` | Abbildung | keiner | Nutzungsbasierte Wartungseinträge nach ID (`[a-z][a-z0-9_]*`). Ohne Einträge sind die Werkzeuge `maintenance` und das Widget auf der Startseite verborgen. |
| `maintenance.items.<id>.name` | Zeichenkette | Pflicht | Anzeigename, zum Beispiel `Voron nozzle`. |
| `maintenance.items.<id>.source.ha` | Zeichenkette | keiner | Eine Entitäts-ID oder Statistik-ID in Home Assistant mit einem numerischen, steigenden Wert (ein Zyklen- oder Laufzeitzähler). |
| `maintenance.items.<id>.source.printer` | Zeichenkette | keiner | Der `name` eines konfigurierten Druckers. Braucht `metric`. |
| `maintenance.items.<id>.source.metric` | `printHours` \| `filamentMeters` | keiner | Der Druckerzähler: die gesamte Druckzeit von Moonraker in Stunden oder das gesamte verbrauchte Filament in Metern. |
| `maintenance.items.<id>.every` | Zahl | Pflicht | Das Intervall in der Einheit des Zählers. Der Eintrag ist fällig, wenn die Nutzung es erreicht. |
| `maintenance.items.<id>.warnBefore` | Zahl | keiner | So viel vor `every` benachrichtigen, mit dem Zustand `soon`. Muss kleiner als `every` sein. |

Eine Quelle hat entweder `ha` oder `printer` mit `metric`, nie beides. Marshal speichert pro Eintrag eine Basis, den Zählerwert, als der Eintrag zuletzt erledigt wurde; die Nutzung ist der aktuelle Wert minus der Basis. Beim ersten Sehen ist die Basis der aktuelle Wert. Fällt ein Zähler unter seine Basis (ein Zurücksetzen), wandert die Basis auf den neuen Wert. Marshal liest Zähler bei Zustandsänderungen der Entität, wenn ein Druck fertig wird, und stündlich.

## Validierung

- `maintenance.backupSchedule must be a five-field cron expression.` (genauso für `consolidationSchedule`, `retentionSchedule`, `skillProposalSchedule`)
- `maintenance.backupCopies must be at least 1.`
- `maintenance.payloadRetention must be at least a day and metadataRetention at least as long.`
- `maintenance.items.<id>: an item id must match [a-z][a-z0-9_]*.`
- `maintenance.items.<id>.name is required.`
- `maintenance.items.<id>.source must have either ha or printer.`
- `maintenance.items.<id>.source.metric belongs to a printer source.`
- `maintenance.items.<id>.source.metric must be printHours or filamentMeters.`
- `maintenance.items.<id>.source.printer: there is no printer '<name>'.`
- `maintenance.items.<id>.every must be a positive number.`
- `maintenance.items.<id>.warnBefore must be positive and less than every.`

## Verwandte Seiten

::: cards
- [Sicherungen und Wiederherstellung](../../guides/backups-and-restore.md)
- [Wartung nach Nutzung verfolgen](../../guides/maintenance.md)
- [`suggestions`](suggestions.md)
- [`recording`](recording.md)
- [Datenverzeichnis](../../reference/data-directory.md)
:::
