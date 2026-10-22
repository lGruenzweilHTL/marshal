# Events

The event bus carries normalized events from Home Assistant, printers, mail, executors, MCP server tasks and webhook sources. This page lists the event envelope, every source and event type with its payload fields, and the filter syntax that watches use.

## Envelope

Every event has the same shape.

| Field | Type | Description |
|---|---|---|
| `source` | string | Where the event comes from, for example `ha`, `printer`, `mail`, `executor`, `uptime-kuma`. |
| `type` | string | What happened, for example `state_changed`. |
| `entity` | string or null | What it happened to: an entity id, a printer name, a mail account alias, a monitor name. |
| `payload` | object | Source-specific fields, listed below. |
| `ownerId` | id or null | The owner the event belongs to. Instance-wide sources (`ha`, `printer`) carry none. |
| `time` | timestamp | When Marshal published the event. |

A run started by a watch receives the event as JSON with the fields `source`, `type`, `entity`, `time` and `payload`. A trusted event adds a top-level `trusted: true`. The event's own data always stays nested under `payload`, so content cannot set that key.

## Trust

Event payloads are untrusted by default. A run started by an event gets the payload wrapped as untrusted data and is tainted with source `event`. An event source is trusted only when its code says so; you cannot set trust in configuration or in event content.

| Source | Trusted |
|---|---|
| `ha` | No |
| `printer` | No |
| `mail` | No |
| `calendar` | Yes |
| `executor` | No |
| `mcp` | No |
| `uptime-kuma` | Yes |
| `proxmox` | Yes |
| `seerr` | Yes |

Trusted webhook sources carry only the owner's own monitor configuration, ids and numbers, never free text from remote systems.

## Home Assistant (`ha`)

Marshal receives these events from the Home Assistant websocket. They carry no owner.

| Type | Entity | Payload fields |
|---|---|---|
| `state_changed` | The entity id | `from` (previous state or null), `to` (new state or null), `attributes` (attributes of the new state). Published only when the state value changes, not for attribute-only changes. |
| `mobile_app_notification_action` | none | The event data of the companion app action, as Home Assistant sends it. Approve and Reject push actions arrive this way and are accepted only with a valid, unexpired nonce. |

## Printers (`printer`)

Marshal derives printer events from state transitions. Temperature changes never produce events. The last known state survives a disconnect, so a transition missed while offline is published after the reconnect. The entity is the printer name.

| Type | Published when |
|---|---|
| `started` | A print starts. |
| `paused` | A print pauses. |
| `resumed` | A paused print continues. |
| `complete` | A print finishes. |
| `error` | The print ends in an error. |
| `cancelled` | A print is cancelled. |
| `klippy_shutdown` | Klipper enters the shutdown state. |
| `progress` | Progress crosses 25, 50 or 75 percent. |
| `first_layer_done` | The slicer reports layers and the print reaches layer 2. |

All printer events share one payload.

| Field | Description |
|---|---|
| `printer` | Printer name. |
| `filename` | File being printed, or null. |
| `progress` | Progress from 0 to 1, rounded to three decimals. |
| `layer`, `totalLayers` | Current and total layer, or null without layer data. |
| `message` | Error or status message. For `klippy_shutdown` it is the Klipper state message. |
| `milestone` | 25, 50 or 75 for `progress`, otherwise null. |

## Mail (`mail`)

| Type | Entity | Payload fields |
|---|---|---|
| `new_mail` | The account alias | `account` (alias), `id` (message id for `mail.read_message`), `from` (address), `subject`, `date`. |

Marshal watches the inbox with IMAP IDLE and polls where IDLE is not available. The event carries the account's owner.

## Calendar (`calendar`)

Published for CalDAV calendars only, after polling each calendar every `pollMinutes`. The entity is the calendar id (`caldav:<account>/<calendar>`). The event carries the instance owner. Events that exist when Marshal starts produce none.

| Type | Published when |
|---|---|
| `event_created` | A new event appeared. |
| `event_updated` | An event changed. |
| `event_removed` | An event was deleted. |
| `invitation` | A new event lists the account's address as an attendee with RSVP `needs-action`. Published together with `event_created`. |

| Field | Description |
|---|---|
| `calendar` | Calendar id. |
| `eventId` | Event id (the iCalendar `UID`) for `calendar_update_event`. Null when the uid is not a plain identifier (letters, digits, `@._:+-`, at most 200 characters). |
| `start`, `end` | Start and end in UTC. Null for `event_removed`. For a recurring event, those of the first occurrence. |

