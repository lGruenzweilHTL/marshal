# task-system Specification

## Purpose
Durable background tasks: triggers, the scheduler and worker pool, wakeups, budgets and limits, the kill switch and the task tools.

## Requirements

### Requirement: Durable tasks
Tasks SHALL be stored in SQLite with owner, parent, `created_by` (`user` | `task` | `system`), kind (`agent` | `system`), goal, template, optional project id, optional origin satellite, trigger, status (`pending`, `running`, `waiting`, `done`, `failed`, `cancelled`), next run time, notes, budget and spend, deliveries, optional quiet-hours override, optional name (unique per owner), result (`speech`, `body`) and depth. Tasks SHALL survive restarts.

#### Scenario: Restart
- **WHEN** Marshal restarts while a task is `waiting` with a future `at` trigger
- **THEN** the task runs at its trigger time after the restart

#### Scenario: Duplicate name
- **WHEN** a second task named `briefing` is created for the same owner
- **THEN** creation fails with a validation error

### Requirement: Triggers and scheduler
Triggers SHALL be `now`, `at(time)`, `cron(expression)`, `event(filter, once?)` and `after(task)`. Cron and `at` times SHALL be evaluated in the owner's time zone (Cronos, DST-aware) and stored in UTC. The scheduler SHALL be a `BackgroundService` using `TimeProvider`, waking for the earliest `next_run_at`. After a run ends `done`, a `cron` task and an `event` task without `once` SHALL wait for their next occurrence; an `event` task with `once` and every other trigger SHALL end `done`.

#### Scenario: Cron in local time
- **WHEN** a task has `cron(0 6 * * *)` and the owner's time zone is `Europe/Vienna`
- **THEN** it runs at 06:00 Vienna time on both sides of a DST change

#### Scenario: After another task
- **WHEN** task B has `after(A)` and A finishes `done`
- **THEN** B becomes due immediately

#### Scenario: Recurring watch re-arms
- **WHEN** a watch without `once` finishes its run `done`
- **THEN** it is `waiting` for the next matching event

#### Scenario: Once-watch ends after its run
- **WHEN** a watch with `once` finishes its run `done`
- **THEN** its status is `done` and no later event makes it due

#### Scenario: Once survives a restart
- **WHEN** Marshal restarts while a once-watch is `waiting`
- **THEN** the watch is still a once-watch and ends `done` after its first run

### Requirement: Headless runs and wakeups
Each run SHALL execute the agent loop headless as the task's owner, with the task's template, project and origin satellite policy, and background tool rules (advisor visible, confirm-tier → approval queue). A run that ends with a new trigger SHALL set status `waiting`; the resumed run SHALL get goal, notes and trigger payload, not the previous transcript.

#### Scenario: Wakeup without transcript
- **WHEN** a task resumes after `tasks.schedule_wakeup`
- **THEN** the model input contains goal, notes and trigger payload, and no messages from the previous run

#### Scenario: Origin satellite policy
- **WHEN** a task created from a satellite with `denyTools: [mail.*]` runs
- **THEN** no `mail_*` tool is visible in any of its runs or its child tasks' runs

### Requirement: Worker pool
At most N task runs (configurable) SHALL execute concurrently; due tasks beyond N SHALL wait in order of `next_run_at`.

#### Scenario: Pool full
- **WHEN** N runs are active and another task becomes due
- **THEN** it starts when a worker becomes free

### Requirement: Budgets and limits
Each task SHALL have a budget (tokens, cost, steps, wall time, runs); child tasks SHALL draw from the parent's budget. A global daily cost cap SHALL stop new runs when reached. Recursion depth (default 3), fan-out per task and minimum wakeup interval (default 5 minutes, per template) SHALL be enforced server-side. Costs SHALL be computed with the same price table as the cost view.

#### Scenario: Budget exhausted
- **WHEN** a run exceeds its task's cost budget
- **THEN** the run stops, the task is `failed` with reason `budget_exceeded`

#### Scenario: Wakeup too soon
- **WHEN** a task schedules a wakeup 1 minute ahead with a 5-minute minimum
- **THEN** the call fails with a validation error

### Requirement: Kill switch
The kill switch SHALL cancel all running and pending background tasks of the owner and pause new task starts until re-enabled. It SHALL be available as a web UI button and as the tool `tasks.stop_all` (safe). Its use SHALL be audited.

#### Scenario: Voice kill switch
- **WHEN** the user says "stop all background tasks" and the model calls `tasks_stop_all`
- **THEN** all running tasks are cancelled and an audit event is written

### Requirement: Task tools
The system SHALL provide:
- `tasks.create`, `tasks.schedule_wakeup`, `tasks.watch(eventFilter, goal, once?)` — safe, `persists` (confirm in tainted runs); an `overrideQuietHoursUntil` argument makes the call confirm with reason `quiet_hours_override`. `once` (default `false`) makes the watch fire on its first matching event only.
- `tasks.update(task, goal?, trigger?, deliver?, template?)` — safe, `persists`; cannot change owner, budget or origin satellite; `overrideQuietHoursUntil` → confirm
- `tasks.run_now(task)` — safe; runs an existing task with its normal budget, deliveries and conditions
- `tasks.list`, `tasks.status(task)`, `tasks.cancel(task)` — safe; `task` accepts an id or name; watches show whether they are `once`
- `tasks.stop_all` — safe
- `tasks.run_executor(executor, spec, onDoneGoal)` — confirm

#### Scenario: Status by name
- **WHEN** the model calls `tasks_status` with `briefing`
- **THEN** the owner's task named `briefing` is returned with its latest result and its age

#### Scenario: Reminder after reading mail
- **WHEN** a run that read mail calls `tasks_create`
- **THEN** a pending action is created whose reason names mail as the source of the prompt-injection risk

#### Scenario: One-off location reminder
- **WHEN** the model calls `tasks_watch` with filter `source: ha, entity: person.lukas, to: Hornbach`, goal "remind to buy wall plugs" and `once: true`
- **THEN** a watch is created whose trigger is `event` with `once`, and the task list shows it as a once-watch

#### Scenario: Once is not a boolean
- **WHEN** the model calls `tasks_watch` with `once: "yes"`
- **THEN** the call fails with a validation error and no task is created

### Requirement: System tasks
Tasks of kind `system` SHALL run registered C# jobs without an agent loop and without model tokens.

#### Scenario: No tokens
- **WHEN** a system task runs
- **THEN** its run records no model call and zero cost
