## 1. Setup

- [x] 1.1 Create `Marshal.Tasks`, `Marshal.ExecutorHost` and their test projects; update `packages.puml`, `CLAUDE.md` repository map and package index (`Cronos`)
- [x] 1.2 Add entities `AgentTask`, `ExecutorSession`, `Notification`, `Proposal`, `FollowUp` and migration; align `erd.puml` and re-render
- [x] 1.3 Verify HA `assist_satellite.announce` / `start_conversation` (incl. `extra_system_prompt`), companion app actionable notifications and `mobile_app_notification_action`; record in `design.md`

## 2. Task system

- [x] 2.1 Write task store tests: validation, unique names per owner, ownership, optimistic concurrency
- [x] 2.2 Implement `EfTaskStore`
- [x] 2.3 Write scheduler tests with `FakeTimeProvider`: `at`, `cron` in owner time zone across DST, `after`, restart recovery of `running` tasks, no double runs
- [x] 2.4 Implement `TaskScheduler` and worker pool
- [x] 2.5 Write runner tests: headless run as owner, template / project / origin satellite policy applied (incl. child tasks), background visibility rules, resume with goal + notes + payload only
- [x] 2.6 Implement `AgentTaskRunner`
- [x] 2.7 Write limit tests: budgets incl. parent draw-down, daily cap, depth, fan-out, minimum wakeup interval
- [x] 2.8 Implement budget and limit enforcement
- [x] 2.9 Write task tool tests: each tool's tier, `persists` escalation with taint reason, `overrideQuietHoursUntil` → confirm, `tasks.update` restrictions, `run_now`, status / cancel by name, `stop_all` audited
- [x] 2.10 Implement task tools and kill switch

## 3. EventBus

- [x] 3.1 Write EventBus and matcher tests: normalization, filter matching without model calls, watches firing with payload
- [x] 3.2 Implement `ChannelEventBus`, `DeclarativeTriggerMatcher`, `IEventPublisher` in Abstractions
- [x] 3.3 Write HA event source tests with a fake websocket: state changes, push action events
- [x] 3.4 Implement `HomeAssistantEventSource`
- [x] 3.5 Write Moonraker semantic event tests with recorded fixtures: transitions, milestones, first layer, no temperature events, missed transition after reconnect
- [x] 3.6 Implement `MoonrakerEventSource`
- [x] 3.7 Write IMAP new-mail tests against the container: IDLE, polling fallback
- [x] 3.8 Implement `ImapNewMailSource`

## 4. Deliveries and notifications

- [x] 4.1 Write delivery validation tests: low + voice rejected, followUp on non-voice rejected, `area.*` rejected, critical rejected
- [x] 4.2 Write conditional delivery tests: immediate, on state change, all conditions required, `$person`, `zone.home` numeric, expiry fallback in owner time zone
- [x] 4.3 Implement `DeliveryDispatcher` and `HaStateConditionEvaluator`
- [x] 4.4 Write router tests: priorities, critical only from system sources, presence-based channel choice, quiet hours hold / silent push, override, DND, coalescing, per-source and global voice caps, history reasons
- [x] 4.5 Implement `NotificationRouter`, `HaPresenceProvider` and channels (announce, conversation, push, inbox)
- [x] 4.6 Write inbox tool tests and implement `notifications.notify`, `inbox_list`, `inbox_consume`
- [x] 4.7 Publish `notifications` and `tasks` live-event topics

## 5. Approval queue

- [x] 5.1 Write approval tests: background confirm freezes with 10-minute TTL, run suspends and frees worker, automatic note, server executes frozen action on approve, resume payloads for approve / reject / expire, push nonce verification, reasons shown verbatim
- [x] 5.2 Implement `ApprovalQueue`, background guard path and expiry handling
- [ ] 5.3 Implement review-queue approval endpoints and push action handling

## 6. Conversation path

- [ ] 6.1 Write tests: auto-background at the cap creates a task with origin satellite and continues the transcript once, outcome `backgrounded`, result delivered; `agent.move_to_background` visible only in interactive turns; satellite policy kept
- [ ] 6.2 Implement auto-background and `agent.move_to_background`
- [ ] 6.3 Write follow-up tests: record creation with `extra_system_prompt`, valid / expired / consumed / wrong-satellite refs, pending action via keyword, waiting task resume, seeded session for delivered result, silence expiry, reply-window fallback
- [ ] 6.4 Implement `EfFollowUpStore` and `followupRef` handling

## 7. Executors

- [ ] 7.1 Write executor host tests: session lifecycle, callback posting with token, worktree per session (Docker runtime faked)
- [ ] 7.2 Implement `Marshal.ExecutorHost` and its Dockerfile
- [ ] 7.3 Write `ClaudeCodeExecutor` tests: start, callback token issue / revoke, wrong-task and reused-token rejection, completion event
- [ ] 7.4 Implement `ClaudeCodeExecutor`, callback endpoint and `Proposal` creation
- [ ] 7.5 Add an architecture test asserting `Marshal.Api` references no Docker client

## 8. Print babysitter

- [ ] 8.1 Write babysitter tests: starts / stops on events, two-strike rule, direct pause, critical notification with snapshot, status exposed
- [ ] 8.2 Implement `PrintBabysitter` and `VlmPrintFailureDetector`; add babysitter status to `printer.status` and the printer page

## 9. System tasks

- [ ] 9.1 Write tests: backup with `VACUUM INTO` + keys + N copies, NDJSON export per owner, consolidation input without tool results, retention purge boundaries
- [ ] 9.2 Implement `BackupJob`, `MemoryExportJob`, `ConsolidationJob`, `RetentionPurgeJob` and seed their system tasks

## 10. Briefing (files only)

- [ ] 10.1 Add `deploy/config/templates/briefing.md`, `deploy/config/skills/briefing-format/SKILL.md` and an example briefing task definition
- [ ] 10.2 Write an end-to-end test with fake clients: cron prepares the briefing, kitchen occupancy delivers voice with follow-up, "tell me more" runs in the seeded session, expiry falls back to push
- [ ] 10.3 Add an architecture test asserting no briefing-specific types exist in `src/`

## 11. Web UI

- [ ] 11.1 Write component tests for tasks list / detail, goal edit, cancel, kill switch
- [ ] 11.2 Implement tasks views
- [ ] 11.3 Write component tests for review queue (approvals with reasons and taint link, coding proposals with diff and merge)
- [ ] 11.4 Implement review queue
- [ ] 11.5 Implement notification history view and dashboard widgets (running tasks, next runs, pending approvals, recent notifications, kill switch)

## 12. Finish

- [ ] 12.1 Remove "(planned)" from `erd.puml` and `packages.puml` titles once all diagrammed structure exists; re-render
- [ ] 12.2 Update `README.md` status and getting-started section
