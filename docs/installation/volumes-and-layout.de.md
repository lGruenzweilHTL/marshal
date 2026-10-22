---
icon: fa-solid fa-hard-drive
order: 20
description: "Was in /config und /data liegt und was du sichern musst."
---

# Volumes und Aufbau

Was in `/config` und `/data` liegt und was du sichern musst.

## `/config`

Von Hand bearbeitet, git-tauglich, ohne Secrets. Häng es schreibgeschützt ein. Marshal beobachtet es: Risikoregeln, Satellitenprofile, Skills, Templates, Aufgaben und MCP-Server laden ohne Neustart neu. Modelle, Authentifizierung und die meisten Abschnitte von `marshal.yaml` brauchen einen Neustart. Ein Neuladen, das die Validierung nicht besteht, behält die vorherige Konfiguration und protokolliert den Fehler.

| Pfad | Inhalt |
|---|---|
| `marshal.yaml` | Konfiguration der Instanz |
| `risk.yaml` | Risikoregeln |
| `satellites.yaml` | Profile pro Satellit |
| `mcp/` | MCP-Server-Dateien, eine pro Server |
| `skills/` | Skills (`SKILL.md`) |
| `templates/` | Agent-Templates |
| `tasks/` | Aufgabendateien, zum Beispiel das tägliche Briefing |

Der Konfigurationsscanner weist Secrets im Klartext in jeder Datei hier ab. Siehe [Secrets](../guides/secrets.md).

## `/data`

Alles, was Marshal speichert. Sichere dieses Volume.

| Pfad | Inhalt |
|---|---|
| `marshal.db` | SQLite-Datenbank |
| `marshal.db.<yyyyMMddHHmmss>.bak` | Kopie vor ausstehenden Migrationen ([Aktualisieren](upgrading.md)) |
| `keys/` | ASP.NET-Data-Protection-Schlüssel. Sie entschlüsseln die Secrets in der Datenbank |
| `backups/<stamp>/` | Nächtliches Backup: Kopie der Datenbank, `keys`, Gedächtnisexporte (NDJSON) |
| `artifacts/` | Artefaktdateien |
| `snapshots/` | Drucker-Snapshots |
| `notes/` | Git-Repository der Notizen (ein Klon, wenn `notes.remote` gesetzt ist) |
| `cache/renditions/` | Verzichtbarer Cache gerenderter 3D-Seiten (höchstens 512 MB) |

> [!IMPORTANT]
> Ohne `/data/keys` lassen sich die Secrets in der Datenbank nicht entschlüsseln. Das nächtliche Backup enthält die Schlüssel.

## Nächtliches Backup

Die Systemaufgabe `maintenance.backupSchedule` (Standard `0 3 * * *`, in der Zeitzone des Eigentümers) schreibt ein SQLite-Online-Backup, kopiert `keys` und exportiert das Gedächtnis. `maintenance.backupCopies` (Standard 7) Kopien bleiben erhalten. Die Backups liegen in `/data`, kopiere sie also auch vom Host weg. Siehe [Backups und Wiederherstellung](../guides/backups-and-restore.md).

## Überschreiben per Umgebung

`MARSHAL__Paths__Config`, `MARSHAL__Paths__Data` und `MARSHAL__Paths__Secrets` verlegen die Verzeichnisse.

## Verwandte Seiten

::: cards
- [Referenz des Datenverzeichnisses](../reference/data-directory.md)
- [Docker Compose](docker-compose.md)
:::
