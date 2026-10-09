# Design

## Context

See proposal.md (Why). Relevant facts about Home Assistant (checked against the HA `dev` source, `components/energy` and `components/recorder`):

- `energy/get_prefs` returns `energy_sources` (each with a `type`: `grid`, `solar`, `battery`, `gas`, `water`) and `device_consumption` (`stat_consumption`, optional `name`, optional `included_in_stat` for nested devices). Values are **statistic ids**, not entity ids (often equal for sensors).
- The `grid` source exists in two shapes. Legacy: `flow_from[]` (`stat_energy_from`, `stat_cost`) and `flow_to[]` (`stat_energy_to`, `stat_compensation`). Current: flat `stat_energy_from`, `stat_energy_to`, `stat_cost`, `stat_compensation`. HA migrates the legacy shape over time, so both appear in the wild.
- When a source has a price but no `stat_cost`, HA creates cost sensors itself; `energy/info` returns `cost_sensors`, a map from energy statistic id to the generated cost entity id.
- `recorder/statistics_during_period` takes `start_time`, `end_time`, `statistic_ids`, `period` (`5minute` … `month`), `types` (`change`, `sum`, …) and `units` (e.g. `{"energy": "kWh"}`); it returns rows per statistic id. Long-term statistics are hourly, so boundaries are exact to the hour; the current hour is covered by the 5-minute statistics.
- `IHomeAssistantCommands.CommandAsync` already sends websocket commands on the cache's connection; the cache already reads `get_config` (for the time zone), which also returns `currency`.

## Goals / Non-Goals

**Goals:**
- One tool call answers "how much", "what did it cost", "what used the most" and "more than last time".
- No HA statistic ids or units leak into the model's decisions: the tool maps the dashboard for it.

**Non-Goals:**
- Caching statistics in Marshal. Every call reads HA; it is a few small websocket requests.
- Supporting HA versions without the energy integration.

## Decisions

### 1. Tool source in the HA integration, no new interface

`EnergyToolSource : IToolSource` (built-in, name `energy`) in `Marshal.Integrations.HomeAssistant`, registered next to `TimerToolSource`; no tools without `ha.url`. Reading the dashboard and statistics goes through the existing `IHomeAssistantCommands`.

| Tool | Tier | Hidden when |
|---|---|---|
| `energy.usage(period, from?, to?, compareWith?)` | safe | no HA configured; HA's energy prefs have no sources |

Visibility needs the prefs, so `IsAvailableAsync` reads them through a short-lived cache (a few minutes) to avoid a websocket round trip on every turn. While disconnected, the tool stays visible if it was visible before and fails with `ha.unavailable` on call, consistent with other HA tools.

New interfaces: none. A statistics interface would wrap three websocket commands with a single caller; the tool's logic is tested against fake websocket responses through `IHomeAssistantCommands`.

Alternative: query statistics through HA's REST API — HA has no REST endpoint for long-term statistics, only the websocket.

### 2. One statistics request per period

Collect every statistic id the dashboard uses (plus cost and compensation ids, plus device ids), and send one `recorder/statistics_during_period` with `period: hour` (exact boundaries), `types: [change]`, `units: {energy: kWh, volume: m³}`. Sum `change` per id. With `compareWith`, a second request for the previous period. `hour` over a month is ~720 rows per id; fine for a household dashboard. A month-long range could use `day` rows, but the boundaries must then fall on midnight; keep `hour` for v1 and revisit only if responses get slow.

Alternative: `period: day` / `week` / `month` aligned to the request — fewer rows, but "this week so far" ends mid-day, and HA's week/month periods follow HA's time zone, which may differ from the owner's.

### 3. Period math in the owner's time zone

The owner's time zone comes from the profile (same as tasks). Boundaries are computed as local times and converted to UTC. `week` starts Monday (ISO). The "previous period of equal elapsed length" rule is in the spec; `month` clamps the day (31 March → 28/29 February). DST is handled by computing local boundaries first and converting each separately, so a period may be 23 or 25 hours long.

### 4. Result shape

```json
{ "period": { "from": "2026-10-05T00:00", "to": "2026-10-08T14:00" },
  "unit": "kWh", "currency": "EUR",
  "gridImport": 41.2, "gridExport": 3.1, "solar": 18.4, "batteryCharge": 4.0, "batteryDischarge": 3.6,
  "consumption": 56.1, "cost": 12.40, "compensation": 0.25,
  "gas": { "value": 12.3, "unit": "m³", "cost": 9.80 }, "water": { "value": 2.1, "unit": "m³" },
  "topDevices": [ { "name": "Heat pump", "value": 22.0 } ],
  "previous": { "period": { … }, … same fields … } }
```
Absent sources are omitted (spec). Values are rounded to one decimal (money to two). Device names come from the dashboard `name`, else the entity's friendly name from the cache, else the statistic id. Multiple grid / solar / battery sources of one type are summed.

Cost per source: `stat_cost` if set, else `energy/info` `cost_sensors[stat_energy_from]`, else none. Same for compensation.

## Risks / Trade-offs

- [HA changes the prefs shape again] → parse defensively; unknown source types are ignored; fixtures for both grid shapes are part of the tests.
- [Statistics missing for part of the period (sensor added mid-week)] → sum what exists; the result does not invent zeros for missing hours. Not flagged in v1.
- [Large ranges (a year at hourly resolution)] → `range` longer than 92 days uses `period: day` with boundaries rounded to local midnight; noted in the result's `period`.
- [Gas in kWh vs. m³] → gas is reported in the unit HA's statistic has after unit conversion request; the result names the unit.

## Migration Plan

None. No stored data; the tool appears on the next start.
