---
icon: fa-solid fa-bolt
description: "Run a task when something happens: a person arrives, a print finishes, a monitor goes down."
---

# Event triggers

Run a task when something happens: a person arrives, a print finishes, a monitor goes down.

## Before you start

- The event source is set up: Home Assistant (`ha`), a printer (`printer`), mail, or a webhook source such as `uptime-kuma`, `proxmox` or `seerr` (see [Uptime Kuma](../integrations/uptime-kuma.md) and [`webhooks`](../../configuration/marshal-yaml/webhooks.md)).
- Reference of events and fields: [events](../../reference/events.md).

## Steps

::: steps
1. **Create a watch**

   Ask Marshal, or pass the arguments of `tasks.watch`:

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

   A watch is a task with an `event` trigger. After each run it re-arms and waits for the next matching event. Add `once: true` for a one-off reminder: it fires on the first match and ends `done`.

   ```text
   Remind me to buy wall plugs when I get to Hornbach.
   ```

   For a place, the agent looks it up with `places_find` and filters on your person entity with `to` (arriving) or `from` (leaving).

2. **Write the filter**

   The filter is a JSON object. `source` is required.

   | Key | Meaning |
   |---|---|
   | `source` | `ha`, `printer`, `mail`, `uptime-kuma`, `proxmox`, `seerr`, ... |
   | `type` | Optional glob on the event type, for example `state_changed`, `printer.complete`, `monitor.down`. |
   | `entity` | Optional glob on the entity, for example `person.lukas` or `binary_sensor.*`. |
   | any other key | A condition on a payload field. A dotted path reaches nested fields (`attributes.battery`). |

   A condition value is a string glob (`*` matches any text, case-insensitive), a number, `true` / `false`, `null` (field missing) or a list of those (any one matches). Numbers also match numeric strings, because Home Assistant states are strings.

   | Goal | Filter |
   |---|---|
   | You arrive home | `source: ha`, `entity: person.lukas`, `to: home` |
   | You leave work | `source: ha`, `entity: person.lukas`, `from: Work` |
   | NAS monitor down | `source: uptime-kuma`, `type: monitor.down`, `entity: NAS` |
   | Battery below a value | `source: ha`, `entity: sensor.door_battery`, then the matching payload condition |

   An invalid filter fails with: `An event filter is a JSON object with a source, optional type and entity, and payload conditions (text, number, true/false or a list of them).`

3. **Know how matching works**

   Matching is deterministic and runs before any task starts; an event that matches no filter costs no model call. A watch fires at most once per arming. The matching event becomes the trigger payload of the run.
:::

## Trust and tainting

Event payloads are untrusted by default. A run started by an event gets the payload wrapped as untrusted data and is tainted with source `event`: memory and task writes in that run need your confirmation. Only sources that declare themselves trusted in code (the webhook sources `uptime-kuma`, `proxmox` and `seerr`) pass the payload unwrapped and leave the run untainted. Trust cannot be set from configuration or from the event's content, and a `trusted: true` field in a payload changes nothing. See [untrusted content](../../concepts/untrusted-content.md).

## Quiet hours

A watch that notifies you at night follows your quiet hours. Overriding them (`overrideQuietHoursUntil`) needs your confirmation.

## Check that it works

Trigger the event, or look at **Tasks**: the watch shows as `watch` (or `once`) with its status. The system page lists event sources, and each webhook source shows the time of its last received webhook.

## Related

::: cards
- [Scheduled tasks](scheduled-tasks.md)
- [Approvals](approvals.md)
- [Events reference](../../reference/events.md)
- [Task definitions](../../configuration/tasks-format.md)
:::
