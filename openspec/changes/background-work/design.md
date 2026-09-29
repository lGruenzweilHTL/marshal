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
`TaskScheduler : BackgroundService` loads the earliest `next_run_at`, waits with `TimeProvider.CreateTimer` (woken early by a signal when tasks change), and enqueues due tasks into a bounded `Channel<TaskId>`; N `TaskWorker`s consume it. Status transitions use optimistic concurrency (`row_version`) so a task never runs twice. On startup, tasks left `running` by a crash become `pending` with a note.
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
| `ITaskScheduler` | scheduling signals | `TaskScheduler` |
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
