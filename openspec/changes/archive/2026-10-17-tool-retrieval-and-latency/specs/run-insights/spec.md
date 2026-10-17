## MODIFIED Requirements

### Requirement: Runs list and detail
The runs view SHALL list the owner's runs (voice, web, task, api, automation) with filters by source, outcome and cost, and SHALL show and sort by time to first delta. The run detail SHALL show the context build (including the loaded tool namespaces and their reasons), model calls (tokens, latency, time to first token, cost, reasoning collapsed by default or a withheld note), tool calls (args, truncated or redacted result, risk tier, guard decision, confirmation reasons, taint sources) and the reply. For voice runs the detail SHALL show a timeline from request to end with the context build, each model call (with time to first token), each tool call, the first delta and the end.

#### Scenario: Guard decision visible
- **WHEN** a run contains a tool call escalated by taint
- **THEN** the detail shows guard decision `escalated_by_taint` and the taint source

#### Scenario: Redacted result
- **WHEN** a tool result was stored as a reference
- **THEN** the detail shows the reference, not content

#### Scenario: Reasoning in the run detail
- **WHEN** a model-call step holds reasoning
- **THEN** the detail shows it collapsed under that model call, and expands it on request

#### Scenario: Voice timeline
- **WHEN** a voice run spent 300 ms building context, 1.2 s in a model call and 2 s in a tool, and sent its first delta after 1.6 s
- **THEN** the detail shows these segments on one timeline with the first delta marked

#### Scenario: Slowest voice turns
- **WHEN** the user sorts the runs list by time to first delta, descending
- **THEN** the voice run with the longest time to first delta is listed first
