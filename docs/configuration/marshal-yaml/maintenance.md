---
icon: fa-solid fa-wrench
description: "Schedules and retention of the system tasks Marshal seeds for the instance owner (nightly backup, memory consolidation, skill proposals and the retention purge), and the usage-based maintenance items Marshal tracks."
---

# `maintenance`

Schedules and retention of the system tasks Marshal seeds for the instance owner (nightly backup, memory consolidation, skill proposals and the retention purge), and the usage-based maintenance items Marshal tracks.

## Example

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

## Keys

Schedules are five-field cron expressions in the owner's time zone.

| Key | Type | Default | Description |
|---|---|---|---|
| `maintenance.backupSchedule` | cron | `0 3 * * *` | Backup: SQLite copy (`VACUUM INTO`), a copy of `/data/keys` and the NDJSON memory export of every owner, into `/data/backups/<stamp>`. |
| `maintenance.backupCopies` | integer | `7` | Backups kept. Older ones are deleted after each backup. |
| `maintenance.consolidationSchedule` | cron | `30 2 * * *` | Memory summaries of the day's closed sessions that the close path missed. |
| `maintenance.retentionSchedule` | cron | `30 3 * * *` | Retention purge. |
| `maintenance.skillProposalSchedule` | cron | `0 4 * * *` | [Skill proposals](../../guides/skill-proposals.md) from repeated workflows, for owners with "help Marshal learn" on. |
| `maintenance.payloadRetention` | duration | `30.00:00:00` | Full run step payloads are deleted after this. Step metadata stays. |
| `maintenance.metadataRetention` | duration | `365.00:00:00` | Runs and their step metadata are deleted after this. Memory chunks are never affected. |
| `maintenance.items` | map | none | Usage-based maintenance items by id (`[a-z][a-z0-9_]*`). Without items, the `maintenance` tools and the Home widget are hidden. |
| `maintenance.items.<id>.name` | string | required | Display name, for example `Voron nozzle`. |
| `maintenance.items.<id>.source.ha` | string | none | A Home Assistant entity id or statistic id with a numeric, increasing value (a cycle or runtime counter). |
| `maintenance.items.<id>.source.printer` | string | none | The `name` of a configured printer. Needs `metric`. |
| `maintenance.items.<id>.source.metric` | `printHours` \| `filamentMeters` | none | The printer counter: Moonraker's total print time in hours or total filament used in meters. |
| `maintenance.items.<id>.every` | number | required | The interval in the counter's unit. The item is due when usage reaches it. |
| `maintenance.items.<id>.warnBefore` | number | none | Notify this much before `every`, with state `soon`. Must be less than `every`. |

A source has either `ha` or `printer` with `metric`, never both. Marshal stores a baseline per item, the counter value when the item was last done; usage is the current value minus the baseline. On first sight the baseline is the current value. When a counter drops below its baseline (a reset), the baseline moves to the new value. Marshal reads counters on state changes of the entity, when a print completes, and hourly.

## Validation

- `maintenance.backupSchedule must be a five-field cron expression.` (same for `consolidationSchedule`, `retentionSchedule`, `skillProposalSchedule`)
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

## Related

::: cards
- [Backups and restore](../../guides/backups-and-restore.md)
- [Track maintenance by usage](../../guides/maintenance.md)
- [`suggestions`](suggestions.md)
- [`recording`](recording.md)
- [Data directory](../../reference/data-directory.md)
:::
