## Why

Until now Marshal only acts while someone waits for a reply. Much of its value is work that continues after the user walks away: long requests that exceed the voice latency cap, reminders, watches, morning briefings, delegated coding sessions, print babysitting, and proactive notifications delivered in the right room at the right time. This change (sub-project 7 of 7) adds the durable task system and everything that depends on it.

## What Changes

- New project `Marshal.Tasks`: durable tasks (SQLite), triggers (`now`, `at`, `cron`, `event`, `after`), scheduler on `TimeProvider` + Cronos, worker pool of headless agent loops, wakeups that resume with goal + notes + trigger payload, named tasks, budgets, recursion / fan-out / wakeup limits, global daily cost cap, kill switch.
- Task tools: `tasks.create`, `tasks.schedule_wakeup`, `tasks.watch`, `tasks.update`, `tasks.run_now`, `tasks.list`, `tasks.status`, `tasks.cancel`, `tasks.stop_all`, `tasks.run_executor`.
- EventBus (`Channel<T>`) with normalized events and a deterministic trigger matcher; sources: HA state changes, Moonraker semantic print events, new mail (IMAP IDLE / polling), executor callbacks, HA companion push actions.
- Task results (`speech` + `body`) and deliveries: several per task, `when` state conditions, `expires` + fallback, validation (`low` never voice), owner time zone.
- NotificationRouter: priorities (`critical` only from system sources), channels (voice announce, voice conversation, push, inbox), presence-based channel choice, quiet hours with confirm-tier override, coalescing, per-source and global voice caps, voice DND, inbox tools, notification history, actionable push.
- Approval queue: confirm-tier actions in background runs are frozen (10-minute TTL), the run suspends, the server executes on approve, the task resumes with the decision and result.
- Conversation path: auto-background at the latency cap and `agent.move_to_background`, with tool list recomputed for background rules and origin satellite policy kept.
- Voice follow-ups via `assist_satellite.start_conversation` and `followupRef`: pending action, waiting task, or seeded session for a delivered result.
- Executors: `IExecutor`, separate `Marshal.ExecutorHost` service, first executor Claude Code headless in a Docker sandbox, per-task callback tokens, coding results in the review queue (merge in the UI only).
- Print babysitter as a system component with `IPrintFailureDetector` (VLM, two-strike rule), auto-pause and `critical` notification.
- System tasks: nightly backup (`VACUUM INTO` + `/data/keys`), NDJSON memory export, nightly memory consolidation, retention purge (30 days payloads, 1 year metadata).
- Daily briefing shipped as files only (template, skill, example task definition).
- Web UI: tasks, review queue (approvals, coding results), notification history, dashboard widgets (running tasks, next runs, pending approvals, kill switch).

## Non-goals

- No agent-created `critical` notifications.
- No notes proposals (open design topic).
- No executors other than Claude Code (cloud sessions, Codex, aider come later behind `IExecutor`).
- No Obico detector (later, behind `IPrintFailureDetector`).
- No busy detection (media playing / calls) in v1 of the router beyond an optional hook.
- No Marshal-side Docker socket access, ever.

## Capabilities

### New Capabilities
- `task-system`: task model, triggers, scheduler, worker pool, wakeups, budgets and limits, kill switch, task tools.
- `event-bus`: event model, sources, trigger matcher, watches.
- `task-delivery`: results, deliveries, `when` conditions, expiry and fallback, validation.
- `notifications`: NotificationRouter, priorities, channels, presence, quiet hours and override, noise control, DND, inbox, history, actionable push.
- `approval-queue`: background confirmations, suspension and resume, approval channels.
- `voice-followup`: follow-up records, `followupRef` handling, the three follow-up targets.
- `executors`: executor interface, executor host, Claude Code executor, callback tokens, coding results.
- `print-babysitter`: semantic print events consumer, failure detector, auto-pause, critical alerts.
- `system-tasks`: backups, exports, consolidation, retention purge.
- `daily-briefing`: briefing as template + skill + task, no briefing code.
- `background-views`: tasks view, review queue, notification history, dashboard widgets.

### Modified Capabilities
- `conversation-api` (introduced by `core-agent`): adds auto-background at the latency cap, `agent.move_to_background`, and `followupRef` handling.

## Impact

- New projects: `src/Marshal.Tasks`, `src/Marshal.ExecutorHost` (own deployment), test projects.
- New entities: `AgentTask`, `ExecutorSession`, `Notification`, `Proposal`, `FollowUp` (in `erd.puml`); `PendingAction` gains approval-queue use.
- New dependency: `Cronos`.
- Changes to `Marshal.Integrations.*`: event sources for HA, Moonraker, mail.
- Depends on all previous changes.
