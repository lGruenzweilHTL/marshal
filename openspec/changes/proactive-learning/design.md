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
