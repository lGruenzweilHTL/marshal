---
icon: fa-solid fa-folder-tree
description: "Der Aufbau des Volumes /data: jede Datei und jeder Ordner, den Marshal dort anlegt, welche du gefahrlos löschen kannst und welche du sichern musst."
---

# Datenverzeichnis

Der Aufbau des Volumes `/data`: jede Datei und jeder Ordner, den Marshal dort anlegt, welche du gefahrlos löschen kannst und welche du sichern musst.

Der Pfad kommt aus `paths.data` (Umgebung `MARSHAL__Paths__Data`, standardmäßig `/data`). Das Container-Image deklariert `/data` als Volume und lässt Marshal als Benutzer `app` laufen, der dorthin schreiben können muss.

## Aufbau

```tree
/data
  marshal.db  # SQLite-Datenbank
  marshal.db-wal, marshal.db-shm  # Write-Ahead-Log-Dateien von SQLite (während des Betriebs)
  marshal.db.<yyyyMMddHHmmss>.bak  # Kopie vor einer ausstehenden Migration
  keys/  # Data-Protection-Schlüssel von ASP.NET
  backups/
    <yyyyMMdd-HHmmss>/  # Eine nächtliche Sicherung
      marshal.db  # Online-Kopie der Datenbank
      keys/  # Kopie der Data-Protection-Schlüssel
      memory-<owner id>.ndjson  # Export des Gedächtnisses, eine Datei pro Eigentümer
  artifacts/
    <owner id>/
      <artifact id>  # Eine Artefaktdatei pro Artefakt
  cache/
    renditions/  # Gerenderte Seiten von 3D-Modellen
  snapshots/
    <printer>/<id>.jpg  # Webcam-Snapshots der Drucker
  notes/  # git-Repository der Notizen
```

## Einträge

| Pfad | Inhalt | Sichern | Gefahrlos löschbar |
|---|---|---|---|
| `marshal.db` | Alle Daten von Marshal: Sitzungen, Durchläufe, Gedächtnis, Aufgaben, Benachrichtigungen, Konten, Datensätze der Artefakte, verschlüsselte Secrets. Jede Zeile mit Benutzerdaten hat ein `owner_id`. | Ja | Nein |
| `marshal.db-wal`, `marshal.db-shm` | Write-Ahead-Log und Shared Memory von SQLite. SQLite verwaltet sie. | Teil der Datenbank | Nein, nicht während Marshal läuft |
| `marshal.db.<yyyyMMddHHmmss>.bak` | Eine vollständige Kopie der Datenbank, angelegt, bevor Marshal ausstehende Migrationen anwendet, benannt nach der UTC-Zeit. Marshal behält sie. Siehe [Aktualisieren](../installation/upgrading.md). | Optional | Ja, sobald das Upgrade funktioniert |
| `keys/` | Data-Protection-Schlüssel. Sie entschlüsseln die in der Datenbank gespeicherten Secrets. Ohne sie sind diese Secrets verloren. | Ja | Nein |
| `backups/<yyyyMMdd-HHmmss>/` | Eine nächtliche Sicherung des Systemjobs `backup`: `marshal.db` (eine Kopie per `VACUUM INTO`), `keys/` und `memory-<owner id>.ndjson` (die Gedächtniseinträge jedes Eigentümers als NDJSON). Die Zeit ist UTC. Marshal behält die neuesten `maintenance.backupCopies` Ordner (standardmäßig 7) und löscht ältere. | Vom Host wegkopieren | Ja, einzelne alte Sicherungen |
| `artifacts/<owner id>/<artifact id>` | Die Bytes jedes Artefakts, benannt nach der Artefakt-ID. Namen, Arten und Quellen stehen in der Datenbank. Es gilt ein Kontingent pro Benutzer (`artifacts.quotaMb`, standardmäßig 5120). | Ja | Nein |
| `cache/renditions/` | Gerenderte Seiten von 3D-Modellen. Auf 512 MB begrenzt. Marshal rendert sie bei Bedarf neu. | Nein | Ja |
| `snapshots/<printer>/` | JPEG-Snapshots der Webcam, ein Ordner pro Drucker. Marshal behält die neuesten `printerSnapshots.retention` pro Drucker (standardmäßig 20). | Optional | Ja |
| `notes/` | Das git-Repository der Notizen. Mit gesetztem `notes.remote` ist es ein Klon, den Marshal zieht; sonst ein lokales Repository, das du füllst. | Ja, außer es ist nur ein Klon | Nur ein Klon |

> [!IMPORTANT]
> Sichere `marshal.db` zusammen mit `keys/`. Eine ohne ihre Schlüssel wiederhergestellte Datenbank kann die darin gespeicherten Secrets nicht entschlüsseln.

> [!NOTE]
> Sicherungen liegen in `/data`, ein Plattenausfall nimmt sie also mit. Kopiere `backups/` auf eine andere Maschine. Siehe [Sicherungen und Wiederherstellung](../guides/backups-and-restore.md).

## Nicht in `/data`

| Ort | Inhalt |
|---|---|
| `/config` | Von Menschen bearbeitete Konfiguration (`marshal.yaml`, `risk.yaml`, `satellites.yaml`, `mcp/`, `skills/`, `templates/`, `tasks/`). Schreibgeschützt eingebunden. Keine Secrets. |
| `/run/secrets` | Docker-Secret-Dateien, gelesen vom Secret-Speicher. |

Die Sicht der Installation steht unter [Volumes und Aufbau](../installation/volumes-and-layout.md).

## Verwandte Seiten

::: cards
- [Sicherungen und Wiederherstellung](../guides/backups-and-restore.md)
- [`maintenance`](../configuration/marshal-yaml/maintenance.md)
- [Datenbesitz](../concepts/data-ownership.md)
- [Datenbankmigrationen](../development/database-migrations.md)
:::
