# Data directory

The layout of the `/data` volume: every file and folder Marshal creates there, which ones are safe to delete and which you must back up.

The path comes from `paths.data` (environment `MARSHAL__Paths__Data`, default `/data`). The container image declares `/data` as a volume and runs Marshal as the user `app`, which must be able to write to it.

## Layout

```text
/data
  marshal.db                      SQLite database
  marshal.db-wal, marshal.db-shm  SQLite write-ahead log files (while running)
  marshal.db.<yyyyMMddHHmmss>.bak Copy taken before a pending migration
  keys/                           ASP.NET Data Protection keys
  backups/
    <yyyyMMdd-HHmmss>/            One nightly backup
      marshal.db                  Online copy of the database
      keys/                       Copy of the Data Protection keys
      memory-<owner id>.ndjson    Memory export, one file per owner
  artifacts/
    <owner id>/
      <artifact id>               One artifact file per artifact
  cache/
    renditions/                   Rendered pages of 3D models
  snapshots/
    <printer>/<id>.jpg            Printer webcam snapshots
  notes/                          Notes git repository
```

## Entries

| Path | Content | Back up | Safe to delete |
|---|---|---|---|
| `marshal.db` | All Marshal data: sessions, runs, memory, tasks, notifications, accounts, artifact records, encrypted secrets. Every user-owned row has an `owner_id`. | Yes | No |
| `marshal.db-wal`, `marshal.db-shm` | SQLite write-ahead log and shared memory. SQLite manages them. | Part of the database | No, not while Marshal runs |
| `marshal.db.<yyyyMMddHHmmss>.bak` | A full copy of the database taken before Marshal applies pending migrations, named with the UTC time. Marshal keeps them. See [upgrading](../installation/upgrading.md). | Optional | Yes, once the upgrade works |
| `keys/` | Data Protection keys. They decrypt secrets stored in the database. Without them those secrets are lost. | Yes | No |
| `backups/<yyyyMMdd-HHmmss>/` | A nightly backup written by the `backup` system job: `marshal.db` (a `VACUUM INTO` copy), `keys/` and `memory-<owner id>.ndjson` (the memory chunks of each owner as NDJSON). The time is UTC. Marshal keeps the newest `maintenance.backupCopies` folders (default 7) and deletes older ones. | Copy off the host | Yes, individual old backups |
| `artifacts/<owner id>/<artifact id>` | The bytes of each artifact, named by artifact id. Names, kinds and sources are in the database. A per-user quota applies (`artifacts.quotaMb`, default 5120). | Yes | No |
| `cache/renditions/` | Rendered pages of 3D models. Capped at 512 MB. Marshal renders them again on demand. | No | Yes |
| `snapshots/<printer>/` | JPEG webcam snapshots, one folder per printer. Marshal keeps the newest `printerSnapshots.retention` per printer (default 20). | Optional | Yes |
| `notes/` | The notes git repository. With `notes.remote` set, it is a clone that Marshal pulls; otherwise it is a local repository you fill. | Yes, unless it is only a clone | A clone only |

> [!IMPORTANT]
> Back up `marshal.db` together with `keys/`. A database restored without its keys cannot decrypt the secrets stored in it.

> [!NOTE]
> Backups live inside `/data`, so a disk failure takes them too. Copy `backups/` to another machine. See [backups and restore](../guides/backups-and-restore.md).

## Not in `/data`

| Location | Content |
|---|---|
| `/config` | Human-edited configuration (`marshal.yaml`, `risk.yaml`, `satellites.yaml`, `mcp/`, `skills/`, `templates/`, `tasks/`). Mounted read-only. No secrets. |
| `/run/secrets` | Docker secret files, read by the secret store. |

See [volumes and layout](../installation/volumes-and-layout.md) for the installation view.

## Related

- [Backups and restore](../guides/backups-and-restore.md)
- [`maintenance`](../configuration/marshal-yaml/maintenance.md)
- [Data ownership](../concepts/data-ownership.md)
- [Database migrations](../development/database-migrations.md)
