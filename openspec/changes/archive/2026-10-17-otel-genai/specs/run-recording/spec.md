## MODIFIED Requirements

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

## ADDED Requirements

### Requirement: Telemetry content capture
With `telemetry.captureContent: true`, spans SHALL additionally carry the user input on the `invoke_agent` span, tool arguments and results on `execute_tool` spans, and the reply on the `invoke_agent` span, in exactly the form run recording stores them (truncated; sensitive results as references; withheld reasoning not included). Spans SHALL never carry system prompts or full model input messages. Startup SHALL log a warning while content capture is on.

#### Scenario: Sensitive result with capture on
- **WHEN** content capture is on and `mail_read` returns a body
- **THEN** its `execute_tool` span carries the reference, not the body

#### Scenario: No prompts
- **WHEN** content capture is on
- **THEN** no span carries the system prompt or the model's full input messages
