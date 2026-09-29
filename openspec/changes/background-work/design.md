## Context

Sources: brainstorm notes §4 (approval queue, taint, reasons), §6 (auto-background, self-backgrounding), §10 (tasks, deliveries, events, executors, follow-ups), §11 (notifications), §12 (briefing), §13 (semantic events, babysitter), §14 (new-mail events), §21 (retention, backups), §18 views 1, 6, 7, 8. All earlier changes are in place: headless agent loop, guard with pending actions, HA client, Moonraker state cache, IMAP provider, memory summarizer, live-event stream.

## Goals / Non-Goals

**Goals:**
- Durable, bounded, observable background work driven by the agent within server-enforced limits.
- Notifications that reach the user in the right place without noise.
- Background confirmations that never block workers and never let the model alter approved actions.

**Non-Goals:**
- Additional executors, Obico, busy detection, notes proposals.

## Decisions

### D1. Scheduler and worker pool
`BackgroundTaskScheduler : BackgroundService` loads the earliest `next_run_at`, waits with `TimeProvider.CreateTimer` (woken early by a signal when tasks change), and enqueues due tasks into a bounded `Channel<TaskId>`; N `TaskWorker`s consume it. Status transitions use optimistic concurrency (`row_version`) so a task never runs twice. On startup, tasks left `running` by a crash become `pending` with a note.
*Alternatives:* Quartz.NET / Hangfire — rejected as too heavy (notes §10).

### D2. Resume model
A run ends in one of: `done` (result set), `waiting` (new trigger: wakeup, event, approval decision, follow-up reply), `failed`. The resume prompt is built from goal + notes + trigger payload only. Approval suspensions append a server-written note (`approval requested: <tool> <args summary> (<pending action id>)`) so the model knows what it is waiting for.

### D3. EventBus and matcher
`EventBus` = one unbounded `Channel<MarshalEvent>` with a single dispatcher that runs `TriggerMatcher` (filters indexed by source + type) and fans matches to the scheduler and to `ConditionalDeliveryTracker`. Sources are hosted services in the integration projects implementing `IEventSource` and publishing through `IEventPublisher` (in Abstractions), so integrations do not reference `Marshal.Tasks`.

### D4. Deliveries and router
`DeliveryDispatcher` validates deliveries at creation (low + voice, followUp on non-voice, `area.*` conditions, critical) and, when a result is ready, evaluates `when` against the HA state cache; pending conditional deliveries are persisted in the task's delivery JSON with state, re-checked on matching `state_changed` events, and expired by a scheduler timer. `NotificationRouter` applies, in order: priority rules → quiet hours / override → DND → presence-based channel choice → coalescing window → per-source and global voice caps → channel delivery → history record with reason.

### D5. Channels
| Channel | Implementation |
|---|---|
| voice announce | HA `assist_satellite.announce` on the chosen satellite |
| voice conversation | HA `assist_satellite.start_conversation` + follow-up record |
| push | HA `notify.mobile_app_*` from the owner's notification targets, with actions for approvals and deep links |
| inbox | `Notification` rows with outcome `inboxed` |

### D6. Approvals
Background guard path: `Guard` receives `ExecutionMode.Background` and, for confirm, calls `IApprovalQueue.EnqueueAsync` (freeze, 10 min TTL, notify) and returns a `Suspend` decision; the runner ends the run as `waiting`. Decisions arrive from the review queue endpoint, the push action handler (nonce check) or a voice follow-up; `ApprovalExecutor` executes the frozen action under the task owner's execution context and schedules the resume with the payload. An expiry timer marks overdue approvals `expired` and resumes the task.

