# Proposal

## Why

Every model call carries every visible tool: dozens of built-in tools plus all HA MCP tools and custom servers. Long tool lists cost tokens and lower tool-choice accuracy, especially on local models (RAG-MCP: 43% vs 14% correct selection with retrieval, over 50% fewer prompt tokens). Voice turns also feel slow: dynamic context sits early in the system prompt, so provider prompt caches are mostly lost, a slow tool leaves an audible silence, and nobody can see where a voice turn's time goes.

## What Changes

- **Tool retrieval by namespace.** A configured core set of namespaces (default `agent`, `memory`, `tasks`, `notifications`, `timers`, `places`) is always loaded. Other namespaces are loaded per session when the turn text matches their description (embedding match, reusing the turn embedding memory retrieval already computes), when a deterministic hint matches (e.g. the turn names an HA entity or area), or when the model calls the new safe tool `agent.find_tools(query)`. Loaded namespaces stay loaded for the session and are appended to the end of the tool list, so the list only grows and the cached prefix survives.
- Retrieval only narrows what the visibility filter already allows; it never makes a filtered tool visible, and `agent.find_tools` only reports namespaces that have visible tools. A call to a visible tool whose namespace is not loaded yet loads it and proceeds.
- Namespace descriptions: built-in tool sources declare one in code; MCP servers get an optional `description` in `/config/mcp/<id>.yaml`.
- Retrieval is skipped when the visible tool list is already small (templates with narrow allowlists).
- **Cache-friendly context.** Context contributors declare a stability (`static`, `session`, `turn`); the builder orders by stability first, so stable text (base prompt, template, profile, skill and delegation indexes, output mode) comes before memory, area, task and time.
- **Spoken filler.** Built-in tools can declare a short filler line per language. In a voice turn, before a tool whose expected duration exceeds a threshold, Marshal sends that line as a `delta`, at most once per turn and only when nothing was spoken yet.
- **Latency budget.** Runs record the time to first delta. The run detail of a voice run shows a timeline (context, model calls, tools, first delta, end), and the runs list can sort by time to first delta.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tool-registry`: namespace descriptions, tool retrieval and session-loaded namespaces, `agent.find_tools`, filler lines on descriptors, deterministic tool order.
- `context-builder`: contributors declare stability; ordering by stability then order.
- `conversation-api`: spoken filler as a `delta` in voice turns.
- `run-recording`: loaded namespaces per run and time to first delta.
- `run-insights`: voice latency timeline and time-to-first-delta sorting.

## Non-goals

- A deterministic fast path that answers without a model call.
- Provider-specific tool search (Anthropic `defer_loading`); one mechanism for all providers.
- Fillers for MCP tools.
- Unloading namespaces within a session.
- Reordering messages or truncating history for caching.

## Impact

- `src/Marshal.Abstractions`: `ToolDescriptor` gains filler lines; tool sources expose namespace descriptions; `IContextContributor` gains a stability; new `IToolNamespaceHints`.
- `src/Marshal.Core`: tool selection after the visibility filter, `agent.find_tools`, filler emission in the agent loop, context builder ordering, contributors' stability values.
- `src/Marshal.Data`: session column for loaded namespaces, run column for time to first delta; migration; `erd.puml` update.
- `src/Marshal.Integrations.HomeAssistant`: namespace hint for entity and area names.
- Every built-in tool source: namespace description; slow tools: filler lines.
- `web/`: runs list column and voice timeline in the run detail.
- Config: `tools.core`, `tools.retrieval.*`, `voice.fillerThresholdMs`, MCP `description`.
