# Spec Delta

## MODIFIED Requirements

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
