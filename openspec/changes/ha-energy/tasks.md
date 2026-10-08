# Tasks

## 1. Fixtures and period math

- [ ] 1.1 Add recorded websocket fixtures to `tests/Marshal.Integrations.HomeAssistant.Tests/Fixtures`: `energy/get_prefs` in the current and the legacy grid shape (grid, solar, battery, gas, water, seven devices with one nested), an empty prefs response, `energy/info` with `cost_sensors`, and `recorder/statistics_during_period` responses. Verify the fixture test that loads every fixture passes.
- [ ] 1.2 Write failing tests for period resolution in the owner's time zone: `today`, `yesterday`, `week` (Monday start), `month`, `last_week`, `last_month`, `range` (inclusive `to`), and each `compareWith: previous` rule including the month clamp and a DST change week. Verify they fail.
- [ ] 1.3 Implement period resolution (local boundaries converted to UTC separately). Verify the 1.2 tests pass.

## 2. Energy tool

- [ ] 2.1 Add `energy` to `ReservedNamespaces`; verify the registry test that rejects a non-built-in source in a reserved namespace covers it.
- [ ] 2.2 Write failing tool tests over a fake `IHomeAssistantCommands`: both grid shapes give the same totals; consumption formula with solar and battery; cost from `stat_cost` and from `cost_sensors`; no cost field without either; top five top-level devices ordered by consumption; absent sources omitted; `compareWith: previous` adds `previous`; one statistics request per period with `period: hour`, `types: [change]` and kWh / m³ units; `range` longer than 92 days uses `period: day`; invalid period, missing range dates and `from` after `to` fail without a statistics request; disconnected HA fails with `ha.unavailable`. Verify they fail.
- [ ] 2.3 Implement `EnergyToolSource` and `energy.usage`, and read `currency` from `get_config` into the cache snapshot. Verify the 2.2 tests pass.
- [ ] 2.4 Write failing visibility tests: no tool without `ha.url`; tool hidden when prefs have no sources; prefs read at most once within the cache window across several visibility checks. Implement them. Verify the tests pass.
- [ ] 2.5 Register the tool source in `HomeAssistantServiceCollectionExtensions` and write a failing registry-level test that `energy_usage` is in the tool list with HA configured and a fake HA that has energy sources. Verify it passes.
- [ ] 2.6 Update the `Marshal.Integrations.HomeAssistant` entry in the `CLAUDE.md` repository map (energy tool). Verify the entry reads correctly.

## 3. Integration check

- [ ] 3.1 Run the HA test project and the full .NET suite (`dotnet test`) and `openspec validate ha-energy --strict`. Verify all pass.
