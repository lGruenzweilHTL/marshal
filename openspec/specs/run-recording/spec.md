# run-recording Specification

## Purpose
Recording what every run did: runs and steps behind IRunRecorder, redaction and the audit log.

## Requirements

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

### Requirement: Recording behind IRunRecorder
All run recording SHALL go through `IRunRecorder`, which writes the domain tables and emits OpenTelemetry metrics from the same data. Traces SHALL be emitted while the run executes and SHALL follow the OpenTelemetry GenAI semantic conventions in the version the system declares: one `invoke_agent` span per run, a child `chat` span per model call (including the structured-output and summarizer calls of the run), a child `embeddings` span per embedding call and a child `execute_tool` span per tool call. Spans SHALL carry the convention attributes for operation, provider, request and response model, input and output token usage, agent name (the template), conversation id (the session id) and, on tool spans, tool name and call id, plus the Marshal attributes run id, source, input mode, trigger, outcome, taint flag and, on tool spans, risk tier and guard decision. Without `telemetry.captureContent`, spans SHALL NOT carry user input, tool arguments, tool results, reasoning, replies or prompts.

#### Scenario: Trace per run
- **WHEN** a run makes two model calls and one tool call and OpenTelemetry export is configured
- **THEN** one trace exists with an `invoke_agent` span that has two `chat` children and one `execute_tool` child

#### Scenario: Convention attributes
- **WHEN** a model call uses 1,200 input and 80 output tokens
- **THEN** its `chat` span carries the convention's input and output token usage attributes with 1,200 and 80

#### Scenario: No content by default
- **WHEN** content capture is off and a run reads mail
- **THEN** no span carries the user input, the tool arguments, the mail result or the reply

### Requirement: Redaction
The recorder SHALL apply a per-tool redaction policy. Results of sensitive tools SHALL be stored as references (message id, path) instead of content, on by default for tools flagged sensitive. Reasoning of a model call that follows a redacted tool result in the same run SHALL NOT be stored; the step SHALL record that reasoning was withheld. Logs SHALL never contain secrets, message bodies, note contents, reasoning or full prompts.

#### Scenario: Sensitive tool result
- **WHEN** a tool flagged sensitive returns content
- **THEN** the run step payload holds only the reference, not the content

#### Scenario: Reasoning after a mail read
- **WHEN** a model call with reasoning follows a redacted `mail_read` result in the same run
- **THEN** that model-call step stores no reasoning text and is marked as withheld

### Requirement: Audit log
Actions outside a run (UI actions, approvals, token and secret changes, kill switch, CLI commands) SHALL be written to the audit log with actor kind (`user` | `api_token` | `system`), actor id, action, target, details and optional run link.

#### Scenario: CLI token creation audited
- **WHEN** an operator creates a token through the CLI
- **THEN** an audit event with action `token.create` and actor kind `system` is stored

### Requirement: Telemetry content capture
With `telemetry.captureContent: true`, spans SHALL additionally carry the user input on the `invoke_agent` span, tool arguments and results on `execute_tool` spans, and the reply on the `invoke_agent` span, in exactly the form run recording stores them (truncated; sensitive results as references; withheld reasoning not included). Spans SHALL never carry system prompts or full model input messages. Startup SHALL log a warning while content capture is on.

#### Scenario: Sensitive result with capture on
- **WHEN** content capture is on and `mail_read` returns a body
- **THEN** its `execute_tool` span carries the reference, not the body

#### Scenario: No prompts
- **WHEN** content capture is on
- **THEN** no span carries the system prompt or the model's full input messages

### Requirement: Turn context snapshot
The context step of every run SHALL store the text of the run's turn-level context contributions (for example retrieved memory, area context, time, task section) separately from the static and session parts, so that the turn can be rebuilt for replay. The snapshot SHALL be removed with the run by the retention purge.

#### Scenario: Snapshot recorded
- **WHEN** a turn's context contains two retrieved memory chunks and the area context
- **THEN** its context step stores their text as the turn-level snapshot
