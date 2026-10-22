---
icon: fa-solid fa-circle-up
order: 70
description: "Aktualisiere Marshal auf eine neuere Version des Repositorys."
---

# Aktualisieren

Aktualisiere Marshal auf eine neuere Version des Repositorys.

## Bevor du anfängst

- Ein aktuelles Backup von `/data` (mit `backups/` und `keys/`). Siehe [Backups und Wiederherstellung](../guides/backups-and-restore.md).
- Das Änderungsprotokoll der Version, auf die du wechselst ([CHANGELOG](https://github.com/lGruenzweilHTL/marshal/blob/master/CHANGELOG.md)).

## Schritte

```sh
git pull
cd deploy
docker compose up -d --build
```

## Was beim Start passiert

Hat die Datenbank ausstehende EF-Core-Migrationen, kopiert Marshal zuerst die Datenbankdatei nach `/data/marshal.db.<yyyyMMddHHmmss>.bak` (nach einem WAL-Checkpoint) und wendet dann die Migrationen an. Das Log meldet `Copied database to ... before migrating` und `Applied N pending database migrations`. Migrationen gehen nur vorwärts; es gibt kein Downgrade. Das Gedächtnis wird neu eingebettet, wenn du das Embedding-Modell geändert hast.

Der Befehl `marshal tokens` führt denselben Migrationsschritt aus.

## Zurückrollen

1. Stoppe Marshal.
2. Checke die vorherige Version aus und baue neu.
3. Ersetze `marshal.db` durch die `.bak`-Datei von vor der Migration (oder ein nächtliches Backup).
4. Starte Marshal.

## Prüfen, ob es funktioniert

`docker compose logs marshal` zeigt einen sauberen Start, und `GET /health` ist nicht `unhealthy`. Aktualisiere nach Marshal auch die Home-Assistant-Integration aus derselben Version. Ihre Einrichtung prüft, ob Marshal API-Version 1 unterstützt.

## Verwandte Seiten

::: cards
- [Datenbankmigrationen](../development/database-migrations.md)
- [Unterstützte Versionen](../reference/supported-versions.md)
:::
