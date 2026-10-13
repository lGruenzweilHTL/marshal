# Spec Delta

## MODIFIED Requirements

### Requirement: Runs list and detail
The runs view SHALL list the owner's runs (voice, web, task, api) with filters by source, outcome and cost. The run detail SHALL show the context build, model calls (tokens, latency, time to first token, cost, reasoning collapsed by default or a withheld note), tool calls (args, truncated or redacted result, risk tier, guard decision, confirmation reasons, taint sources) and the reply.

#### Scenario: Guard decision visible
- **WHEN** a run contains a tool call escalated by taint
- **THEN** the detail shows guard decision `escalated_by_taint` and the taint source

#### Scenario: Redacted result
- **WHEN** a tool result was stored as a reference
- **THEN** the detail shows the reference, not content

#### Scenario: Reasoning in the run detail
- **WHEN** a model-call step holds reasoning
- **THEN** the detail shows it collapsed under that model call, and expands it on request
