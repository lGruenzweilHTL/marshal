# Proposal

## Why

"Remind me to buy wall plugs when I'm at the hardware store" is a natural voice request that Home Assistant automations cannot serve: they are static, and ad-hoc reminders are exactly what Marshal adds. The event bus and `tasks.watch` can almost do it today, but three gaps make it unreliable: a watch re-arms after every run, so a one-off reminder would fire on every later visit; the model cannot see which zones exist, so it guesses the zone name in the filter and a wrong guess silently never matches; and a place that has no zone yet cannot be added by voice.

## What Changes

- `tasks.watch` gains a `once` argument. A once-watch fires on its first matching event and then ends `done` instead of re-arming. The flag is stored with the task and shown in the task API and web UI.
- New safe tool `places.find(query?)`: lists Home Assistant zones from the state cache (zone id, name, radius, whether the owner is inside now), matched against the spoken place name. It also tells the model the value that the owner's person entity takes in that zone (the zone's name, or `home` for `zone.home`), so the watch filter uses the exact value.
- New confirm-tier tool `places.create_here(name, radius?)`: creates a Home Assistant zone at the owner's current GPS position (from their person entity) through the websocket `zone/create` command. No geocoding; most zones are still created in Home Assistant itself.
- Location watches are limited to the owner's own person entity: the `places.*` tools only ever read the owner's configured `person.*` entity, and the tool descriptions steer the model to `tasks.watch` with that entity and `to` (arriving) or `from` (leaving).
- `places.*` tools are hidden without a Home Assistant connection, and `places.create_here` is also hidden when the owner has no person entity configured.

## Capabilities

### New Capabilities

None. Zone lookup and creation are part of the Home Assistant integration.

### Modified Capabilities

- `task-system`: `tasks.watch` gains a `once` flag; a once-watch ends `done` after its first run instead of waiting for the next event.
- `event-bus`: the deterministic trigger matching requirement gains a scenario for a once-watch (fires once, never again).
- `home-assistant`: new requirement for places (zone lookup from the cache, zone creation at the owner's position, confirm tier, visibility rules).

## Non-goals

- Watching other people's locations ("tell me when my partner gets home"). Only the owner's own person entity is used.
- Geocoding a place name to coordinates, or any outside lookup service. Zones come from Home Assistant or from the owner's current position.
- A map picker in the web UI for creating zones.
- Updating or deleting zones from Marshal.
- Debounce or dwell time against GPS jitter at a zone edge. A once-watch is not affected; recurring "when I leave" watches may fire more than once.
- A "static reminder" delivery path that skips the model run. A fired reminder still runs its goal like any other watch.
- Detecting watches whose filter names a zone that no longer exists (e.g. after a rename in Home Assistant).

## Impact

- `src/Marshal.Abstractions/Tasks` (`TaskTrigger` or task record gains the once flag), `src/Marshal.Data/Tasks/AgentTask.cs` plus a migration, `docs/diagrams/erd.puml` / `.svg`.
- `src/Marshal.Tasks/Tools/TaskTools.cs` (`watch` schema), `src/Marshal.Tasks/Scheduling/BackgroundTaskScheduler.cs` (re-arm decision).
- `src/Marshal.Api/Tasks/TaskContracts.cs`: task responses gain an additive `once` field (no new API version, additive only); `web/` task views show it.
- `src/Marshal.Integrations.HomeAssistant`: new `places` tool source over `IHomeAssistantCache`, `IHomeAssistantCommands` and the owner's preferences; `places` added to `ReservedNamespaces`.
- `deploy/config.example/risk.yaml` if risk rules list tools explicitly.
- Tests in `tests/Marshal.Tasks.Tests`, `tests/Marshal.Integrations.HomeAssistant.Tests`, `tests/Marshal.Api.Tests` and `web/` component tests.
- Home Assistant: `zone/create` needs the long-lived token's user to be an administrator; otherwise the tool fails with HA's error.
