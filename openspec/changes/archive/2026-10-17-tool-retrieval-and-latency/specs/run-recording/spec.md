## MODIFIED Requirements

### Requirement: Runs and steps
Every turn and every headless loop execution SHALL be recorded as a run (source, owner, session, trigger, input mode, satellite, area, language, start / end, time to first delta when a delta was sent, outcome `ok` | `error` | `backgrounded` | `cancelled`, taint flag and sources, cost) with ordered steps: context build (including the loaded tool namespaces and why each was loaded: `core`, `retrieval` with its score, `hint`, `find_tools`, `call` or `session`), model calls (model, tier, input / output / cached tokens, latency, time to first token, cost, reasoning text when the model returned any), tool calls (tool id, args, truncated result, risk tier, guard decision, duration, error) and the reply (text, `continueConversation`).

#### Scenario: Turn with one tool call
- **WHEN** a turn makes two model calls and one tool call
- **THEN** the run has a context step, two model-call steps, one tool-call step and a reply step, in order

#### Scenario: Reasoning recorded
- **WHEN** a model call returns reasoning text
- **THEN** its model-call step holds that reasoning

#### Scenario: Loaded namespaces recorded
- **WHEN** a turn loads `printer` by retrieval with score 0.71
- **THEN** the context step lists `printer` with reason `retrieval` and score 0.71 next to the core namespaces

#### Scenario: Time to first delta
- **WHEN** a voice turn's first delta is sent 900 ms after the request arrived
- **THEN** the run records a time to first delta of 900 ms
