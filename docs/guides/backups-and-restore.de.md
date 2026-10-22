---
icon: fa-solid fa-floppy-disk
description: "Prüfe das nächtliche Backup, kopiere es von der Maschine weg und stell Marshal daraus wieder her."
---

# Backups und Wiederherstellung

Prüfe das nächtliche Backup, kopiere es von der Maschine weg und stell Marshal daraus wieder her.

## Bevor du anfängst

- Du erreichst die Volumes `/data` und `/config` und kannst auf dem Host `docker compose` ausführen.
- Aufbau der Volumes: [Volumes und Aufbau](../installation/volumes-and-layout.md).

## Was das nächtliche Backup enthält

Eine Systemaufgabe `system-backup` läuft nach `maintenance.backupSchedule` (Standard `0 3 * * *`, in der Zeitzone des Eigentümers). Sie schreibt einen Ordner `/data/backups/<yyyyMMdd-HHmmss>` (Zeitstempel in UTC):

| Datei | Inhalt |
|---|---|
| `marshal.db` | Online-Kopie der SQLite-Datenbank (`VACUUM INTO`), geschrieben, während Marshal läuft. |
| `keys/` | Kopie von `/data/keys`, den Data-Protection-Schlüsseln. Ohne sie lassen sich die verschlüsselt in der Datenbank gespeicherten Secrets nach einer Wiederherstellung nicht entschlüsseln. |
| `memory-<owner>.ndjson` | Gedächtnisexport pro Eigentümer: Einträge, Versionen und Kanten, ein JSON-Objekt pro Zeile, ohne Embeddings. |

Marshal behält die neuesten `maintenance.backupCopies` datierten Ordner (Standard 7) und löscht ältere. Andere Ordner in `/data/backups` fasst es nie an. Zwei weitere Systemaufgaben laufen nachts: `system-consolidation` (Gedächtniszusammenfassungen, `30 2 * * *`) und `system-retention` (`30 3 * * *`), die Nutzdaten von Schritten löscht, die älter als `payloadRetention` (30 Tage) sind, und Metadaten von Durchläufen, die älter als `metadataRetention` (365 Tage) sind. Gedächtniseinträge werden nie bereinigt. Alle Schlüssel stehen in [`maintenance`](../configuration/marshal-yaml/maintenance.md).

Das Backup enthält nicht `/config` (halte es in git), den Artefaktspeicher, Drucker-Snapshots, das Notizen-Repository oder Secrets aus Umgebungsvariablen oder Dateien in `/run/secrets`. Sichere sie getrennt.

## Schritte

::: steps
1. **Prüfe, ob Backups laufen**

   Öffne **Aufgaben**: `system-backup` zeigt sein letztes Ergebnis, zum Beispiel `Backup 20261011-010000: database, keys and 1 memory exports; 0 older backups deleted.` Oder liste den Ordner auf.

   ```sh
   docker compose exec marshal ls /data/backups
   ```

   Fehlt der Ordner mit den Schlüsseln, sagt das Ergebnis `no keys (none yet)`, und das Log enthält `No Data Protection keys at /data/keys to back up`.

2. **Kopiere die Backups vom Host weg**

   Häng `/data/backups` ein oder kopiere es regelmäßig mit deinem gewohnten Werkzeug. Ein Backup auf derselben Platte schützt nicht vor einem Plattenausfall.

   ```sh
   rsync -a <docker-volume-path>/backups/ <backup-host>:/backups/marshal/
   ```

3. **Mach jetzt ein Backup**

   Bitte Marshal, die Aufgabe `system-backup` auszuführen (`tasks_run_now`), oder warte auf den Zeitplan.

4. **Stell in eine neue oder kaputte Instanz wieder her**

   1. Stoppe Marshal.

      ```sh
      docker compose stop marshal
      ```

   2. Wähle den Backup-Ordner `<stamp>`, den du willst.
   3. Ersetze Datenbank und Schlüssel im Daten-Volume. Entferne alte SQLite-Nebendateien, damit kein veraltetes Journal angewendet wird.

      ```sh
      rm -f <data>/marshal.db <data>/marshal.db-wal <data>/marshal.db-shm
      cp <backup>/<stamp>/marshal.db <data>/marshal.db
      rm -rf <data>/keys
      cp -r <backup>/<stamp>/keys <data>/keys
      ```

   4. Stell deine Dateien in `/config` und alle Secrets in `/run/secrets` oder in Umgebungsvariablen wieder her.
   5. Starte Marshal. Ausstehende Datenbankmigrationen laufen beim Start.

      ```sh
      docker compose up -d marshal
      ```

   > [!IMPORTANT]
   > Stell `marshal.db` und `keys/` zusammen wieder her. Eine Datenbank ohne ihre Schlüssel verliert jedes Secret, das in der Weboberfläche gespeichert wurde.

   > [!NOTE]
   > Der NDJSON-Export des Gedächtnisses ist eine portable Kopie des Gedächtnisses. Marshal hat keinen Befehl und keine Ansicht, die ihn importiert. Die Wiederherstellung von `marshal.db` stellt das Gedächtnis wieder her, einschließlich der Embeddings. Wechselst du das Embedding-Modell, bettet Marshal beim nächsten Start das ganze Gedächtnis neu ein.
:::

## Prüfen, ob es funktioniert

Öffne die Weboberfläche, melde dich an und prüfe **System** auf den Zustand, **Gedächtnis** auf deine Einträge und **Aufgaben** auf deine Aufgaben. `GET /health` liefert `status` `healthy`.

## Verwandte Seiten

::: cards
- [Aktualisieren](../installation/upgrading.md)
- [Datenverzeichnis](../reference/data-directory.md)
- [Secrets](secrets.md)
- [Fehlerbehebung](troubleshooting.md)
:::
