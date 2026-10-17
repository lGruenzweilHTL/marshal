# Proposal

## Why

Marshal only speaks when asked or when a task the user set up fires. It does not notice a window left open before rain, a device that suddenly uses twice its usual energy, or a print that finished while nobody is home. It also does not notice that the user keeps walking it through the same multi-step routine, which could become a skill. And there is no way to track maintenance by usage (nozzle after 300 print hours, dishwasher filter after 60 cycles); pure time intervals already work as cron tasks. Proactive help annoys when it is wrong. PRISM (ICLR 2026) cut false alarms by 22.8% by speaking only when the expected acceptance is high enough. All of this sits behind the "help Marshal learn" toggle from the evals change, except configured maintenance, which the user sets up explicitly.

## What Changes

- **Suggestions:** deterministic detectors in code (v1: open window before rain, energy anomaly per device, print finished while the owner is away) produce candidates with facts. A cheap model words them. A per-owner, per-detector acceptance estimate from Accept / Dismiss feedback decides whether a candidate is sent, and whether to the inbox (`low`) or as push (`normal`); never voice. A suggestion may carry a proposed action, frozen like an approval, which runs only on Accept. Detectors can be muted per owner. Only with the toggle on.
- **Skill proposals:** a nightly job finds repeated successful workflows: the same sequence of tools in at least 3 runs within 30 days, with similar requests, not rated down. It drafts a `SKILL.md` from the user requests and the tool sequence (never from tool results) and puts it into the Inbox as a proposal with preview, a diff against an existing skill of the same name, and download. `/config` stays read-only; the user copies the file. Only with the toggle on.
- **Maintenance by usage:** `maintenance.items` in `marshal.yaml`, each with a counter source (a Home Assistant sensor or statistic, or Moonraker print hours / filament), an interval and an optional early warning. Marshal stores a baseline per item, notifies (`normal`) when the interval is reached, and offers `maintenance.status` and `maintenance.done(item)`. A Home widget lists items. With the toggle on, a detector suggests items for counters it finds (with a YAML snippet to paste).
- `maintenance` becomes a reserved namespace.

## Capabilities

### New Capabilities

- `suggestions`: detectors, wording, acceptance estimate and thresholds, delivery, proposed actions, feedback, muting.
- `skill-proposals`: workflow detection, drafting, the proposal item.
- `maintenance`: configuration, counters and baselines, notifications, tools, widget, suggested items.

### Modified Capabilities

- `background-views`: the Inbox lists suggestions and skill proposals.
- `notifications`: suggestions are a notification kind with Accept / Dismiss push actions.
- `tool-registry`: `maintenance` is reserved.
- `printer`: optional `powerEntity` per printer (used by the print-done-away suggestion).

## Non-goals

- A model that scans the whole house state for anything worth saying.
- Voice delivery of suggestions.
- Writing skills or `marshal.yaml` from Marshal.
- Time-only maintenance intervals (cron tasks cover them).
- Detectors defined in configuration.

## Impact

- `src/Marshal.Tasks`: suggestion pipeline, acceptance store, skill proposal job, maintenance tracker and tools.
- `src/Marshal.Integrations.HomeAssistant` / `.Moonraker`: detectors and counter sources.
- `src/Marshal.Data`: suggestion, detector stats, skill proposal and maintenance state tables; migration; `erd.puml`.
- `src/Marshal.Api`, `web/`: Inbox items, Home widget, detector muting in preferences.
- Depends on `evals-and-learning-toggle` (toggle) and `ha-ai-task` (structured output shaper for wording).
