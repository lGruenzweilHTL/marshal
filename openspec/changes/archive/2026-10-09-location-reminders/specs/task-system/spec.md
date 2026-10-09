## MODIFIED Requirements

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
