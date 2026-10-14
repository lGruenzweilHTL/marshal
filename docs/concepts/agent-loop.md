# Agent loop

This page explains how Marshal runs one turn or one task run: the loop of model calls and tool calls, its limits, and how it behaves when something goes wrong.

## The problem

An LLM that can call tools needs a driver. The driver decides which tools the model sees, runs the calls it asks for, feeds results back and stops at the right moment. Off-the-shelf drivers often run tool calls automatically, inside the model client. That makes it hard to check each call before it runs, to record what happened, and to stop a model that loops.

Voice adds a second problem. A person standing in the kitchen will not wait 30 seconds for an answer.

## How it works

Every turn and every task run uses the same loop:

1. **Build the context.** The [context builder](context-building.md) assembles the system prompt: base rules, user profile, memory, time and locale, satellite context, skills index, area states and output rules.
2. **Call the model** with the conversation and the currently visible tools. The tool list is computed again before every model call by the [visibility filter](tool-visibility.md).
3. **Guard every requested call.** Each call goes through the guard, which decides `safe` (execute), `confirm` (freeze and ask) or deny. See [risk and confirmation](risk-and-confirmation.md).
4. **Execute allowed calls** and append their results. Results of tools with untrusted output are wrapped as data and taint the run. See [untrusted content](untrusted-content.md).
5. **Repeat** until the model returns text without tool calls, or a limit is reached.

Marshal never lets the model client invoke tools on its own. Every call passes through the guard and is recorded as a run step.

Once a call is frozen as a pending action, the model gets no further tools in that turn. A second call in the same round gets the error `confirmation.already_pending`. The model's job is now to ask the confirmation question.

### Model tier

The run's agent template can name a tier. Without one, voice turns use the `fast` tier and text turns the `standard` tier. If no model of that tier with tool support exists, the turn ends with a fixed "model unavailable" reply. See [`models`](../configuration/marshal-yaml/models.md).

### Reasoning

A model with `reasoning` set to `low`, `medium` or `high` is asked to reason at that effort in text turns and background task runs. Voice turns never ask for reasoning, because of the voice latency cap. In the web chat the reasoning streams as `reasoning` events, and each tool call as a `tool` event with a short summary of its arguments and result. Results of sensitive tools, such as mail and notes, are never summarized. See [conversation API](../api/conversation.md).

### Tool errors are results

A tool that fails in an expected way returns a failed `Result` with a stable code, for example `printer.offline`. The loop gives the model a structured error (`{"error": {"code": ..., "message": ...}}`) and continues. A tool that throws an exception is treated the same way, with the code `tool.failed`. Neither ends the turn. The model can explain the problem or try something else.

### Limits

| Limit | Default | Effect when reached |
|---|---|---|
| Steps (`agent.maxSteps`, or the template's `maxSteps`) | 8 tool rounds | No further tool runs; the turn ends with a short reply that the request could not be finished. |
| Voice latency cap (`agent.voiceLatencyCap`) | 8 s | The turn ends with a "working on it" reply and continues as a background task. |
| Text latency cap (`agent.textLatencyCap`) | 60 s | Same as voice. |
| Task budget (tokens, cost, steps, wall time) | from the task | The run stops and the task fails with `budget_exceeded`. |

See [`agent`](../configuration/marshal-yaml/agent.md) and [`tasks`](../configuration/marshal-yaml/tasks.md) for the keys.

### Moving to the background

When an interactive turn reaches its latency cap, Marshal speaks a short "working on it, I'll tell you when done" reply and ends the turn. The run continues as a background task owned by the same user, from its last completed tool round. The original run is recorded with outcome `backgrounded`. The task later delivers its result by voice to the originating satellite (voice turns) or to the web UI (text turns). The task records the session it came from, so its result also appears as a new reply in that conversation and counts as history for the session's next turn.

The model can do the same on purpose with `agent.move_to_background(statusMessage)`, which is visible only in interactive turns.

After the switch, the tool list is recomputed with background rules: the advisor becomes visible and confirm-tier calls go to the approval queue instead of a spoken question. The originating satellite's `denyTools` and `denyConfirm` keep applying.

### The advisor

`agent.ask_advisor(question)` sends only the question and an agent-written summary to an `advisor`-tier model, with no tools, and returns the advice as text. It is limited per run (default 2, `agent.advisor.maxCallsPerRun`, overridable per template) and by a daily advisor cost cap (`agent.advisor.dailyCostCap`, default 1). It is hidden in voice turns, when no advisor model is configured, and once either limit is reached.

### Subagents

`agent.delegate(template, task)` runs a nested loop with another template's prompt, tools, skills and tier, and returns its final text as the tool result. The nested loop uses the same guard, the caller's owner, satellite policy and taint, and passes its taint back to the caller. Subagents never run confirm-tier actions: they have no user to ask. Delegation depth is limited (default 3, `agent.maxDelegationDepth`). See [agent templates](../guides/agent-templates.md).

### Headless execution

The loop does not need an HTTP request. Owner, source, input mode, satellite, language and taint come from an explicit execution context. Task runs, approvals and tests all use the same loop with the same guard. This is also what lets tests run the loop against a fake `IChatClient`.

## What is recorded

Every run is recorded with its steps in order: the context build, each model call (model, tier, tokens, latency, time to first token, cost, reasoning), each tool call (tool id, arguments, result or reference, risk tier, guard decision, duration, error) and the reply (text, `continueConversation`). The run detail in the web UI shows this. Reasoning that follows a sensitive tool result in the same run is not stored. See [observability](../guides/observability.md) and [privacy](privacy.md) for what is kept and for how long.

## Trade-offs

- **Fast tier for voice.** Voice turns default to the cheapest, quickest model. Complex voice requests may hit the latency cap and move to the background rather than get a slower, better answer in place.
- **Step limit over cleverness.** A hard step limit stops runaway loops at the cost of sometimes cutting off a long but legitimate chain. Templates can raise it.
- **No tools after freezing.** Stopping tool use once a confirmation is pending keeps the conversation simple and the frozen action unambiguous, but the model cannot do other work in the same turn.

## Related

- [Tool visibility](tool-visibility.md)
- [Risk and confirmation](risk-and-confirmation.md)
- [Context building](context-building.md)
- [Conversation API](../api/conversation.md)
