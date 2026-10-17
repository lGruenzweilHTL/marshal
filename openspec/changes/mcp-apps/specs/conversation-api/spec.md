## MODIFIED Requirements

### Requirement: Streaming response
The response SHALL be `text/event-stream` with `delta` events (text chunks), optional `status` events, optional `artifact` events (`{ id, name, kind, url }`, one per artifact the turn shows), optional `widget` events (`{ kind, toolCallId, data }`) and `app` events (`{ appId, server, tool, toolCallId }`) for web turns, and one final event `{ speech, continueConversation, conversationId }`. `conversationId` SHALL echo the client's id for token clients. `continueConversation` SHALL be true when Marshal asks the user something (confirmation, clarification).

#### Scenario: Confirmation question keeps listening
- **WHEN** the turn ends with a pending action
- **THEN** the final event has `continueConversation = true`

#### Scenario: Id echo
- **WHEN** a token client sends `conversationId = "01JABC"`
- **THEN** the final event carries `conversationId = "01JABC"`, not Marshal's session id

#### Scenario: Artifact shown
- **WHEN** a web turn calls `artifacts_show` for artifact `a1`
- **THEN** the stream carries an `artifact` event with `a1`'s id, name, kind and content URL before the final event

#### Scenario: Widget event
- **WHEN** a web turn calls `printer_status`
- **THEN** the stream carries a `widget` event of kind `printer` before the final event

#### Scenario: No widgets for voice
- **WHEN** a voice turn calls `printer_status`
- **THEN** the stream carries no `widget` event
