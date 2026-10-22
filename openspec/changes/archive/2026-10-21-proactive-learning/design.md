# Design

## Context

See proposal.md (Why). Current state:

- The event bus carries normalized HA state changes, printer events (`printer.complete`, ...) and others; the trigger matcher is deterministic.
- The notification router chooses channels by priority, presence, quiet hours and caps; push actions arrive as `mobile_app_notification_action` events and are checked by nonce; approvals freeze actions and the server executes them.
- The HA integration reads energy prefs and statistics (energy change) and has a state cache; weather forecasts come from the `weather.get_forecasts` action with response data (verify the exact websocket call when building).
- Moonraker exposes `/server/history/totals` (`total_print_time` in seconds, `total_filament_used` in mm); verify field names against the current Moonraker docs when building.
- The evals change adds the "help Marshal learn" preference; the AI task change adds the structured output shaper.

## Goals / Non-Goals

**Goals:**
- Deterministic detection, model only for wording.
- Learning per owner and detector with a simple, inspectable estimate.

**Non-Goals:**
- Contextual bandits or per-situation features; one estimate per detector is enough for v1 and easy to explain.

## Decisions

### 1. Suggestion pipeline

New interfaces:
- `ISuggestionDetector` (`Marshal.Abstractions`): `Id`, `Triggers` (event filters and/or an interval), `EvaluateAsync(owner, trigger) → IReadOnlyList<SuggestionCandidate>` (facts, dedupe key, expiry condition, optional proposed action). v1: `WindowRainDetector`, `EnergyAnomalyDetector` (HA), `PrintDoneAwayDetector` (Moonraker + HA presence), `MaintenanceCounterDetector` (Tasks).
- `ISuggestionPolicy` (`Marshal.Tasks`): `Decide(owner, detector) → Drop | Inbox | Push`. v1: `BetaAcceptancePolicy` (the formula in the spec, explore count, thresholds).
- `ISuggestionStore`: candidates, sent suggestions, counts, mutes. v1: EF tables `suggestions` and `suggestion_detector_stats`.

Flow: event or interval → detectors (only owners with the toggle on and the detector not muted) → dedupe → policy → wording (`fast` tier through the structured output shaper: `{ text }`, facts as input) → notification router with kind `suggestion` and priority from the policy. The facts come from HA, Moonraker and the energy statistics; they are the owner's own device data, not third-party text, so wording runs untainted and without tools.

Why the formula: it is a Beta(1,1) posterior mean, close to PRISM's idea of an acceptance threshold from the cost of false alarms and missed help, but explainable in the UI ("you accepted 5 of 6").

### 2. Proposed actions

A candidate's proposed action is frozen through the approval queue's pending action store with kind `suggestion` and TTL = the suggestion's expiry. Accept runs it through the guard. A safe action executes; a confirm-tier action counts as confirmed, because Accept is a user action with a nonce. The Accept button text shows the action's reason sentence for confirm-tier actions. v1 proposed actions: `print_done_away` → switch off a configured printer power entity (`printers[].powerEntity`, new optional key; without it no action).

### 3. Skill proposals

`SkillProposalJob` (system task after nightly consolidation). Query runs per owner (30 days, ok, ≥3 tool calls, not rated down), build the tool id sequence from steps, group by equal sequence, then split groups by input embedding similarity (≥ 0.8; input embeddings are computed and cached per run, since the turn embedding is not stored today). Groups of ≥3 that no proposal row covers go to the `standard` tier with the user inputs, tool ids, argument keys and the list of existing skill names and descriptions. The model returns `{ covered: bool, name, description, body }`. If not covered, a `skill_proposals` row is stored (owner, name, file content, source run ids, sequence hash, status). The diff against an existing skill uses the same unified diff code as coding proposals.

### 4. Maintenance

`MaintenanceOptions` (validated), `IMaintenanceCounterSource` (`Marshal.Abstractions`): `ReadAsync(source) → Result<decimal>`. v1: `HaCounterSource` (entity state, else statistics `sum`), `MoonrakerCounterSource` (history totals; hours = seconds / 3600, meters = mm / 1000). `MaintenanceTracker` (hosted service in `Marshal.Tasks`) subscribes to HA state events of configured entities and `printer.complete`, plus an hourly timer; stores `maintenance_state (owner_id, item_id, baseline, last_done_at, notified_soon, notified_due)`. Items belong to the instance owner (configuration is instance-level; multi-user later adds an owner key).

| Tool | Tier | Hidden when |
|---|---|---|
| `maintenance.status(item?)` | safe | no items configured |
| `maintenance.done(item)` | safe, `persists` | no items configured |

### 5. Toggle checks

Detectors and the skill job check `IUserPreferences.HelpMarshalLearn` per owner at run time; turning the toggle off stops new suggestions immediately; open ones stay until they expire.

## Risks / Trade-offs

- [Forecast sources differ (weather integrations without hourly forecasts)] → `window_rain` uses the hourly forecast when available, else daily, else stays silent; documented.
- [Energy anomaly on new devices with little history] → requires 14 days of statistics before it fires.
- [Skill drafts copy wrong habits] → proposals are reviewed and copied by the user; nothing installs automatically.
- [Accepting a suggestion with a confirm-tier action by push is a one-tap power action] → only detectors in code can attach actions; v1 attaches only a printer power-off.

## Migration Plan

EF migration adds `suggestions`, `suggestion_detector_stats`, `skill_proposals`, `maintenance_state` and run input embedding cache columns. All features are off until the user turns on the toggle (suggestions, skill proposals) or configures `maintenance.items`.

