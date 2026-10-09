# Proposal

## Why

Home Assistant's energy dashboard records how much electricity, gas and water the household uses, but Marshal cannot answer "how much power did we use this week?" or "more than last week?". HA's MCP server only returns current states (`GetLiveContext`), not long-term statistics, so the data is out of reach for the model today.

## What Changes

- New safe tool `energy.usage(period, from?, to?, compareWith?)` that reads the energy dashboard's configuration and HA's long-term statistics over the websocket and returns totals for the period: grid import and export, solar production, battery charge and discharge, gas, water, the household's total consumption, cost and compensation where HA records them, and the top individual devices when the dashboard lists them.
- Periods: `today`, `yesterday`, `week` (since Monday), `month` (since the 1st), `last_week`, `last_month` and `range` with `from` / `to` dates, all in the owner's time zone.
- `compareWith: previous` returns the previous period of the same elapsed length next to the requested one (this week so far vs. last week up to the same weekday and hour), so one call answers comparison questions.
- The tool is hidden while Home Assistant is not configured or the energy dashboard has no sources.
- `energy` becomes a reserved tool namespace.

## Capabilities

### New Capabilities

None. Energy statistics are part of the Home Assistant integration.

### Modified Capabilities

- `home-assistant`: new requirement for energy statistics (tool, periods, comparison, fields, visibility, failure behavior).

## Non-goals

- "Run it when it's cheap": dynamic prices, solar forecasts or scheduling appliances.
- Live power (watts right now); `GetLiveContext` already covers current sensor states.
- Charts in the web UI or an energy view.
- Energy data in the daily briefing beyond what a briefing task can ask the tool for.
- Writing energy preferences or statistics.
- Per-device statistics for devices that are not on the energy dashboard.

## Impact

- `src/Marshal.Integrations.HomeAssistant`: new `energy` tool source using `IHomeAssistantCommands` (`energy/get_prefs`, `energy/info`, `recorder/statistics_during_period`); the cache's `get_config` read gains the currency.
- `src/Marshal.Core/Tools/ReservedNamespaces.cs`: adds `energy`.
- Tests in `tests/Marshal.Integrations.HomeAssistant.Tests` with recorded HA websocket responses as fixtures.
- No database, API or dependency changes.
