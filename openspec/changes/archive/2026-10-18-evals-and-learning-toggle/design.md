# Design

## Context

See proposal.md (Why). Current state:

- Runs and steps are recorded (model calls, tool calls with arguments and truncated or redacted results, the reply). The context step does not store the context text.
- After `tool-retrieval-and-latency`, context contributions are split into `static`, `session` and `turn` stability, and the builder can return the parts separately.
- Demo mode simulates integrations but its state differs from any recorded run, so it cannot answer recorded calls.
- `Microsoft.Extensions.AI.Evaluation` (+ `.Quality`, version 10.x) offers LLM-judged evaluators (relevance, coherence, tool call accuracy) and reporting with response caching.

## Goals / Non-Goals

**Goals:**
- Replays are deterministic apart from the candidate model's own output.
- No replay can touch the house, mail, memory or tasks.

**Non-Goals:**
- Multi-turn replay of whole sessions. A case is one turn with its history.

## Decisions

### 1. What a case stores

`eval_cases`: owner, id, source run id, expectation (`good` from up, `bad` from down, `unrated` when saved without rating), note, input, input mode, language, satellite, area, history (JSON messages, user and assistant text only, as the agent loop gets them), turn context text, recorded tool calls (JSON: tool id, canonical args, result as stored, untrusted flag), reply, `incomplete`, timestamps.

Canonical arguments: JSON with sorted keys and normalized numbers, so `{"b":1,"a":2}` equals `{"a":2,"b":1}`.

Static and session context are not stored. Replay rebuilds them from the current configuration plus the candidate overrides, because they are what is being tested. Turn context is replayed from the snapshot, so memory changes since the run do not change the case.

### 2. Replay runner

New interfaces (`Marshal.Core`):
- `IEvalReplayer`: `ReplayAsync(EvalCase, CandidateConfig, CancellationToken) → ReplayResult`. v1: `AgentLoopReplayer`, which runs `IAgentRunner` with a replay execution context (`IsReplay = true`, `IsInteractive = true` so confirm-tier calls produce confirmation questions rather than queue entries, and a fixed clock at the case's time).
- `IToolExecutor` seam: the runner already executes tools through the registry; replay swaps in `RecordedToolExecutor`, which matches `(tool id, canonical args)` against the case in order, allows repeats of the same recorded call, and returns `no_recording` otherwise.
- The guard runs as usual; the recorder is replaced by an in-memory recorder that feeds the result, so nothing reaches the runs tables. Pending actions and approvals are not persisted in replay.

Because no tool executes, writes cannot happen even if the guard allowed them. The `IsReplay` flag is a second line for any component that writes outside tools (memory consolidation hooks, session close), which skip replay contexts.

### 3. Comparison

Tool comparison is deterministic (sequence of tool ids, then arguments). Reply comparison: exact match → `pass`; otherwise `changed` unless a judge is selected. Judge: `RelevanceEvaluator` / `CoherenceEvaluator` from `Microsoft.Extensions.AI.Evaluation.Quality`, plus a custom evaluator that compares against the recorded reply and the case note, using a chosen configured model. Judge calls count as eval cost.

### 4. Eval runs

`eval_runs` (owner, candidate config JSON, started by, status, totals, cost) and `eval_results` (run, case, result, replayed calls and reply, judge score, cost). Eval runs execute on the task worker pool as a system job with its own cancellation, one case at a time. Cost estimate: average recorded tokens of the selected cases × candidate model prices.

### 5. Ratings and toggle

`runs.rating`, `runs.rating_note`, `runs.rated_at`. `PUT /api/v1/runs/{id}/rating` (cookie principals, own runs). The case is created in the same transaction when the owner's preference is on. `user_preferences.help_marshal_learn` (bool, default false). Later changes read the same preference through `IUserPreferences`.

### 6. Decisions made while building

- **Bad cases that repeat themselves:** a `bad` case whose replay makes the same tool calls as the recording is `changed`, not `pass`: the recorded behavior was wrong, so repeating it is a change of nothing worth reporting as success, but it is no failure of the replay either.
- **Judge threshold:** with a judge, a reply that differs from the recording counts as the same when the judge scores it at least 0.7; below that the case is `changed`.
- **Cost estimate:** per case, the recorded text (history, turn context, input, tool arguments and results, reply) at about four characters per token plus a fixed 2,000-token allowance for the system prompt and tool definitions, sent once per model call (recorded tool calls plus one), priced with the candidate model of the case's tier (voice cases on `fast`, others on `standard`).
- **Worker:** eval runs execute on a dedicated background worker (`EvalRunner`, behind `IEvalRunControl` for enqueue and cancel) rather than as a task on the task worker pool, so they never compete with or appear among user tasks; they still draw from the owner's daily cost cap.
- **Live run id in chat:** the turn stream's final event stays unchanged; after it, the web chat reads the conversation detail to learn the run id of the reply it just finished. Turn entries of the conversation detail carry the stored `rating`.

## Risks / Trade-offs

- [Eval cases keep personal conversation text beyond retention] → they exist only by explicit user action (rating with the toggle on, or admin save); listed and deletable; documented in the privacy section.
- [Replayed history differs from what the model saw (tool results in history)] → history keeps user and assistant text only, matching what the agent loop gets today.
- [Candidate makes extra harmless reads (e.g. `printer_status`) not recorded] → the case is `diverged`, which is honest; the report shows the extra call so the admin can judge.
- [Judge models are biased] → optional; tool comparison stays the primary signal.

## Migration Plan

EF migration adds the run rating columns, the preference column, `eval_cases`, `eval_runs`, `eval_results`. Runs recorded before this change have no turn context snapshot; saving them as cases marks them `incomplete`.
