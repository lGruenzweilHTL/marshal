# Proposal

## Why

Switching a model, editing a template or changing the base prompt can quietly break things that used to work ("turn off the kitchen light" now calls the wrong tool), and nobody notices until it happens live. Marshal records every run, but there is no way to turn good and bad runs into test cases and replay them against a new configuration. Several upcoming features (evals, proactive suggestions, skill proposals, suggested maintenance reminders) learn from the user's behavior and some send notifications. They need one clear consent setting.

## What Changes

- **"Help Marshal learn"**: a per-user preference, off by default. It gates every learning feature: rated runs becoming eval cases (this change), proactive suggestions, skill proposals and suggested maintenance reminders (later changes). The preferences view explains what it enables.
- **Ratings:** thumbs up / down on assistant replies in web chat and on every run in the run detail (so voice runs can be rated afterwards), with an optional note on a thumbs down ("should have turned off the ceiling light, not the lamp"). Ratings are stored on the run whatever the toggle says.
- **Eval cases:** with the toggle on, a rated run becomes an eval case automatically. Admins can also save any run as a case from the run detail. A case snapshots the turn's input, history, turn-level context (retrieved memory, area, time, task), recorded tool calls with their results, the reply, the rating and the note.
- **Replay:** an admin view runs a set of cases against a candidate configuration (model per tier, template, base prompt override). Tools are not executed: a call whose tool and arguments match a recorded call gets the recorded result; any other call gets "no recording" and marks the case as diverged. The report compares tool sequence, arguments and reply per case (pass / changed / diverged / failed), with an optional judge model for reply quality, and the total cost.
- Recording: the context step of every run stores the text of its turn-level contributions, so cases can rebuild the context.

## Capabilities

### New Capabilities

- `evals`: ratings, eval cases, replay, comparison, reports, the admin view.

### Modified Capabilities

- `user-preferences`: the "help Marshal learn" preference.
- `web-chat`: rating replies.
- `run-insights`: rating runs and saving them as cases in the run detail.
- `run-recording`: turn-level context text in the context step.

## Non-goals

- Running evals in CI (tests never need a real model).
- Automatic tuning of prompts or thresholds from eval results.
- Replaying with live tool execution or demo simulations.
- Rating by voice command.
- Sharing eval cases between instances.

## Impact

- `src/Marshal.Core`: replay runner (agent loop with a recorded-result tool executor), comparison.
- `src/Marshal.Data`: run ratings, eval cases, eval runs and results; migration; `erd.puml`.
- `src/Marshal.Api`: rating, case and eval run endpoints (admin for cases and replays).
- New packages: `Microsoft.Extensions.AI.Evaluation` and `.Quality` for the optional judge (package index in `CLAUDE.md`, `packages.puml` if it lists key dependencies).
- `web/`: thumbs in chat and run detail, preferences toggle, evals admin view.
