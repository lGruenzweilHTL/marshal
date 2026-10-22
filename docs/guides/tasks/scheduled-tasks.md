---
icon: fa-solid fa-clock
description: "Create tasks that run now, at a time, on a cron schedule or after another task, and control where their results go."
---

# Scheduled tasks

Create tasks that run now, at a time, on a cron schedule or after another task, and control where their results go.

## Before you start

- A working conversation with Marshal (voice, web chat or the API). Tasks are created by the agent through the `tasks.*` tools; there is no create form in the web UI and Marshal does not read a `/config/tasks` directory (see [task definitions](../../configuration/tasks-format.md)).
- Your time zone is set (`instance.timeZone` or your profile). Cron expressions and `at` times are evaluated in the owner's time zone and survive DST changes.
- For voice or push deliveries: a satellite or the Home Assistant mobile app, see [notifications](../notifications.md).

## Steps

::: steps
1. **Ask for the task**

   Describe the goal and the time. Marshal calls `tasks.create`.

   ```text
   Every weekday at 7:30, check the printer queue and tell me what is waiting. Call the task "queue-check".
   ```

   Or hand it the arguments directly, as in the example files:

   ```yaml
   name: queue-check
   goal: Check the printer queue and report what is waiting.
   trigger:
     kind: cron
     expression: "30 7 * * 1-5"
   deliver:
     - channel: inbox
   ```

2. **Pick a trigger**

   | `trigger.kind` | Extra key | Behavior |
   |---|---|---|
   | `now` (default) | none | Runs as soon as a worker is free. |
   | `at` | `time` | Runs once at a local time such as `2026-09-30T07:00`. Add an offset (`+02:00`, `Z`) to pin an absolute time. |
   | `cron` | `expression` | Five-field cron expression in local time. The task waits for the next occurrence after each run. |
   | `after` | `task` | Runs as soon as the named task (id or name) finishes `done`. |

   A cron schedule may run at most every `tasks.minWakeupInterval` (default 5 minutes). Event triggers are described in [event triggers](event-triggers.md).

3. **Choose deliveries**

   `deliver` is a list; each entry is delivered independently. Without `deliver`, the result stays in the web UI.

   | Key | Description |
   |---|---|
   | `channel` | `voice`, `push`, `inbox` or `silent`. |
   | `target` | Voice only: satellite entity id. Omit it to speak where you are. |
   | `priority` | `normal` (default) or `low`. |
   | `followUp` | Voice only: the satellite listens for a reply. |
   | `when` | List of `state(<entity_id> <op> <value>)` conditions, all must hold. Operators `=`, `!=`, `>`, `<` (numeric). `$person` is your person entity. |
   | `expires` | Local deadline, `11:00` or `2026-09-30T11:00`. Needs `fallback`. |
   | `fallback` | Channel used when `expires` passes first. Needs `expires`. |

   Voice uses the result's `speech`, push uses `speech` plus a deep link, the web UI shows `body`. A conditional delivery goes out when the conditions first hold; if `expires` passes first, the fallback channel delivers instead. Marshal rejects these combinations:

   | Error text | Cause |
   |---|---|
   | `Low-priority deliveries never use voice; use inbox or push.` | `priority: low` with `channel: voice` (or voice fallback). |
   | `followUp needs the voice channel.` | `followUp: true` on another channel. |
   | `Only voice deliveries have a target.` | `target` on a non-voice channel. |
   | `A delivery with expires needs a fallback channel.` / `A fallback channel needs expires.` | One of the pair is missing. |
   | `A delivery cannot be critical; only system sources send critical notifications.` | Priority `critical` is never available to tasks. |

   A condition that names an area (`area.kitchen`) is rejected: conditions take entity ids only. Quiet hours hold normal notifications; see [notifications](../notifications.md).

4. **Wake a task later**

   Inside a run, the agent can call `tasks.schedule_wakeup` (`at` or `inMinutes`, plus `notes`). The run ends with status `waiting`; the next run gets the goal, the notes and the trigger payload, not the previous transcript. The delay must be at least the template's `minWakeupInterval` (default `tasks.minWakeupInterval`): `A wakeup must be at least 5 minutes ahead.`

5. **Manage tasks**

   | Tool | What it does |
   |---|---|
   | `tasks.list`, `tasks.status` | List tasks; show one by id or name with its latest result and age. |
   | `tasks.update` | Change goal, trigger, deliveries, template or name. Owner, budget and origin satellite cannot change. |
   | `tasks.run_now` | Run an existing task now with its normal budget and deliveries. |
   | `tasks.cancel` | Cancel a task and its unfinished child tasks. |
   | `tasks.stop_all` | Kill switch: cancel everything and pause new starts. |

   Task names are lowercase letters, digits, `-` and `_`, at most 64 characters, unique per owner (`A task named 'briefing' already exists.`).
:::

## Prepare ahead

A scheduled task can do its slow reading before it is due. Ask for it when you create the task, for example "prepare the calendar and news three hours before". Marshal passes `prepare: { before: 3h, goal: ... }` to `tasks.create` or `tasks.update`.

The task detail in the web UI shows the preparation goal, the lead time, the next preparation run and whether prepared notes are waiting. Keys, limits and what happens with stale notes: [Preparation](../../configuration/tasks-format.md#preparation).

## Tool policy

Each task may use only the tools in its tool policy. When you create a task from a conversation, the policy is the template's tools in the core namespaces plus the namespaces that conversation loaded. Ask for fewer tools to narrow it, for example "only use the printer status and notifications"; Marshal then passes `tools: [printers.status, notifications.notify]`. The confirmation text lists the policy.

To change the policy, open the task in the web UI (**Tasks**, then the task), select **Edit tool policy**, tick the namespaces the task may use and select **Save policy**. The model cannot change a policy with `tasks.update`.

A run step that the policy blocked shows the error `tool.outside_policy` with a **Widen tool policy** link to the task. Background on why policies exist: [untrusted content](../../concepts/untrusted-content.md#task-tool-policies).

## Budgets and limits

Each task has a budget for one activation (tokens, cost, steps, wall time, runs), and child tasks draw from their parent. Defaults and the daily cost cap are in [`tasks`](../../configuration/marshal-yaml/tasks.md). A task that exceeds its budget ends `failed` with `The task's budget (or its parent's) is used up.` When the daily cap is reached no new run starts: `Today's cost cap for background tasks is reached.`

Creating or changing a task from a run that has read untrusted content (mail, RSS, web pages) needs your confirmation, see [approvals](approvals.md).

## Check that it works

Open **Tasks** in the web UI: the task shows its trigger, next run time, spend against budget, deliveries and latest result. Use `tasks.run_now` ("run queue-check now") to test a delivery without waiting.

## Stop everything

Press **Stop all** on the Tasks page, or say "stop all background tasks". Running tasks are cancelled and nothing starts until you press **Resume**. The action is audited.

## Related

::: cards
- [Event triggers](event-triggers.md)
- [Daily briefing](daily-briefing.md)
- [Approvals](approvals.md)
- [Task definitions](../../configuration/tasks-format.md)
- [`tasks` settings](../../configuration/marshal-yaml/tasks.md)
- [Tasks and scheduling](../../concepts/tasks-and-scheduling.md)
:::
