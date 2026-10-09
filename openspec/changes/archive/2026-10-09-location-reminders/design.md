# Design

## Context

See proposal.md (Why) for the motivation. Current state that shapes the approach:

- `tasks.watch` stores a task with `TaskTrigger(Event, filterJson)` (`TaskTools.cs`). `DeclarativeTriggerMatcher` fires an armed watch on a matching event. After a `done` run, `BackgroundTaskScheduler.RearmAsync` puts every `event` task back to `waiting`, so watches recur forever.
- The trigger is persisted as two columns on `AgentTask`: `trigger_kind` and `trigger_spec`. The API's `TaskResponse` exposes the `TaskTrigger` record as is.
- `HomeAssistantEventSource` publishes `state_changed` with `from`, `to` and the new `attributes`. For a `person.*` entity, the state is `home`, `not_home` or the **name** of the zone the person is in (not the zone id). Passive zones never become a person's state.
- `HaSnapshot.States` already holds every entity's state, including `zone.*` (state = person count; attributes `latitude`, `longitude`, `radius`, `passive`, `persons`, `friendly_name`) and `person.*` (attributes `latitude`, `longitude`, `gps_accuracy` when a GPS tracker feeds it).
- `IHomeAssistantCommands.CommandAsync` sends websocket commands on the cache's connection. HA's `zone/create` takes `name`, `latitude`, `longitude`, `radius` (HA default 100), `passive`, `icon`, and requires an administrator user.
- The owner's person entity is `UserPreferences.NotificationTargets.Person` (also used for `$person` in delivery conditions).

## Goals / Non-Goals

**Goals:**
- A one-off watch that is deterministic: ending after one run is decided by the scheduler, not by the model.
- The model writes the watch filter from data returned by a tool, not from a guess.
- No new external dependency and no new outside call.

**Non-Goals:**
- Enforcing "own person only" inside `tasks.watch`. Watches on any HA entity are existing behavior (e.g. occupancy sensors). The places tools only expose the owner's person, and that is the boundary this change sets.
- A dedicated reminder concept or reminder tool. A location reminder is a once-watch.

## Decisions

### 1. `once` lives on the trigger, stored in its own column

`TaskTrigger` gains `bool Once` (default `false`, meaningful only for `Event`); `TaskTrigger.Event(filterJson, once)` builds it. `AgentTask` gains a `trigger_once` column (non-null, default `false`), so existing rows migrate unchanged. `RearmAsync` checks `Kind == Event && !Once` before re-arming; a once-watch takes the default branch and ends `Done`.

Alternatives:
- A reserved `once` key inside the filter JSON: no migration, but it mixes "when to fire" with "how often", and `EventFilter` would have to skip it as a condition.
- A new trigger kind `event_once`: every `switch` over `TaskTriggerKind` (matcher, scheduler, API labels, web `TriggerLabel.vue`) grows a near-duplicate branch.
- A task-level flag: `once` only means something for event triggers, so it belongs to the trigger.

### 2. API exposes `once` additively

`TaskTrigger` is serialized in `TaskResponse`, so the new property appears as `trigger.once` in `/api/v1/tasks`. Adding a response field is backward compatible, so v1 gets it without a new version. The SPA regenerates its types (`npm run gen:api`) and `TriggerLabel.vue` shows "once" for once-watches. `tasks.list` / `tasks.status` output for the model includes `once` as well.

### 3. `tasks.watch` argument

The schema gains `"once": {"type": "boolean", "description": "Fire on the first matching event only, then end (one-off reminders)."}`. A non-boolean value fails validation in the tool. The tool description gains one sentence: for a place, call `places_find` first and filter on the owner's person entity with `to` (arriving) or `from` (leaving).

### 4. Places tools: a built-in `places` tool source in the HA integration

`PlacesToolSource : IToolSource` (built-in, name `places`) in `Marshal.Integrations.HomeAssistant`, registered next to `TimerToolSource`, returns no tools when `ha.url` is not configured. `places` is added to `ReservedNamespaces`.

| Tool | Tier | Persists | Hidden when |
|---|---|---|---|
| `places.find(query?)` | safe | no | no HA configured |
| `places.create_here(name, radius?)` | confirm (`DefaultTier = Confirm`) | no | no HA configured, or the owner has no person entity |

`create_here` changes HA configuration, so it is confirm tier in every run (tainted or not); the confirmation summary names the zone, radius and "at your current position". The frozen confirm action holds name and radius only; the position is read when the confirmed call runs, so the zone lands where the owner is at confirmation time. Neither tool returns untrusted content: zone names and positions are the owner's own HA configuration.

`find` output per zone: `id`, `name`, `radius`, `passive`, `ownerInside`, `personState` (`home` for `zone.home`, else the name; `null` for passive zones), plus the top-level `person` entity id. Matching is case-insensitive substring on name and id; without a query all zones are listed (households have few).

`create_here` checks, in order, with no HA call on failure: name not empty; radius 50–1000 (default 150, a store and its parking lot); person entity configured; person state has numeric `latitude` / `longitude`; no zone with the same name (case-insensitive). Then `zone/create`; the response's zone is mapped to the `find` shape. HA errors come back as `ha.command_failed` with HA's message.

New interfaces: none. The tools build on existing small interfaces: `IHomeAssistantCache` (zones, person state), `IHomeAssistantCommands` (`zone/create`) and `IUserPreferencesStore` (owner's person entity). A zone-specific interface would have one caller and wrap a single command.

Alternatives:
- Zone lookup through HA's MCP server: zones are not exposed to Assist, and `GetLiveContext` does not list them.
- `$person` placeholder in watch filters, resolved at match time: neat, but it adds a substitution concept to `EventFilter` for one use case; returning the entity id from `places.find` is enough.

## Risks / Trade-offs

- [Zone renamed in HA after the watch is created] → the filter's `to` no longer matches and the reminder never fires. Accepted for v1 (non-goal); `places.find` makes the initial value right, and the task stays visible in the task list.
- [Model skips `places_find` and guesses the zone name] → the tool description steers it; the watch still shows the filter in the task list. A filter-side check would need HA knowledge in `Marshal.Tasks`.
- [GPS jitter at the zone edge] → a once-watch fires once anyway; recurring `from` watches may fire twice. Larger default radius (150 m) lowers it.
- [HA token user is not an administrator] → `zone/create` fails with HA's message; documented in the proposal impact and the tool error.
- [Owner's position is stale] → HA's person position is as fresh as the phone's last update; `create_here` returns the coordinates used so the reply can mention them.

## Migration Plan

One EF Core migration adds `trigger_once` (`INTEGER NOT NULL DEFAULT 0`) to `agent_tasks`. Forward-only, like every migration; existing watches keep recurring. Rollback = restore the `/data` backup taken before the upgrade. `erd.puml` / `.svg` updated in the same change.
