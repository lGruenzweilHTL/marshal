# Spec Delta

## MODIFIED Requirements

### Requirement: Runs and steps
Every turn and every headless loop execution SHALL be recorded as a run (source, owner, session, trigger, input mode, satellite, area, language, start / end, outcome `ok` | `error` | `backgrounded` | `cancelled`, taint flag and sources, cost) with ordered steps: context build, model calls (model, tier, input / output / cached tokens, latency, time to first token, cost, reasoning text when the model returned any), tool calls (tool id, args, truncated result, risk tier, guard decision, duration, error) and the reply (text, `continueConversation`).

#### Scenario: Turn with one tool call
- **WHEN** a turn makes two model calls and one tool call
- **THEN** the run has a context step, two model-call steps, one tool-call step and a reply step, in order

#### Scenario: Reasoning recorded
- **WHEN** a model call returns reasoning text
- **THEN** its model-call step holds that reasoning

### Requirement: Redaction
The recorder SHALL apply a per-tool redaction policy. Results of sensitive tools SHALL be stored as references (message id, path) instead of content, on by default for tools flagged sensitive. Reasoning of a model call that follows a redacted tool result in the same run SHALL NOT be stored; the step SHALL record that reasoning was withheld. Logs SHALL never contain secrets, message bodies, note contents, reasoning or full prompts.

#### Scenario: Sensitive tool result
- **WHEN** a tool flagged sensitive returns content
- **THEN** the run step payload holds only the reference, not the content

#### Scenario: Reasoning after a mail read
- **WHEN** a model call with reasoning follows a redacted `mail_read` result in the same run
- **THEN** that model-call step stores no reasoning text and is marked as withheld
