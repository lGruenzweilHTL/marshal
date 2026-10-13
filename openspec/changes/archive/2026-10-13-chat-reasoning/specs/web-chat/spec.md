# Spec Delta

## ADDED Requirements

### Requirement: Reasoning and tool steps
The chat SHALL show the `reasoning` events of a reply in a reasoning block above the reply text. The block SHALL be open while reasoning streams and SHALL collapse when the first `delta` of the reply arrives; the user SHALL be able to open and close it. The chat SHALL show `tool` events as a timeline of steps in the reply, one line per tool call with the tool's interface label (the tool id when no label exists), argument summary, state and duration, updated in place when the call ends. `status` events SHALL be shown in the same timeline. The "thinking" indicator SHALL be shown only until the first event of the turn arrives. Reasoning SHALL be rendered as plain text, never as markdown or HTML.

#### Scenario: Reasoning collapses
- **WHEN** a reply streams reasoning and then its first text delta
- **THEN** the reasoning block was open during reasoning and is collapsed once the text appears

#### Scenario: Tool step updates in place
- **WHEN** a `tool` event with state `running` is followed by the ending event with the same id and state `error`
- **THEN** the timeline shows one line for that call, now marked as failed

#### Scenario: Model without reasoning
- **WHEN** a turn produces no `reasoning` events
- **THEN** no reasoning block is shown

#### Scenario: Markup in reasoning
- **WHEN** reasoning text contains `![x](https://evil.example/a.png)`
- **THEN** it is shown as literal text and no request is made