## Implementation Notes

- **HA weather forecast (verified 2026-10-21 against the HA weather integration docs and the websocket API docs):** `weather.get_forecasts` with `service_data: { type: hourly | daily | twice_daily }` and `target: { entity_id }` is called over the websocket as `call_service` with `return_response: true`; the result is `{ context, response }`, and `response` maps each entity id to `{ forecast: [ { datetime, condition, precipitation_probability, precipitation, temperature, ... } ] }`. An entity that does not support a type answers with an error, which `window_rain` treats as "no hourly forecast" and falls back to `daily`. The forecast fixtures (`weather-forecast-hourly.json`, `weather-forecast-daily.json`) are hand-written in that documented shape, not recorded.
- `window_rain` reads the `weather.*` entity in `suggestions.detectors.window_rain.weatherEntity`, else the first one by id; it runs on HA state changes (only when a window or door sensor opened) and every 15 minutes. Door sensors (`device_class: door`) count like windows, as the spec says.
- Wording uses `ChatResponseFormat.ForJsonSchema` and parses `{ text }` itself (same pattern as the idle memory jobs), recorded as a run with trigger `suggestion:<detector>` and checked against the daily cost cap.
- `SuggestionOptions` lives in `Marshal.Abstractions` (integrations read their detector's parameters); its validator is in `Marshal.Tasks`.
- An inbox suggestion is stored only (no notification row), so the Inbox shows it once; a pushed one is also sent through the router with source `suggestion:<detector>`.
- All three v1 HA-facing detectors live in `Marshal.Integrations.HomeAssistant/Suggestions` (`print_done_away` needs HA presence and entity names; it reads printers through `IPrinterDirectory`, which now carries `PowerEntity`). Its proposed action is `ha.HassTurnOff` with the power switch's friendly name and `domain: [switch]`, since Assist intents address entities by name; it is offered only while the switch is on, and an open suggestion expires once the switch is off.
- `energy_anomaly` parses the dashboard with the energy tool's `Dashboard` parser and reads daily `change` statistics in kWh for the 15 local days up to yesterday; it runs every six hours (dedupe key: statistic and day).
- **Moonraker job totals (verified 2026-10-21 against the Moonraker history API docs):** `GET /server/history/totals` answers `{ job_totals: { total_jobs, total_time, total_print_time, total_filament_used, longest_job, longest_print }, auxiliary_totals: [...] }` inside Moonraker's `result` wrapper; times are seconds and filament is millimeters. The counter source test uses the documented example (print time adjusted to 300 h).
- `HaCounterSource` reads an entity's numeric state (unit from `unit_of_measurement`); an id that is not a current entity is read as a long-term statistic's latest hourly `sum` over the last day.
- `maintenance.items.<id>.source.printer` is checked against every registered `IPrinterDirectory` (the configured printers, or the demo printer in demo mode).
- Skill proposals run as their own seeded system job (`skill_proposals`, schedule `maintenance.skillProposalSchedule`, default `0 4 * * *`, after consolidation at 02:30) rather than as a step inside the consolidation chain: `Marshal.Tasks` cannot reference `Marshal.Memory`, and the job needs nothing from consolidation. Workflows the model judges covered by an existing skill are stored with status `covered`, so their sequence hash is not offered to the model again.
- The diff against a same-name skill comes from a small LCS-based unified diff (`UnifiedDiff`, 3 lines of context) in `Marshal.Tasks`; coding proposals take their diffs from git, so there was no shared diff code to reuse.
- One combined migration (`ProactiveLearning`) adds `suggestions`, `suggestion_detector_stats`, `skill_proposals`, `maintenance_state` and `runs.input_embedding`.
- `maintenance_counter` lives in `Marshal.Tasks/Maintenance` and asks every `IMaintenanceCounterSource` to list candidate counters (Moonraker: each configured printer's print hours; HA: `sensor.*` with state class `total_increasing` whose id or name matches runtime, hours, cycles, operating, Betriebsstunden, Zyklen or Laufzeit). A printer counts as covered when any item uses that printer, whatever the metric. The YAML snippet is a `SuggestionCandidate.Snippet`, stored with the suggestion and shown verbatim below the worded text; it never reaches the wording model. Its starting intervals are 300 h for a printer nozzle, 1000 for hour counters and 100 otherwise.
- Web: skill proposals sit under the Inbox's "Changes" filter and suggestions under "Alerts"; both sections are hidden while empty. The navigation badge counts open skill proposals with the open change proposals; a failing skill-proposal list leaves them out instead of freezing the badge. The skill proposal detail is its own screen (`/inbox/skills/:id`); Download is a plain link to the download endpoint, so the browser saves `SKILL.md` and the server marks it downloaded. Detector mute and reset apply at once in a "Suggestions" card in Preferences, outside the preferences form.
- Demo: the simulated printer reports job totals that grow with the clock (`DemoPrinterCounterSource` replaces Moonraker's counter source), and the simulated HA answers `weather.get_forecasts` with rain in the next hours, so the open workshop window yields a `window_rain` candidate. The demo maintenance item (`voron_nozzle`) is added by the host as a post-configure step when the printer is simulated and no items are configured (`Marshal.Demo` cannot reference `Marshal.Tasks`, which owns `MaintenanceOptions`); the seeder starts it at 286 of 300 h only in that case (`DemoSeedParts.MaintenanceItem`).
