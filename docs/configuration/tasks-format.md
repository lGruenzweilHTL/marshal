# Task definitions (`tasks/*.yaml`)

The files in `deploy/config.example/tasks/` are example arguments for the `tasks.create` and `tasks.watch` tools. They show the shape of a task; Marshal does not read a `/config/tasks` directory.

> [!IMPORTANT]
> Marshal does not load `tasks/*.yaml` from `/config`. Create the task by asking Marshal ("create the morning briefing task with these settings") or by passing the arguments to the tool from the web UI. Afterwards the task is stored in the database and edited like any other task with `tasks.update`.

## Example: scheduled task (`tasks.create`)

```yaml
name: briefing
template: briefing
goal: >-
  Prepare today's morning briefing. Follow the briefing-format skill.
trigger:
  kind: cron
  expression: "0 6 * * *"
prepare:
  before: 3h
  goal: >-
    Collect today's appointments and the RSS digest for the morning briefing.
deliver:
  - channel: voice
    followUp: true
    when:
      - state($person = home)
      - state(binary_sensor.kitchen_occupancy = on)
    expires: "11:00"
    fallback: push
  - channel: inbox
```

## `tasks.create` arguments

No other argument is accepted. Owner, budget and origin satellite cannot be set.

| Key | Type | Default | Description |
|---|---|---|---|
| `goal` | string | required | What to achieve, with every detail a later run needs. |
| `trigger` | object | `{ kind: now }` | When the task runs. See below. |
| `name` | string | none | Unique per owner. Lower-cased; lower-case letters, digits, `-` and `_`, at most 64 characters, not a GUID. |
| `template` | string | `default` | [Agent template](templates-format.md) of the runs. |
| `notes` | string | none | Notes passed to runs. |
| `prepare` | object | none | A preparation run before each scheduled run, `cron` triggers only. See [Preparation](#preparation). `tasks.update` accepts it too; `null` removes it. |
| `deliver` | list of deliveries | `[]` | Where the result goes. With none, the result is visible in the web UI only. |
| `overrideQuietHoursUntil` | local time | none | Lets the task notify during quiet hours until then. Makes the call `confirm`. |

## Triggers

| `kind` | Extra keys | Meaning |
|---|---|---|
| `now` | none | Run as soon as a worker is free. |
| `at` | `time` | Run once at an ISO 8601 time. A time without offset is in the owner's time zone, for example `2026-09-30T07:00`. |
| `cron` | `expression` | Five-field cron expression, evaluated in the owner's time zone (DST-aware). The task waits for the next occurrence after each run. |
| `after` | `task` | Run when the named task (id or name) finishes `done`. The task must exist. |

Event triggers exist only through `tasks.watch`.

## Preparation

A `cron` task can collect slow or bulky input ahead of time. `before` each scheduled run, a preparation run works on the preparation `goal` with the task's template, tool policy and budget. Its result is stored as prepared notes, and the next main run gets them in its input together with the time they were collected.

| Key | Type | Description |
|---|---|---|
| `prepare.before` | duration | How long before each scheduled run: `3h`, `90m`, `PT3H` or `03:00:00`. More than zero, at most 12 hours. |
| `prepare.goal` | string | What the preparation run collects. |

- The main run starts with the taint of the preparation run, so prepared notes from mail or RSS keep the main run tainted.
- Notes older than `before` plus one hour at the main run are dropped. Notes are discarded after the main run uses them.
- A failed or skipped preparation does not block the main run; it runs without notes and can still call tools.
- Preparation spend counts against the task's budget but not as a run.

## Example: watch (`tasks.watch`)

```yaml
name: media-available
filter:
  source: seerr
  type: media.available
goal: >-
  A title requested in Seerr has become available. Tell me in one short sentence which title is ready to watch.
deliver:
  - channel: push
```

### `tasks.watch` arguments

| Key | Type | Default | Description |
|---|---|---|---|
| `filter` | object | required | Event filter. Must contain `source`. |
| `goal` | string | required | What to do when the event matches. |
| `name` | string | none | As in `tasks.create`. |
| `once` | boolean | `false` | Fire on the first matching event only, then end `done`. Any non-boolean value is a validation error. |
| `deliver` | list | `[]` | As in `tasks.create`. |
| `overrideQuietHoursUntil` | local time | none | As in `tasks.create`. |

### Event filter

The filter is matched deterministically, without a model call. A watch fires once per arming.

| Key | Type | Description |
|---|---|---|
| `source` | string | Required. Event source, matched case-insensitively (`ha`, `printer`, `mail`, `uptime-kuma`, `seerr`, ...). |
| `type` | string glob | Optional event type, for example `state_changed`, `complete`, `monitor.down`. |
| `entity` | string glob | Optional entity, for example `person.lukas`. |
| any other key | text, number, boolean, `null`, or a list of those | Payload condition. A dotted key (`attributes.battery`) walks into the payload. Text is a case-insensitive glob (`*` matches anything). Numbers also match numeric strings. `null` matches a missing field. A list matches any of its values. |

Event payloads are untrusted by default: a run started by an event gets the payload wrapped as untrusted data and is tainted with source `event`. Only sources that declare it in code are trusted (webhook sources such as `uptime-kuma` and `seerr`). Configuration and payload content cannot change that.

## Deliveries

Each delivery goes out independently.

| Key | Type | Default | Description |
|---|---|---|---|
| `channel` | `voice`, `push`, `inbox`, `silent` | required | Voice speaks the result's `speech`. Push sends `speech` plus a link. Inbox and the web UI show the `body`. |
| `target` | string | where the user is | Voice only: satellite entity id. |
| `priority` | `normal` or `low` | `normal` | `critical` is never allowed. |
| `followUp` | boolean | `false` | Voice only: the satellite listens for a reply. |
| `when` | list of conditions | none | All must hold. Checked when the result is ready and on every matching state change. |
| `expires` | `HH:mm` or local date-time | none | Deadline in the owner's time zone. `HH:mm` is the next such time. Needs `fallback`. |
| `fallback` | `voice`, `push`, `inbox`, `silent` | none | Channel used when `expires` passes first. Needs `expires`. |

### Conditions

`state(<entity_id> <op> <value>)` with `=`, `!=` (text, case-insensitive) or `>`, `<` (numeric). `$person` stands for the owner's person entity. Areas are not entities; use an occupancy sensor. Unknown or unavailable states never satisfy a condition.

### Delivery validation

- `A delivery cannot be critical; only system sources send critical notifications.`
- `Low-priority deliveries never use voice; use inbox or push.` (also for a `low` delivery whose `fallback` is `voice`)
- `followUp needs the voice channel.`
- `Only voice deliveries have a target.`
- `'<text>' is not a condition like state(binary_sensor.kitchen_occupancy = on).`
- `'<entity>' is an area, not an entity; use an occupancy sensor such as binary_sensor.kitchen_occupancy.`
- `'<text>' compares with > or < but '<value>' is not a number.`
- `'<text>' is not a deadline like 11:00 or 2026-09-30T11:00.`
- `A delivery with expires needs a fallback channel.` / `A fallback channel needs expires.`
- `Each delivery needs a channel: voice, push, inbox or silent.`

## Other validation

- `goal is required.`
- `Only scheduled (cron) tasks can have a preparation run.`, `prepare.before must be more than zero and at most 12 hours.`, `prepare.before must be a duration like 3h, 90m or PT3H.`, `prepare needs a goal.`
- `trigger.kind must be now, at, cron or after.`, `An 'at' trigger needs a time.`, `A 'cron' trigger needs an expression.`, `An 'after' trigger needs a task.`
- `'<x>' is not a five-field cron expression.`, `'<x>' is not a time like 2026-09-30T07:00.`
- `An event filter is a JSON object with a source, optional type and entity, and payload conditions ...`
- `'<name>' is not a valid task name ...` and a duplicate name (names are unique per owner).
- `Unknown argument '<x>'.`
- Nesting limit `tasks.maxDepth` (3) and unfinished children `tasks.maxActiveChildren` (5). The minimum wakeup interval is `tasks.minWakeupInterval` (5 minutes) or the template's `minWakeupInterval`.

The task tools are `safe` but persist work: in a tainted run `tasks.create`, `tasks.watch` and `tasks.update` become `confirm`.

## Related

- [Scheduled tasks](../guides/tasks/scheduled-tasks.md)
- [Event triggers](../guides/tasks/event-triggers.md)
- [Daily briefing](../guides/tasks/daily-briefing.md)
- [Events reference](../reference/events.md)
- [Tasks and scheduling](../concepts/tasks-and-scheduling.md)
