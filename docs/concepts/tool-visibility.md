---
icon: fa-solid fa-eye
description: "Why Marshal hides tools from the model instead of refusing them, and which rules decide what the model sees."
---

# Tool visibility

This page explains why Marshal hides tools from the model instead of refusing them, and which rules decide what the model sees.

## The problem

The usual way to stop an agent from doing something is to refuse the call after the model makes it. That has costs:

- The model wastes steps and latency on calls that can never succeed.
- Refusals invite the model to argue, retry with different arguments or look for a workaround.
- Every tool in the list costs prompt tokens and attention, and small local models choose worse from long lists.
- A tool the model can see is a target for prompt injection, even if it is refused later.

## The rule

**The model can't use it → the model can't see it.** Forbidden, denied or unavailable tools are filtered out of the tool list before every model call. The guard enforces the same rules again, server-side, on every call, as a second line.

## Tool ids and names

Every tool has an internal id `<namespace>.<name>`, used in risk rules, satellite policies, template allowlists, run records and the audit log. The model sees a model-facing name `<namespace>_<name>`. The registry maps both ways: the model calls `agent_ask_advisor`, Marshal resolves `agent.ask_advisor`.

Namespaces name capabilities, never providers. These namespaces are reserved for built-in tools: `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music` and `system`. An MCP server configured with id `mail` or `music` is rejected.

External tool names are sanitized: characters outside `[a-zA-Z0-9_-]` become `_`, and names longer than 64 characters are shortened with a hash suffix. Two tools that map to the same model-facing name cause the later tool source to be rejected. See [tools reference](../reference/tools.md).

## The visibility filter

Before every model call, Marshal removes a tool from the list when any of these apply, in this order:

| Rule | Example |
|---|---|
| Not in the active template's `tools` allowlist | A template with `tools: [printer.*, agent.load_skill]` sees only printer tools and skill loading. |
| Forbidden by a risk rule for every call | A `risk.yaml` rule without conditions marks `ha.intent__HassClimateSetTemperature` as `forbidden`. |
| Denied for the satellite (`denyTools`) | The kids' room satellite has `denyTools: [mail.*]`. |
| Confirm-only inside a subagent | A subagent has no user to ask, so tools that always need confirmation are hidden. |
| Confirm-only on a satellite with `denyConfirm` for it | A hallway satellite with `denyConfirm: [printer.*]` sees no confirm-only printer tools. |
| The tool's own visibility condition fails | `agent.ask_advisor` in voice turns; `mail.list_labels` when no account supports labels; tools of an MCP server that is down; `system.health` without warnings. |
| The MCP tool waits for approval | A tool that is new or changed after its server's first discovery, until an admin approves it ([tool pinning](../configuration/mcp-servers.md#tool-pinning)). The guard denies calls to it as well. |

"Confirm-only" includes taint escalation. In a tainted run, a tool flagged `persists` that would be `safe` counts as `confirm`, so it disappears in subagents and on satellites that deny confirmation for it.

### Arg-aware tools stay visible

Some tools are `safe` for some arguments and `confirm` for others. Turning on a light is safe, unlocking a door needs confirmation, and both go through the same Home Assistant tool. Hiding such a tool would also hide its safe uses. These tools stay visible, and the guard decides per call: a call that resolves to `confirm` on a satellite with `denyConfirm` is denied with "not allowed from this device", and no pending action is created.

## Tool retrieval

A long tool list costs tokens on every model call and makes it harder for a model, local models especially, to pick the right tool. After the visibility filter, Marshal therefore offers only the tools of the namespaces a session has loaded:

- **Core namespaces** (`tools.core`, by default `agent`, `memory`, `tasks`, `notifications`, `timers`, `places`) are always loaded.
- **Retrieval.** Before the first model call of a turn, Marshal compares the turn's embedding (the one memory retrieval already computes) with a one-line description of every other namespace and loads the best matches above `tools.retrieval.threshold`, at most `tools.retrieval.maxPerTurn`. Built-in tools describe their namespaces in code; an MCP server can set `description` in its file.
- **Hints.** Deterministic matches load a namespace without an embedding: a Home Assistant entity, alias or area named in the turn loads `ha`, a configured speaker name loads `music`.
- **`agent.find_tools(query)`.** When the model misses a tool, it can ask for it. The tool loads the best matching namespaces among those with visible tools; the found tools are offered from the next model call.
- **Direct calls.** A call to a visible tool of a namespace that is not loaded yet loads it and goes through the guard as usual.

Loaded namespaces stay loaded for the rest of the session. New tools are appended to the end of the list (core namespaces first, then the load order, tools by name), so the cached prompt prefix of the provider survives. Retrieval is skipped, and every visible tool offered, when `tools.retrieval.enabled` is `false` or no more than `tools.retrieval.minTools` tools are visible, for example with a narrow template.

Retrieval is an efficiency mechanism, not a boundary. It never offers a tool the filter removed, and `agent.find_tools` only names namespaces with visible tools, so it cannot reveal a tool that a satellite or template hides. The run detail lists every loaded namespace with its reason and score. See [`tools`](../configuration/marshal-yaml/tools.md).

### Spoken filler

A voice turn is silent while a tool runs. Slow built-in tools (printer snapshots, mail and notes search, news, calendar, media, infrastructure and GitHub reads) carry a short line per language, such as "Let me look at the printer.". When a voice turn is about to run such a tool, nothing has been spoken yet, and the tool usually takes longer than `voice.fillerThresholdMs` (the median of its last 20 calls), Marshal speaks that line first. At most one line per turn; text turns never get one.

## The guard as second line

The guard checks every call again, in this order: the tool is known and was offered in this round; it is still visible; the arguments are valid; risk rules, taint escalation, subagent and satellite policy. Each stage can only raise the tier, never lower it.

The second check matters because things change between building the list and making the call: a rule in `risk.yaml` can be edited mid-turn, or an argument can resolve to a lock. A call to a name that is not in the current list is denied, nothing runs, and the model receives an error result.

## External tools

MCP servers and skills add tools next to the built-in ones. Each source registers and unregisters as a unit when its file changes. A run keeps the tool set it started with; changes apply from the next turn. External tools that no risk rule matches default to `confirm`, so a new MCP server cannot run anything without your approval until you write rules for it. See [custom MCP servers](../guides/integrations/custom-mcp-servers.md) and [risk rules](../guides/risk-rules.md).

## Capabilities that do not exist

Some capabilities are not hidden; they are absent. There is no tool and no code path to send mail, delete or move messages, send raw G-code, restart printer firmware, delete printer files, or fetch a caller-chosen URL. A missing tool cannot be enabled by a rule change, a template or a prompt. See [security model](security-model.md).

## Trade-offs

- **A smaller list is a weaker model in some cases.** If you hide too much, the model cannot help. Template allowlists and satellite policies are explicit so you can see what each context gets.
- **Two places enforce the same rules.** The filter and the guard share one implementation of the checks, but they run twice per call. The cost is small next to a model call.

## Related

::: cards
- [Risk and confirmation](risk-and-confirmation.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
- [`satellites.yaml`](../configuration/satellites-yaml.md)
- [Templates format](../configuration/templates-format.md)
:::
