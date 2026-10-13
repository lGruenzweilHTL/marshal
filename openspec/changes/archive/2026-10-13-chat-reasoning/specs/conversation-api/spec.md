# Spec Delta

## ADDED Requirements

### Requirement: Reasoning and tool events for web clients
For cookie principals, the turn stream SHALL also carry `reasoning` events (`{ text }`, chunks of the model's reasoning as they arrive) and `tool` events. A `tool` event SHALL be sent when a tool call starts (`{ id, tool, arguments, state: "running" }`) and when it ends (same `id`, `state` `ok` | `error` | `denied` | `pending`, `durationMs`, optional `result`). `tool` SHALL be the tool id (for example `printer.status`), `arguments` a short one-line summary of the arguments, and `result` a short one-line summary of the result. For tools flagged sensitive or redacted by the recorder, `result` SHALL be omitted. Token principals SHALL NOT receive `reasoning` or `tool` events; their stream SHALL stay unchanged.

#### Scenario: Web turn with a tool call
- **WHEN** a cookie user's turn calls `printer_status` and then replies
- **THEN** the stream carries a `tool` event with state `running`, then one with state `ok`, then the `delta` events of the reply

#### Scenario: Home Assistant client
- **WHEN** a token client's turn uses a model that returns reasoning and calls a tool
- **THEN** the stream carries no `reasoning` and no `tool` events

#### Scenario: Sensitive tool
- **WHEN** a web turn calls a mail tool flagged sensitive
- **THEN** its ending `tool` event has no `result`

#### Scenario: Tool waits for confirmation
- **WHEN** a web turn's tool call is frozen as a pending action
- **THEN** the ending `tool` event has state `pending`
