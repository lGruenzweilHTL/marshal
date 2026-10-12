# `maintenance`

Schedules and retention of the system tasks Marshal seeds for the instance owner: nightly backup, memory consolidation and the retention purge.

## Example

```yaml
maintenance:
  backupSchedule: "0 3 * * *"
  backupCopies: 7
  consolidationSchedule: "30 2 * * *"
  retentionSchedule: "30 3 * * *"
  payloadRetention: "30.00:00:00"
  metadataRetention: "365.00:00:00"
```

## Keys

Schedules are five-field cron expressions in the owner's time zone.

| Key | Type | Default | Description |
|---|---|---|---|
| `maintenance.backupSchedule` | cron | `0 3 * * *` | Backup: SQLite copy (`VACUUM INTO`), a copy of `/data/keys` and the NDJSON memory export of every owner, into `/data/backups/<stamp>`. |
| `maintenance.backupCopies` | integer | `7` | Backups kept. Older ones are deleted after each backup. |
| `maintenance.consolidationSchedule` | cron | `30 2 * * *` | Memory summaries of the day's closed sessions that the close path missed. |
| `maintenance.retentionSchedule` | cron | `30 3 * * *` | Retention purge. |
| `maintenance.payloadRetention` | duration | `30.00:00:00` | Full run step payloads are deleted after this. Step metadata stays. |
| `maintenance.metadataRetention` | duration | `365.00:00:00` | Runs and their step metadata are deleted after this. Memory chunks are never affected. |

## Validation

- `maintenance.backupSchedule must be a five-field cron expression.` (same for `consolidationSchedule`, `retentionSchedule`)
- `maintenance.backupCopies must be at least 1.`
- `maintenance.payloadRetention must be at least a day and metadataRetention at least as long.`

## Related

- [Backups and restore](../../guides/backups-and-restore.md)
- [recording](recording.md)
- [Data directory](../../reference/data-directory.md)
