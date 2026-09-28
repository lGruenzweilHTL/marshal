## ADDED Requirements

### Requirement: Runs and steps
Every turn and every headless loop execution SHALL be recorded as a run (source, owner, session, trigger, input mode, satellite, area, language, start / end, outcome `ok` | `error` | `backgrounded` | `cancelled`, taint flag and sources, cost) with ordered steps: context build, model calls (model, tier, input / output / cached tokens, latency, time to first token, cost), tool calls (tool id, args, truncated result, risk tier, guard decision, duration, error) and the reply (text, `continueConversation`).

#### Scenario: Turn with one tool call
- **WHEN** a turn makes two model calls and one tool call
- **THEN** the run has a context step, two model-call steps, one tool-call step and a reply step, in order

### Requirement: Recording behind IRunRecorder
All run recording SHALL go through `IRunRecorder`, which writes the domain tables and emits OpenTelemetry traces and metrics from the same data.

#### Scenario: Trace per run
- **WHEN** a run completes
- **THEN** one trace with a span per step is exported when OpenTelemetry export is configured

### Requirement: Redaction
The recorder SHALL apply a per-tool redaction policy. Results of sensitive tools SHALL be stored as references (message id, path) instead of content, on by default for tools flagged sensitive. Logs SHALL never contain secrets, message bodies, note contents or full prompts.

#### Scenario: Sensitive tool result
- **WHEN** a tool flagged sensitive returns content
- **THEN** the run step payload holds only the reference, not the content

### Requirement: Audit log
Actions outside a run (UI actions, approvals, token and secret changes, kill switch, CLI commands) SHALL be written to the audit log with actor kind (`user` | `api_token` | `system`), actor id, action, target, details and optional run link.

#### Scenario: CLI token creation audited
- **WHEN** an operator creates a token through the CLI
- **THEN** an audit event with action `token.create` and actor kind `system` is stored
