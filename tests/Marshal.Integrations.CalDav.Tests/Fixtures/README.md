# CalDAV fixtures

- `radicale/`: recorded from Radicale 3 (`tomsquest/docker-radicale`, htpasswd auth) on 2026-10-20: discovery,
  `calendar-query`, `sync-collection` (initial, changes, invalid token).
- `nextcloud/`, `icloud/`: written after the response formats of sabre/dav (Nextcloud) and iCloud CalDAV: prefixed
  namespaces, scheduling inbox and outbox, read-only and task-only collections, and on iCloud a calendar home set on
  another host. Not recorded from a live server.
