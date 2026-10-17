# Proposal

## Why

Today, any untrusted text a run reads (mail, RSS, notes, later web results) enters the main model's context and taints the run. The guard then escalates memory and task writes, but the model has still read text an attacker may have written. Many questions about such content only need a typed fact ("is this a parcel notice?", "which date?", "how much?"). Background tasks also run with their template's full tool allowlist on every run, even when the task only ever needs two namespaces, so a task that reads injected content later has more tools than it needs. The CaMeL work separates a tool-less "quarantined" model for untrusted data, and Progent freezes per-task tool privileges, which got AgentDojo attack success to 0%.

## What Changes

- **Quarantined extractor:** an internal building block that sends untrusted content plus a question to a model with no tools and a fixed answer schema, and validates the answer. Typed answers (`boolean`, `number`, `integer`, `date`, `datetime`, `enum` of caller-given values) do not taint the run; `text` answers do.
- **`agent.extract(ref, question, answerType, values?)`** (safe): lets the main model ask about an untrusted item by reference (a mail message id, an RSS article id, a note path) without reading it. The content is resolved server-side and given only to the extractor.
- **Per-task tool policies:** each agent task stores a tool policy (tool id globs) frozen at creation. Default: the template's allowlist narrowed to the core namespaces plus the namespaces loaded in the creating session; `tasks.create` and `tasks.watch` accept an optional `tools` list that can only narrow the template's allowlist. Every run of the task sees and may call only tools inside the policy (visibility filter and guard). Only a user action in the web UI changes a policy; `tasks.update` cannot.
- The task detail and the confirmation text for task creation show the policy.

## Capabilities

### New Capabilities

- `quarantined-extraction`: the extractor, answer types and taint rule, content references and `agent.extract`.

### Modified Capabilities

- `task-system`: tool policy on durable tasks; `tools` argument on task creation; `tasks.update` cannot change it.
- `tool-registry`: the visibility filter also removes tools outside a task run's policy.
- `background-views`: task detail shows and edits the tool policy.

## Non-goals

- Full CaMeL (a planner that writes programs, data-flow tracking through an interpreter).
- Untainting free-text answers.
- Argument constraints in task policies (e.g. "printer = voron only").
- Changing how existing tools return untrusted content to the main model.
- Parcel or deadline extraction features; they can be built on the extractor later.

## Impact

- `src/Marshal.Core`: extractor, answer schema validation (reusing the AI task change's validator), `agent.extract`, content resolvers, policy check in the visibility filter and guard.
- `src/Marshal.Integrations.Mail`, `.FreshRss`, `.Notes`: untrusted content resolvers.
- `src/Marshal.Tasks`: policy derivation on creation, storage, enforcement in task runs, API for edits.
- `src/Marshal.Data`: task column `tool_policy`; migration; `erd.puml`.
- `web/`: policy display and edit in the task detail.
- Depends on `tool-retrieval-and-latency` (loaded namespaces of a session) and `ha-ai-task` (JSON Schema validator).
