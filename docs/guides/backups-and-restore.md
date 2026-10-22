---
icon: fa-solid fa-floppy-disk
---

# Backups and restore

Check the nightly backup, copy it off the machine, and restore Marshal from it.

## Before you start

- You can reach the `/data` and `/config` volumes and run `docker compose` on the host.
- Volume layout: [volumes and layout](../installation/volumes-and-layout.md).

## What the nightly backup contains

A system task `system-backup` runs on `maintenance.backupSchedule` (default `0 3 * * *`, in the owner's time zone). It writes a folder `/data/backups/<yyyyMMdd-HHmmss>` (UTC stamp):

| File | Content |
|---|---|
| `marshal.db` | Online copy of the SQLite database (`VACUUM INTO`), written while Marshal runs. |
| `keys/` | Copy of `/data/keys`, the Data Protection keys. Without them, secrets stored encrypted in the database cannot be decrypted after a restore. |
| `memory-<owner>.ndjson` | Memory export per owner: chunks, versions and edges, one JSON object per line, without embeddings. |

Marshal keeps the newest `maintenance.backupCopies` dated folders (default 7) and deletes older ones. Other folders in `/data/backups` are never touched. Two more system tasks run nightly: `system-consolidation` (memory summaries, `30 2 * * *`) and `system-retention` (`30 3 * * *`), which deletes run step payloads older than `payloadRetention` (30 days) and run metadata older than `metadataRetention` (365 days). Memory chunks are never purged. All keys are in [`maintenance`](../configuration/marshal-yaml/maintenance.md).

The backup does not contain `/config` (keep it in git), the artifact store, printer snapshots, the notes repository or secrets supplied by environment variables or `/run/secrets` files. Back those up separately.

## Steps

::: steps
1. **Check that backups run**

   Open **Tasks**: `system-backup` shows its latest result, for example `Backup 20261011-010000: database, keys and 1 memory exports; 0 older backups deleted.` Or list the folder.

   ```sh
   docker compose exec marshal ls /data/backups
   ```

   If the keys folder is missing, the result says `no keys (none yet)` and the log has `No Data Protection keys at /data/keys to back up`.

2. **Copy backups off the host**

   Mount `/data/backups` or copy it regularly with your usual tool. A backup on the same disk does not protect against disk failure.

   ```sh
   rsync -a <docker-volume-path>/backups/ <backup-host>:/backups/marshal/
   ```

3. **Run a backup now**

   Ask Marshal to run the `system-backup` task (`tasks_run_now`), or wait for the schedule.

4. **Restore into a new or broken instance**

   1. Stop Marshal.

      ```sh
      docker compose stop marshal
      ```

   2. Choose the backup folder `<stamp>` you want.
   3. Replace the database and keys in the data volume. Remove old SQLite side files so no stale journal is applied.

      ```sh
      rm -f <data>/marshal.db <data>/marshal.db-wal <data>/marshal.db-shm
      cp <backup>/<stamp>/marshal.db <data>/marshal.db
      rm -rf <data>/keys
      cp -r <backup>/<stamp>/keys <data>/keys
      ```

   4. Restore your `/config` files and any secrets in `/run/secrets` or environment variables.
   5. Start Marshal. Pending database migrations run at startup.

      ```sh
      docker compose up -d marshal
      ```

   > [!IMPORTANT]
   > Restore `marshal.db` and `keys/` together. A database restored without its keys loses every secret stored in the web UI.

   > [!NOTE]
   > The memory NDJSON export is a portable copy of memory. Marshal has no command or screen that imports it. Restoring `marshal.db` restores memory, including embeddings. If you change the embedding model, Marshal re-embeds all memory at the next start.
:::

## Check that it works

Open the web UI, sign in, and check **System** for health, **Memory** for your chunks and **Tasks** for your tasks. `GET /health` returns `status` `healthy`.

## Related

- [Upgrading](../installation/upgrading.md), [data directory](../reference/data-directory.md), [secrets](secrets.md)
- [Troubleshooting](troubleshooting.md)
