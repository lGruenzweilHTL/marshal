# Design

## Context

See proposal.md (Why). Today `McpServerFile.Untrusted` flows into `McpServerDefinition`, and `McpToolSource.Describe` sets `UntrustedOutput` and `TaintKind = mcp:<id>` on every tool of the server. Hot reload restarts a server only when `McpToolSources.SameDefinition` reports a difference. Unknown YAML keys are errors, so the new key needs to be added to the file model. The config endpoint returns `Untrusted` per server and the SPA config view shows it in a column.

HA's to-do read tool is reported as `todo__get_items` on current HA (`components/todo/llm.py`); older releases named it differently, which is why the key takes globs and a non-matching glob is a visible warning.

## Goals / Non-Goals

**Goals:**
- Trust is decided per tool at discovery time, from config, with no change to the guard or taint tracking.

**Non-Goals:**
- Re-checking globs on every call; tools are described once per discovery.

## Decisions

### 1. Globs on the reported tool name

`untrustedTools` matches the name as the server reports it (`todo__get_items`), not the internal id (`ha.todo__get_items`) or the model-facing name. The name is stable across `toolPrefix` changes and is what the server's docs show. Glob syntax: `*` matches any text, case-sensitive like tool names. The glob helper used by risk rules is reused if it fits; otherwise a small shared one.

Alternative: internal ids (`ha.todo__get_items`) like risk rules — consistent with `risk.yaml`, but redundant inside a file that already names the server.

### 2. Marking happens in `Describe`

`McpServerDefinition` gains `UntrustedTools` (empty list by default). `Describe` sets `UntrustedOutput` when `Untrusted` is true or any glob matches; `TaintKind` stays `mcp:<id>`. `SameDefinition` compares the list, so a change restarts the server's source and the next turn sees new descriptors (existing hot-reload rule: no change mid-turn).

### 3. Validation and warnings

`YamlMcpServerCatalog` rejects `untrusted: true` with a non-empty `untrustedTools` (config error naming the file and both keys), and rejects empty or whitespace globs. Unmatched globs are only known after discovery, so `McpToolSource` records them; the server's state gains a warnings list that the config API and `/health` surface. A server with warnings is still `up`.

New interfaces: none.

### 4. API and view

The MCP server response gains `untrustedTools: string[]` and `warnings: string[]` (additive, v1). The view's "untrusted" column shows "all", "some (n)" with the globs on hover, or "no"; warnings appear next to the server like the existing reload errors.

## Risks / Trade-offs

- [HA renames `todo__get_items` again] → the glob stops matching and list reads become trusted silently, except for the warning. The warning on `/health` is the mitigation; the example uses an exact name so the warning fires.
- [A broad glob such as `*` makes the whole server untrusted] → allowed; equivalent to `untrusted: true`, harmless.

## Migration Plan

None. Existing files without the key behave exactly as before.
