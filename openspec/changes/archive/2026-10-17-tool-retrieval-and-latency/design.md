# Design

## Context

See proposal.md (Why). Current state:

- `ToolVisibilityFilter` (`Marshal.Core/Tools`) removes forbidden, non-allowlisted, satellite-denied and conditionally hidden tools before each model call. The list order follows registration order.
- Memory retrieval embeds the turn text (with area name) once per turn and reuses it for project activation.
- Context contributors and their order values: base 0, template 50, profile 100, pinned memory 150, retrieved memory 160, time 200, skill index 250, delegation index 260, satellite 300, HA area 350, output mode 400, task 450. Memory and time change every turn but come before the stable indexes, so the cacheable prefix ends after the profile.
- `HaAreaContextContributor` already matches entity names mentioned in the turn text against the HA cache.
- The HA component speaks `delta` events only (`status` is dropped), so a spoken filler must be a `delta`.
- Providers cache prompt prefixes in the order tools → system → messages (Anthropic explicitly; llama.cpp/Ollama through KV-cache reuse of an identical prefix).

## Goals / Non-Goals

**Goals:**
- Fewer tools per call without losing the cached prefix within a session.
- Retrieval is an efficiency mechanism only. Security stays in the visibility filter and the guard.
- No extra model call for retrieval in the common case.

**Non-Goals:**
- Learning retrieval weights. Thresholds are configuration, tuned with the eval replay of the evals change.

## Decisions

### 1. Retrieval unit is the namespace

Namespaces already group tools (`printer.*`, `ha.*`, server ids for MCP). Loading a whole namespace keeps related tools together for multi-step work and limits list changes to a few per session. New: `IToolSource.Namespaces` returns `(namespace, description)` pairs; built-in sources declare descriptions in code, `McpToolSource` reads `description` from the server file or builds the fallback.

Alternative: single tools by embedding — smallest lists, but many list changes (cache loss) and misses in multi-step work.

### 2. Selection pipeline

```
all tools ─▶ visibility filter ─▶ visible set ──┐
                                                │  (≤ minTools or disabled → done)
session.loaded ∪ core ∪ hints ∪ top-k(retrieval) ─▶ loaded set
visible ∩ loaded ─▶ ordered list (core order, then load order, then name)
```

New interfaces:
- `IToolSelector` (`Marshal.Core`): `SelectAsync(visibleTools, ToolSelectionRequest) → ToolSelection` (ordered tools, newly loaded namespaces with reasons). v1: `NamespaceToolSelector`.
- `IToolNamespaceHints` (`Marshal.Abstractions`): `GetHintsAsync(turnText, satelliteContext) → namespaces`. v1: `HaNamespaceHints` (entity, alias or area name in the turn text → `ha`; reuses the area contributor's name matcher) and `MusicNamespaceHints` (a configured speaker name → `music`).
- `INamespaceEmbeddings` (`Marshal.Core`): cached description embeddings keyed by embedding model and description hash; rebuilt on tool-source reload. v1: `CachedNamespaceEmbeddings`.

The turn embedding comes from the memory retrieval step. Turn embedding and namespace embeddings use the same `IEmbeddingGenerator`; when no embedding generator is configured, only core, hints, `find_tools` and calls load namespaces.

Defaults: `tools.retrieval.threshold: 0.30` (cosine), `maxPerTurn: 3`, `minTools: 24`. The threshold depends on the embedding model; the eval replay is the tuning tool. The record of every load reason and score makes misses visible in the run detail.

### 3. Session state

`sessions.loaded_tool_namespaces` (JSON array, load order). Written after each turn. Task runs and AI tasks have their own sessions and start from core. Subagents start from core plus their template's allowlist; most templates fall under `minTools` and skip retrieval.

### 4. `agent.find_tools` and calls to unloaded namespaces

| Tool | Tier | Hidden when |
|---|---|---|
| `agent.find_tools(query)` | safe | retrieval disabled or skipped for the run; every namespace with visible tools already loaded |

`find_tools` embeds `query` and ranks only namespaces with visible tools, so it reveals nothing the filter hides. A call to a visible but unloaded tool (the model may know names from earlier sessions) loads the namespace (reason `call`) and goes through the guard. Retrieval is not a boundary, so refusing would only cost a round trip.

### 5. Context stability

`IContextContributor.Stability` (`Static`, `Session`, `Turn`). The builder sorts by (stability, order). Assignments: base, template, skill index, delegation index → `Static`; profile, output mode, satellite → `Session`; pinned memory → `Session` (changes only when the user pins); retrieved memory, time, HA area, task → `Turn`. The builder returns the system prompt as two text contents (stable part, turn part) so a provider adapter can set a cache breakpoint between them. Whether the Anthropic SDK's `IChatClient` honors cache control through `AdditionalProperties`, or caches automatically, is checked in the tasks; the breakpoint is an adapter detail in `Marshal.Api`.

Result (task 4.3, `Anthropic` 12.50): the SDK's `IChatClient` applies no prompt caching on its own. It honors a breakpoint per content block set with `AIContent.WithCacheControl(...)` (the SDK's `AIContentCacheExtensions`, stored on the content's additional properties). `AnthropicPromptCaching` in `Marshal.Api/Models` wraps the Anthropic chat client and marks a copy of the system message's first text content (the stable part) with an ephemeral breakpoint, so the cached prefix ends after the session sections. Ollama, llama.cpp and vLLM reuse an identical prompt prefix through their KV cache without markers; they get the two parts concatenated.

### 6. Filler

`ToolDescriptor.Fillers` (map language → line). `IToolLatencyStats` (`Marshal.Abstractions`): `ExpectedDuration(toolId) → TimeSpan?`. v1: `RecordedToolLatencyStats` reads the last 20 durations per tool from run steps, cached for 5 minutes. The agent runner checks, after the guard allows a call and before executing it: voice turn, no delta sent yet, no filler sent yet, filler in turn language, expected duration > threshold. Then it writes the filler to the event channel as a `delta` and appends it to the reply buffer.

Initial fillers: printer snapshot and inspect, mail search, RSS digest reads, notes search, artifacts render, media search, music search, calendar list over long ranges, infra and repos reads.

### 7. Latency recording

`ConversationService` stamps the request start. The runner stamps the first delta (filler or model text). The run gets `time_to_first_delta_ms`. The timeline in the run detail is built from existing step timestamps plus that field; no new step type.

## Risks / Trade-offs

- [Short German commands ("mach's heller") miss the `ha` namespace by embedding] → HA hints cover named entities and areas; `find_tools` or a direct call recovers the rest at one round trip. Measure with eval cases before tuning.
- [Embedding model unset] → retrieval falls back to core plus hints plus `find_tools`; documented.
- [A long session accumulates many namespaces] → acceptable: the list only grows toward today's full list.
- [Fillers sound repetitive] → at most one per turn, only for slow tools, phrased per tool.
- [Filler and later model text read oddly together] → the base prompt tells the model that a short "checking…" line may already have been spoken and not to repeat it.

## Migration Plan

EF migration adds `sessions.loaded_tool_namespaces` (nullable; null = core only) and `runs.time_to_first_delta_ms` (nullable). Retrieval is on by default; `tools.retrieval.enabled: false` restores today's behavior. Rollback: the columns are ignored by older versions.
