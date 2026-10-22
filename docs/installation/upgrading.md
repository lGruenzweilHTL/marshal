---
order: 70
---

# Upgrading

Update Marshal to a newer version of the repository.

## Before you start

- A current backup of `/data` (`backups/` and `keys/` included). See [backups and restore](../guides/backups-and-restore.md).
- The changelog for the version you move to ([CHANGELOG](https://github.com/lGruenzweilHTL/marshal/blob/master/CHANGELOG.md)).

## Steps

```sh
git pull
cd deploy
docker compose up -d --build
```

## What happens at start

If the database has pending EF Core migrations, Marshal first copies the database file to `/data/marshal.db.<yyyyMMddHHmmss>.bak` (after a WAL checkpoint), then applies the migrations. The log says `Copied database to ... before migrating` and `Applied N pending database migrations`. Migrations only move forward; there is no downgrade. Memory is re-embedded when you changed the embedding model.

The `marshal tokens` command runs the same migration step.

## Roll back

1. Stop Marshal.
2. Check out the previous version and rebuild.
3. Replace `marshal.db` with the `.bak` file taken before the migration (or a nightly backup).
4. Start Marshal.

## Check that it works

`docker compose logs marshal` shows a clean start, and `GET /health` is not `unhealthy`. After upgrading Marshal, update the Home Assistant integration from the same version. Its setup checks that Marshal supports API version 1.

## Related
- [Database migrations](../development/database-migrations.md)
- [Supported versions](../reference/supported-versions.md)