### D7. Backgrounding interactive turns
The conversation runner holds the run in memory; on cap or `agent.move_to_background`, it creates an `AgentTask` (`created_by = user`, origin satellite, deliveries from the turn's context), transfers the in-flight transcript to this single continuation run (the only case where a transcript continues), writes the final SSE event, and hands the run to a worker. Tool list is recomputed with `ExecutionMode.Background`.

### D8. Executor host
`Marshal.ExecutorHost` is a minimal ASP.NET app with its own image: `POST /sessions` (spec, callback URL, callback token), `DELETE /sessions/{id}`, runs `claude -p --output-format stream-json` in a per-session container via the host's (rootless) Docker, in a git worktree; progress and completion are posted to Marshal's `/api/v1/executor/callbacks/{sessionId}` with the callback token. Marshal's `ClaudeCodeExecutor : IExecutor` talks to it with a scoped token stored as a secret.

### D9. Babysitter
`PrintBabysitter` in `Marshal.Integrations.Moonraker` subscribes to printer events through `IEventSubscriber`, snapshots every N seconds (config), calls `IPrintFailureDetector` (`VlmPrintFailureDetector`: `fast` + `vision`, strict JSON verdict), keeps a two-strike counter, pauses via `IPrinterClient`, and sends through `INotificationSender` with source `system:babysitter`.

### D10. Interfaces introduced
| Interface | Purpose | v1 implementation |
|---|---|---|
| `ITaskStore` | tasks CRUD, due queries | `EfTaskStore` |
| `ITaskScheduler` | scheduling signals | `TaskSignals` (wake signal and stop handles), consumed by `BackgroundTaskScheduler` (not `TaskScheduler`, which clashes with `System.Threading.Tasks.TaskScheduler`). Separate classes because the scheduler depends on the agent loop, whose tool registry holds the task tools that signal it |
| `ITaskRunner` | one headless task run | `AgentTaskRunner`, `SystemTaskRunner` |
| `ISystemJob` | code-only jobs | `BackupJob`, `MemoryExportJob`, `ConsolidationJob`, `RetentionPurgeJob` |
| `IEventPublisher` / `IEventSubscriber` | EventBus | `ChannelEventBus` |
| `IEventSource` | event producers | `HomeAssistantEventSource`, `MoonrakerEventSource`, `ImapNewMailSource`, `ExecutorCallbackSource` |
| `ITriggerMatcher` | filter matching | `DeclarativeTriggerMatcher` |
| `IDeliveryDispatcher` | deliveries + conditions | `DeliveryDispatcher` |
| `IConditionEvaluator` | `state(...)` conditions | `HaStateConditionEvaluator` |
| `INotificationSender` | send with priority | `NotificationRouter` |
| `INotificationChannel` | deliver on one channel | `VoiceAnnounceChannel`, `VoiceConversationChannel`, `PushChannel`, `InboxChannel` |
| `IPresenceProvider` | home / room presence | `HaPresenceProvider` |
| `IApprovalQueue` | background confirmations | `ApprovalQueue` |
| `IFollowUpStore` | follow-up records | `EfFollowUpStore` |
| `IExecutor` | external long work | `ClaudeCodeExecutor` |
| `IPrintFailureDetector` | failure verdicts | `VlmPrintFailureDetector` |
| `IProposalStore` | coding proposals | `EfProposalStore` |

### D11. Model-facing tools
| Tool | Tier | Flags | Hidden when |
|---|---|---|---|
| `tasks.create`, `tasks.schedule_wakeup`, `tasks.watch` | safe; `overrideQuietHoursUntil` → confirm | `persists` (confirm in tainted runs) | kill switch active; depth / fan-out limit reached |
| `tasks.update` | safe; `overrideQuietHoursUntil` → confirm | `persists` | no tasks exist |
| `tasks.run_now` | safe | — | kill switch active |
| `tasks.list`, `tasks.status`, `tasks.cancel`, `tasks.stop_all` | safe | — | never (subject to allowlists) |
| `tasks.run_executor` | confirm | — | no executor configured; `denyConfirm` |
| `notifications.notify` | safe (priority `normal` / `low` only) | — | never |
| `notifications.inbox_list`, `notifications.inbox_consume` | safe | `untrustedOutput` (inbox items may carry third-party text) | never |
| `agent.move_to_background` | safe | — | background runs; subagents |

`agent.ask_advisor` becomes visible in background runs (rule from `core-agent`).

## Verification (task 1.3, 2026-09-29)

Checked against the HA core source (`dev` branch: `assist_satellite/services.yaml`, `assist_satellite/entity.py`, `assist_pipeline`, `conversation/models.py`) and the companion app docs (notifications basic and actionable). No HA container was run.

- **`assist_satellite.announce`**: target entities of domain `assist_satellite` with the `ANNOUNCE` feature; fields `message` (text, default empty), `media_id`, `preannounce` (default true), `preannounce_media_id`. The call blocks until playback is done. Marshal sends `message` only (TTS by the satellite's pipeline).
- **`assist_satellite.start_conversation`**: target entities with the `START_CONVERSATION` feature; fields `start_message`, `start_media_id`, `extra_system_prompt`, `preannounce`, `preannounce_media_id`. It fails when the satellite's pipeline uses the built-in HA agent (not relevant: Marshal's pipelines use the Marshal agent) and raises `SatelliteBusyError` while the satellite is announcing. The satellite keeps `extra_system_prompt` (or `start_message` when none is given) and consumes it in the **first** pipeline run after the prompt; the pipeline passes it as `ConversationInput.extra_system_prompt`, which custom conversation entities receive. So the `marshal-followup:<nonce>` marker works (as already found in `ha-conversation-integration`); the reply-window fallback stays for satellites or HA versions where it does not arrive. The `start_message` is also added to HA's own chat log as assistant content, which Marshal ignores.
- **`assist_satellite.ask_question`** exists (question, `answers` with sentence matching, returns a response). Not used: its answer matching runs in HA, and Marshal needs the reply as a normal turn.
- **Actionable push**: `notify.mobile_app_<device>` with `data.actions: [{action, title, uri?, behavior?, authenticationRequired?}]`. Android shows at most 3 actions and needs `action: URI` to use `uri`; iOS allows about 10. A pressed action fires the `mobile_app_notification_action` event with `action` (the identifier), `reply_text` (only for `REPLY` / `behavior: textInput`), `action_data` (iOS) and, on Android, the notification's data fields such as `tag`. The event is fired by the companion app through its webhook, so it arrives on HA's event bus and Marshal receives it through its websocket subscription. Marshal encodes the decision and the nonce in the action identifier (`MARSHAL_APPROVE_<nonce>`, `MARSHAL_REJECT_<nonce>`), because `action` is the only field both platforms return, and verifies the nonce server-side; the event itself is not authenticated beyond HA's own access, so the nonce is the proof.
- **Deep links**: tap target is `data.url` (iOS) and `data.clickAction` (Android); both accept relative paths, so Marshal sends both with the result URL. `data.tag` replaces a notification with the same tag; `message: clear_notification` with the tag clears it (used after an approval is decided elsewhere).
- **Priority mapping**: silent push = iOS `push.interruption-level: passive`, Android `importance: low`; `critical` = iOS `push.interruption-level: critical`, Android `channel: alarm` with `priority: high` and `ttl: 0`.

## Implementation notes

- **Run outcomes and re-arming.** A run ends `done`, `waiting` or `failed`. `done` on a `cron` task re-arms it (`waiting`, next occurrence in the owner's time zone); on an `event` task (a watch) it re-arms it without a run time (the matcher makes it due); on `now`, `at` and `after` tasks it is final. `waiting` keeps the task's trigger and sets only `next_run_at` (wakeup) or leaves it empty (approval decision, follow-up reply), so a cron task that waits for an approval keeps its schedule. `failed` is final. An `after(A)` task becomes due when a run of A ends `done`.
- **Claiming.** The worker moves a due task to `running` with the row version it read; a second claim fails with `tasks.conflict` and is dropped. The outcome is applied to a fresh copy (tools may have changed notes during the run); a task cancelled during its run stays cancelled.
- **Cross-owner reads.** The scheduler's due queries (`GetDueAsync`, `GetNextRunAtAsync`, `GetRunningAsync`) are the only task queries that bypass the owner filter; they return ids and owners only, and each task is then loaded and run as its owner.
- **Results.** The final reply of a task run is its result: the first paragraph (up to the first blank line) is `speech`, the whole reply is `body`. The task prompt rules (`TaskPromptContributor`) ask for exactly that shape, so no extra tool is needed.
- **Trigger payloads.** The resumed run sees goal, notes and payload in its user message. Event payloads can carry third-party text (mail subjects, HA attribute values), so they are wrapped as untrusted data and taint the run with kind `event`; payloads written by the server (wakeups, approval decisions) are not.
- **Budgets.** A budget covers one activation of a task: from its trigger through its wakeups until it is done. Recurring tasks (cron, event) start each activation with zero spend, so a daily briefing does not use up a lifetime budget. Unset limits fall back to `tasks.defaultBudget`. A run gets the minimum of what is left of its task's and every ancestor's budget, and at most what is left of the daily cost cap, as `RunLimits` (cost, tokens, model calls, wall time); the agent loop stops with `budget_exceeded` when one is reached (usage counted on the run context, so subagent and advisor calls count). After the run, its spend is added to the task and every ancestor. Over budget before a run: the task fails with `budget_exceeded`. Daily cap reached: the task waits until the owner's next local day (`daily_cap`). The daily cap is per owner (like the advisor's), over all their background runs.
- **Depth, fan-out, intervals.** A child's depth is its parent's + 1 and may not exceed `tasks.maxDepth` (3); a task may have at most `tasks.maxActiveChildren` (5) unfinished children; a cron schedule may not run more often than the minimum wakeup interval (checked over its next occurrences). The minimum wakeup interval is `tasks.minWakeupInterval` (5 min) or the template's `minWakeupInterval`. A child task keeps its parent's origin satellite.
- **Task tools.** Times without an offset are the owner's local time. Inside a task run, `tasks.create` / `tasks.watch` create child tasks (origin satellite and project carry over); `tasks.schedule_wakeup` exists only inside task runs and ends the run `waiting`. Tools accept only their declared arguments, so owner, budget and origin satellite can never be passed. `tasks.cancel` also cancels unfinished descendants. `tasks.run_executor` comes with the executors (task 7.4).
- **Event filters.** A watch's filter is a JSON object: `source` (required), optional `type` and `entity` globs (`*` matches any text), and every other key a payload condition on a dotted path (`to`, `attributes.battery`) whose value is a string glob, a number (also matching numeric strings, because HA states are strings), a boolean, null (field missing) or a list of those (any of them). Strings compare case-insensitively. The filter is validated when the task is created. Example: `{"source": "ha", "entity": "person.alice", "to": "home"}`.
- **Matching.** The bus dispatcher hands each event to the matcher, then to subscribers. The matcher reads the armed watches (`event` tasks that are pending or waiting and not already due) per event, a small indexed query, so there is no cache to keep in step with task changes. A match sets the task `pending`, due now, with the normalized event (`{source, type, entity, time, payload}`) as trigger payload; a watch that is already due or running does not fire again (once per arming). An event with an owner (mail) reaches only that owner's watches; instance-wide events (HA, printers) reach every owner's.
- **HA events.** `state_changed` is published only when the state itself changes (not for attribute-only updates such as a person's GPS position), with `from`, `to` and the new attributes; `mobile_app_notification_action` carries the event data. Both come from the cache's existing websocket connection.
- **Printer events.** Source `printer`, entity = printer name, types `started`, `paused`, `resumed`, `complete`, `error`, `cancelled`, `klippy_shutdown`, `progress` (payload `milestone` 25 / 50 / 75) and `first_layer_done` (only when the slicer reports layers). They are derived from consecutive cache states; the last known state survives a disconnect, so a transition missed while offline is published after the reconnect. The fixture sequences are transcribed from the Moonraker docs (no live printer available).
- **New-mail events.** One IMAP connection per account watches the inbox (read-only, so nothing is marked seen) with IDLE, renewed every 9 minutes, or polls every 2 minutes when the server has no IDLE. Mail already there when the watch starts produces no events; mail that arrived while disconnected does after the reconnect (UIDs above the last seen one, same UIDVALIDITY). Events: source `mail`, type `new_mail`, entity = account alias, owner = account owner, payload `{account, id, from, subject, date}`. The account list is re-read every 5 minutes.
- **Delivery conditions.** `state(<entity_id> <op> <value>)` with `=` / `!=` (case-insensitive text) and `>` / `<` (numeric, checked when the task is saved); `unknown` and `unavailable` states never satisfy a condition. `expires` is `HH:mm` (the next such local time after the result is ready) or a local date and time, and needs a fallback channel (and vice versa).
- **Scheduler signals.** `ITaskScheduler` (wake, cancel a run) is `TaskSignals`, separate from `BackgroundTaskScheduler`: the scheduler depends on the agent loop, whose tool registry holds the task tools, which signal the scheduler.
- **SQLite concurrency.** Workers, web requests and background services write concurrently. Every connection sets `busy_timeout = 5000` (EF Core already creates the database in WAL mode), and the pre-migration copy runs a `wal_checkpoint(TRUNCATE)` first so the copy holds every committed row.

## Risks / Trade-offs

- [Runaway agent loops in the background] → budgets, depth, fan-out, minimum wakeup interval, global daily cap, kill switch; all server-side.
- [Noise from proactive voice] → caps, coalescing, quiet hours, DND, presence; history explains every decision.
- [Approval fatigue] → reasons shown verbatim; taint escalation limited to `persists` tools.
- [HA `extra_system_prompt` not reaching custom agents] → reply-window fallback.
- [Continuation transcript on auto-background is an exception to "no transcript on resume"] → only for the single continuation run of an interrupted turn; later wakeups follow the normal rule.
- [Executor sandbox escapes] → separate host, rootless runtime, network allowlist, per-task tokens.

## Migration Plan

EF migration adds `AgentTask`, `ExecutorSession`, `Notification`, `Proposal`, `FollowUp` and task-related columns on `PendingAction`. System tasks are seeded on startup (idempotent by name). The executor host is optional and deployed separately.

## Open Questions

- Whether HA timer fallback (tasks + announce) is needed depends on `extensibility` verification.
- Default noise-control numbers — start with the notes' values, tune with real use.
