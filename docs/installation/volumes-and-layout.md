---
order: 20
---

# Volumes and layout

What lives in `/config` and `/data`, and what to back up.

## `/config`

Human-edited, git-friendly, no secrets. Mount it read-only. Marshal watches it: risk rules, satellite profiles, skills, templates, tasks and MCP servers reload without restart. Models, authentication and most `marshal.yaml` sections need a restart. A reload that fails validation keeps the previous configuration and logs the error.

| Path | Content |
|---|---|
| `marshal.yaml` | Instance configuration |
| `risk.yaml` | Risk rules |
| `satellites.yaml` | Per-satellite profiles |
| `mcp/` | MCP server files, one per server |
| `skills/` | Skills (`SKILL.md`) |
| `templates/` | Agent templates |
| `tasks/` | Task files, for example the daily briefing |

The configuration scanner rejects literal secrets in any file here. See [secrets](../guides/secrets.md).

## `/data`

Everything Marshal stores. Back this volume up.

| Path | Content |
|---|---|
| `marshal.db` | SQLite database |
| `marshal.db.<yyyyMMddHHmmss>.bak` | Copy taken before pending migrations ([upgrading](upgrading.md)) |
| `keys/` | ASP.NET Data Protection keys. They decrypt secrets stored in the database |
| `backups/<stamp>/` | Nightly backup: database copy, `keys`, memory exports (NDJSON) |
| `artifacts/` | Artifact files |
| `snapshots/` | Printer snapshots |
| `notes/` | Notes git repository (a clone when `notes.remote` is set) |
| `cache/renditions/` | Disposable cache of rendered 3D pages (at most 512 MB) |

> [!IMPORTANT]
> Without `/data/keys` the secrets stored in the database cannot be decrypted. The nightly backup includes the keys.

## Nightly backup

The system task `maintenance.backupSchedule` (default `0 3 * * *`, in the owner's time zone) writes an SQLite online backup, copies `keys` and exports memory. `maintenance.backupCopies` (default 7) copies are kept. The backups live inside `/data`, so also copy them off the host. See [backups and restore](../guides/backups-and-restore.md).

## Environment overrides

`MARSHAL__Paths__Config`, `MARSHAL__Paths__Data` and `MARSHAL__Paths__Secrets` move the directories.

## Related
- [Data directory reference](../reference/data-directory.md)
- [Docker Compose](docker-compose.md)