The payload holds no summaries, descriptions or attendees, so the source is trusted.

## Executors (`executor`)

Marshal publishes one event when an executor session ends. The entity is the session id. The event carries the session's owner.

| Type | Meaning |
|---|---|
| `completed` | The executor finished. A change proposal is created when the executor reports a branch and a diff. |
| `failed` | The executor failed, or reported an unknown type. |
| `cancelled` | The session was cancelled. |

Payload fields: `session`, `executor`, `status`, `target`, `branch` and `proposal`, where present.

## MCP server tasks (`mcp`)

Marshal publishes one event when a server task that an MCP tool call started ends. The entity is `<server>/<task id>`. The event carries the owner of the call.

| Type | Meaning |
|---|---|
| `task_finished` | The server reported the task completed, failed or cancelled, or no longer knows it. |

Payload fields: `server`, `tool`, `task` and `status` (`completed`, `failed` or `cancelled`). The payload never contains the server's result or error text; the waiting task receives those.

## Webhook sources

Webhook sources arrive through `POST /api/v1/hooks/<source>`, each with a token that has the scope `hooks:<source>`. See [webhooks](../api/webhooks.md). Each source appears on the system page with the time of its last received webhook. The entity and payload are set by the source's normalizer. Posts that the normalizer does not map (test notices, unknown types) publish nothing.

### `uptime-kuma`

| Type | Entity | Payload fields |
|---|---|---|
| `monitor.down`, `monitor.up`, `monitor.pending`, `monitor.maintenance` | Monitor name | `monitorId`, `name`, `type` (monitor type), `url` (when the monitor has one), `time` (UTC), `pingMs`, `important`. |
| `certificate.expiring` | Monitor name | `name`, `days` (days until expiry). |

### `proxmox`

Marshal reads the `fields` object of the notification body. The entity is the `job-id` field, or the hostname when there is none.

| Type | Set by `fields.type` | Payload fields |
|---|---|---|
| `backup.ok` | `vzdump`, severity other than `error` | `type`, `severity`, `hostname`, `jobId`, `time`. |
| `backup.failed` | `vzdump`, severity `error` | Same. |
| `replication.failed` | `replication` | Same. |
| `node.fenced` | `fencing` | Same. |
| `updates.available` | `package-updates` | Same. |

### `seerr`

The entity is `<mediaType>:<tmdbId>`, for example `movie:438631`.

| Type | Seerr notification type | Payload fields |
|---|---|---|
| `media.available` | `MEDIA_AVAILABLE` | `mediaType` (`movie` or `tv`), `tmdbId`, `requestId`, `jellyfinItemId`, `jellyfin` (link). |
| `media.approved` | `MEDIA_APPROVED`, `MEDIA_AUTO_APPROVED` | Same. |
| `media.declined` | `MEDIA_DECLINED` | Same. |
| `media.failed` | `MEDIA_FAILED` | Same. |

## Watch filters

A watch (`tasks.watch`, or a task with an event trigger) selects events with a declarative filter. Marshal matches filters deterministically, without a model call, before any task run starts.

| Key | Type | Description |
|---|---|---|
| `source` | string, required | Event source. Case-insensitive. |
| `type` | string | Glob on the event type. |
| `entity` | string | Glob on the event entity. An event without an entity never matches. |
| any other key | text, number, boolean, null or a list of those | A condition on the payload. The key is a dotted path into the payload, for example `to` or `attributes.battery`. A list matches when any value matches. |

Rules for matching:

- `*` in a glob matches any run of characters. Text comparison ignores case.
- A number matches numeric strings, because Home Assistant states are strings.
- `null` matches a missing field.
- A condition value cannot be an object or an empty list.

A watch fires at most once per arming. A watch created with `once: true` fires on the first matching event and then ends.

Example: arriving home.

```json
{ "source": "ha", "entity": "person.lukas", "to": "home" }
```

Example: a failed print on any printer.

```json
{ "source": "printer", "type": "error" }
```

## Related

- [Event triggers guide](../guides/tasks/event-triggers.md)
- [Webhooks API](../api/webhooks.md)
- [Uptime Kuma](../guides/integrations/uptime-kuma.md)
- [Tools](tools.md) (`tasks.watch`)
