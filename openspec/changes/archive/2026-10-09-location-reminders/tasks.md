# Tasks

Groups 1 and 3 are independent and can run in parallel. Group 2 depends on group 1. Group 4 needs all of them.

## 1. Once-watch (task system)

- [x] 1.1 Write failing `Marshal.Tasks.Tests` scheduler tests: a watch with `once` that finishes `done` ends `done` with no `next_run_at`, and a second matching event does not make it due; a watch without `once` still returns to `waiting` (existing behavior). Verify the new tests fail.
- [x] 1.2 Add `bool Once` (default `false`) to `TaskTrigger` and `TaskTrigger.Event(filterJson, once)`, and skip re-arming once-watches in `BackgroundTaskScheduler.RearmAsync`. Verify the 1.1 tests pass.
- [x] 1.3 Write a failing persistence test: a once-watch saved and read back through the task store keeps `Once`; an existing row without the column reads as `false`. Verify it fails.
- [x] 1.4 Add the `TriggerOnce` property to `AgentTask` and its mapping, create the migration (`dotnet ef migrations add TaskTriggerOnce --project src/Marshal.Data --output-dir Migrations`) with a non-null `false` default, and update `docs/diagrams/erd.puml` plus the re-rendered `erd.svg`. Verify the 1.3 test passes.
- [x] 1.5 Write failing `TaskTools` tests: `tasks_watch` with `once: true` creates an event trigger with `Once`; `once: "yes"` fails with a validation error and creates no task; `tasks_list` and `tasks_status` output shows `once` for a once-watch. Verify they fail.
- [x] 1.6 Add `once` to the `watch` schema and argument parsing, mention `places_find`, `to` and `from` in the `watch` description, and add `once` to list and status output. Verify the 1.5 tests pass.

## 2. Once in the API and web UI (after group 1)

- [x] 2.1 Write a failing `Marshal.Api.Tests` test: `GET /api/v1/tasks/{id}` for a once-watch returns `trigger.once = true`, and a cron task returns `trigger.once = false`. Verify it fails, then make it pass by including `Once` in the trigger contract (additive, v1). Verify the OpenAPI document shows the new field.
- [x] 2.2 Regenerate the SPA API types (`npm run gen:api` in `web/`). Write a failing component test that `TriggerLabel` shows the "once" marker for a once-watch in German and English. Add the label and catalog entries. Verify `npm test` and `npm run build` pass.

## 3. Places tools (Home Assistant integration)

- [x] 3.1 Add `places` to `ReservedNamespaces` and write a failing registry test that a non-built-in source cannot use it. Verify it passes after the change.
- [x] 3.2 Write failing `Marshal.Integrations.HomeAssistant.Tests` for `places_find` over a fake cache and fake preferences: match by name and id (case-insensitive substring); `zone.home` → `personState = home`; a passive zone is listed with `passive = true` and `personState = null`; `ownerInside` follows the owner's person state; result includes the owner's `person` id; no person configured → no presence and no `person`; cache not loaded → `ha.unavailable` error. Verify they fail.
- [x] 3.3 Implement `PlacesToolSource` (built-in, no tools without `ha.url`) with `places.find` (safe). Register it next to `TimerToolSource`. Verify the 3.2 tests pass.
- [x] 3.4 Write failing tests for `places_create_here` with a fake `IHomeAssistantCommands`: descriptor tier is confirm; hidden without a person entity; default radius 150 and range 50–1000 enforced; no GPS → error, no command; duplicate name (case-insensitive) → error naming the zone, no command; success sends `zone/create` with name, the person's latitude and longitude and radius, and returns the zone in the `find` shape; an HA rejection returns `ha.command_failed` with HA's message; the confirmation summary names the zone and radius. Verify they fail.
- [x] 3.5 Implement `places.create_here`. Verify the 3.4 tests pass.
- [x] 3.6 Write a failing guard/visibility test in `Marshal.Api.Tests` (or the HA test project, whichever hosts the tool registry): with HA configured and a person entity, `places_find` is visible and a `places_create_here` call creates a pending confirmation and sends no command; without HA neither tool is visible. Verify it passes.
- [x] 3.7 Update the repository map entry for `Marshal.Integrations.HomeAssistant` in `CLAUDE.md` (adds places tools). Verify the entry reads correctly.

## 4. Integration check

- [x] 4.1 Write an end-to-end test with a fake `IChatClient`, the fake HA websocket and the real task worker: the model calls `places_find` then `tasks_watch` with `once`; a fake `person.*` state change to the zone name fires the watch once, its run delivers, and a second entry fires nothing. Verify it passes.
- [x] 4.2 Run the full .NET suite (`dotnet test`) and the web suite (`npm test`, `npm run build`), and run `openspec validate location-reminders --strict`. Verify all pass.
