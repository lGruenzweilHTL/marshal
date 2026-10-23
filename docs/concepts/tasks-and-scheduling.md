---
icon: fa-solid fa-calendar-days
description: "How Marshal does work when nobody is talking to it: durable tasks, triggers, the worker pool, approvals, budgets and how results reach you."
---

# Tasks and scheduling

This page explains how Marshal does work when nobody is talking to it: durable tasks, triggers, the worker pool, approvals, budgets and how results reach you.

## The problem

A useful assistant acts on its own: a morning briefing at 06:00, a reminder when you reach the hardware store, a notice when a print finishes, a follow-up on a long request. Doing this with an LLM raises hard questions:

- What happens to a scheduled job when the container restarts?
- How do you stop a model from scheduling itself every minute, spawning child jobs without end, or spending your budget overnight?
- Who confirms a risky action at 03:00?
- How does the result reach you: spoken in the kitchen, as a push, or quietly in an inbox?

## Durable tasks

A task is a row in SQLite, so it survives restarts. It holds its owner, parent, who created it (`user`, `task` or `system`), kind (`agent` or `system`), goal, template, optional project, optional origin satellite, trigger, status, next run time, notes, budget and spend, deliveries, an optional quiet-hours override, an optional name (unique per owner), its latest result and its depth.

Status moves through `pending`, `running`, `waiting`, `done`, `failed` and `cancelled`.

**System tasks** (kind `system`) run registered C# jobs without an agent loop and without model tokens: the nightly backup and memory export, memory consolidation and the retention purge. See [`maintenance`](../configuration/marshal-yaml/maintenance.md).

## Triggers

| Trigger | Fires |
|---|---|
| `now` | Immediately. |
| `at(time)` | Once at a time. |
| `cron(expression)` | On a schedule. |
| `event(filter, once?)` | When a matching event arrives (a watch). |
| `after(task)` | When another task finishes `done`. |

Cron and `at` times are evaluated in the owner's time zone with DST awareness and stored in UTC, so `cron(0 6 * * *)` runs at 06:00 local time on both sides of a DST change. After a successful run, cron tasks and watches without `once` wait for their next occurrence; once-watches and all other triggers end `done`.

The scheduler is a background service that sleeps until the earliest `next_run_at`.

## Events and deterministic matching

Event sources publish normalized events `{ source, type, entity, payload, ownerId?, time }` on an in-process bus:

- Home Assistant state changes and companion app push actions
- Moonraker print events derived from state transitions (`started`, `paused`, `resumed`, `complete`, `error`, `cancelled`, klippy shutdown, progress milestones, first layer done), never raw temperature diffs
- new mail (IMAP IDLE, with polling fallback)
- executor callbacks
- webhooks from Uptime Kuma, Proxmox and Seerr

Watches use declarative filters (source, type, entity glob, payload conditions such as `to` and `from`). Matching is deterministic and happens before any run starts: an event that matches nothing costs no model call. A watch fires at most once per arming. See [events](../reference/events.md) and [event triggers](../guides/tasks/event-triggers.md).

Event payloads are untrusted unless the source declares itself trusted in code, so most event-started runs are tainted from the start. See [untrusted content](untrusted-content.md).

## The lifecycle of a run

<!--@include: ../_diagrams/task-lifecycle.md-->

Inside a run:

<!--@include: ../_diagrams/task-run.md-->

1. **Due.** A trigger fires and the task becomes due.
2. **Worker.** At most `tasks.workers` runs execute at once (default 2). Further due tasks wait in order of `next_run_at`.
3. **Headless run.** The [agent loop](agent-loop.md) runs as the task's owner, with the task's template, project and origin satellite policy. A satellite's `denyTools` and `denyConfirm` follow its tasks and their child tasks. Background rules apply: the advisor is visible, and confirm-tier calls go to the approval queue.
4. **Approval.** A confirm-tier call freezes the action (tool, arguments, task, owner, nonce, reasons) with a 10-minute TTL and notifies you by actionable push and in the web UI review queue. The run ends with status `waiting`, and its worker becomes free. When you approve (web UI, push action or voice follow-up), the server executes exactly the frozen action and resumes the task with the decision and the result. Rejection and expiry resume it with a rejection. See [approvals](../guides/tasks/approvals.md).
5. **Wakeup.** A run that ends with a new trigger (for example from `tasks.schedule_wakeup`) waits. The next run gets the goal, the task notes and the trigger payload, not the previous transcript. Notes are how a task remembers between runs.
6. **Result and delivery.** The final reply becomes the result: `speech` (short, speakable) and `body` (markdown).

## Delivery

Each task has a list of deliveries, each delivered independently through the notification router:

| Channel | Gets |
|---|---|
| `voice` (a satellite, an area, or "where I am") | `speech`, optionally with a follow-up so you can answer |
| `push` | `speech` plus a deep link to the result |
| `inbox` | the result, for later (for example the next briefing) |
| `silent` | nothing is sent; the result is stored |

A delivery can wait for conditions, such as `state(binary_sensor.kitchen_occupancy = on)`, checked when the result is ready and on every matching state change. If `expires` passes first, it goes out through a fallback channel. Delivery priority is `normal` or `low`; `low` never uses voice, and no task delivery can be `critical`.

The router then applies presence, quiet hours, coalescing and voice caps. See [notifications](../guides/notifications.md).

## Limits and the kill switch

The model can create tasks, so the server enforces limits the model cannot change:

| Limit | Default |
|---|---|
| Per-task budget (tokens, cost, steps, wall time, runs); child tasks draw from the parent's | cost 0.5, 60 steps, 30 minutes, 20 runs |
| Daily cost cap for all background runs of an owner | 2 |
| Recursion depth of child tasks | 3 |
| Unfinished child tasks per task (fan-out) | 5 |
| Minimum wakeup interval and cron period (templates can set their own) | 5 minutes |

A run that exceeds its budget stops, and the task fails with `budget_exceeded`. Costs use the same price table as the cost view. See [`tasks`](../configuration/marshal-yaml/tasks.md).

The **kill switch** cancels all running and pending background tasks of the owner and pauses new task starts until you re-enable them. It is a button in the web UI and the tool `tasks.stop_all` ("stop all background tasks"), and every use is audited.

## Task tools

The model manages tasks with `tasks.create`, `tasks.schedule_wakeup`, `tasks.watch`, `tasks.update`, `tasks.run_now`, `tasks.list`, `tasks.status`, `tasks.cancel`, `tasks.stop_all` and `tasks.run_executor`. The writing tools are flagged `persists`, so they need confirmation in tainted runs. Overriding quiet hours always needs confirmation. `tasks.update` cannot change the owner, budget or origin satellite. `tasks.run_executor` is always confirm tier. See [tools reference](../reference/tools.md).

You can also define tasks as files; see [tasks format](../configuration/tasks-format.md).

## Trade-offs

- **No transcript between runs.** A resumed run sees only goal, notes and payload. This keeps runs small and prevents a tainted transcript from carrying over, but a task must write down in its notes what it needs later.
- **Suspend instead of wait.** Runs end while an approval is pending, so a slow decision never blocks a worker. The task has to pick up again from the decision payload.
- **Conservative limits.** The defaults stop runaway costs early. Long-running research tasks may need a larger budget in their template or task definition.

## Related

::: cards
- [Scheduled tasks](../guides/tasks/scheduled-tasks.md)
- [Daily briefing](../guides/tasks/daily-briefing.md)
- [Risk and confirmation](risk-and-confirmation.md)
- [Follow-ups](../guides/voice/follow-ups.md)
:::
