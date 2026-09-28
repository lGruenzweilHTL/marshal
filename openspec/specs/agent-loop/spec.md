# agent-loop Specification

## Purpose
TBD - created by archiving change core-agent. Update Purpose after archive.
## Requirements
### Requirement: Agent loop
The system SHALL run each turn as a loop: build context, call the model with the visible tools, pass every requested tool call through the guard, execute allowed calls, append results, and repeat until the model returns a final reply or the step limit is reached.

#### Scenario: Tool call then reply
- **WHEN** the model first requests a safe tool call and then returns text
- **THEN** the tool is executed once, its result is given to the model, and the text becomes the reply

#### Scenario: Step limit
- **WHEN** the model keeps requesting tool calls beyond the configured maximum number of steps
- **THEN** the loop stops, no further tool is executed, and the turn ends with a short reply that the request could not be finished

### Requirement: Tool errors are results
A tool that fails with an expected error SHALL return a failed `Result`, which the loop SHALL pass to the model as a structured error message. The loop SHALL NOT end the turn because of an expected tool error.

#### Scenario: Tool reports an expected failure
- **WHEN** a tool returns a failed `Result` with code `printer.offline`
- **THEN** the model receives a tool result containing the code and message, and the loop continues

### Requirement: Advisor tool
The system SHALL provide the tool `agent.ask_advisor(question)` (safe tier). It SHALL send only the question and an agent-written context summary to an `advisor`-tier model, with no tools, and return the advice text. Advisor calls SHALL be limited per run (default 2, configurable per template) and by a separate daily advisor cost cap. The tool SHALL be hidden in voice turns, when no advisor model is configured, and when either limit is reached.

#### Scenario: Advisor in a text turn
- **WHEN** a text turn calls `agent.ask_advisor` for the first time
- **THEN** one advisor-tier call without tools is made and its text is returned as the tool result

#### Scenario: Hidden in voice turns
- **WHEN** the turn has `inputMode = voice`
- **THEN** `agent_ask_advisor` is not in the tool list sent to the model

#### Scenario: Per-run limit
- **WHEN** a run has already made 2 advisor calls with the default limit
- **THEN** the advisor tool is removed from the tool list for the rest of the run, and a third call is denied by the guard

### Requirement: Headless execution
The agent loop SHALL be usable without an HTTP request, with the owner, source and policy taken from an explicit execution context, so later task runs use the same loop and guard.

#### Scenario: Loop runs from a test harness
- **WHEN** a test runs the loop with a fake `IChatClient` and an execution context for a fake owner
- **THEN** the loop completes and records the run for that owner

