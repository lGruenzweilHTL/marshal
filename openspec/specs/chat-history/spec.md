# chat-history Specification

## Purpose
Lets the user see earlier conversations with Marshal, typed and spoken, in the web UI, including results of work that moved to the background.

## Requirements

### Requirement: Conversation list
`GET /api/v1/conversations` SHALL return the current user's sessions with source `web`, `voice` or `api`, ordered by last turn time, newest first, paged by a cursor. Sessions with source `task` or `automation` SHALL NOT be listed. Each entry SHALL carry the session id, source, created and last-turn times, the number of turns, a title (the first user input, cut to 80 characters), whether the conversation can be continued (`web` only), and for voice sessions the satellite and area of the first turn. The endpoint SHALL require a human principal and SHALL only return sessions of the current owner.

#### Scenario: Newest first
- **WHEN** the user has a web session last used yesterday and a voice session last used an hour ago
- **THEN** the voice session is listed first

#### Scenario: Task sessions hidden
- **WHEN** a scheduled task run created a session with source `task`
- **THEN** that session is not in the list

#### Scenario: Automation sessions hidden
- **WHEN** an AI task created a session with source `automation`
- **THEN** that session is not in the list

#### Scenario: Other owner
- **WHEN** owner B has sessions and owner A requests the list
- **THEN** none of B's sessions are returned

#### Scenario: Token principal
- **WHEN** a machine token calls `GET /api/v1/conversations`
- **THEN** the response is 403

### Requirement: Conversation detail
`GET /api/v1/conversations/{id}` SHALL return the session's entries in time order. A turn entry SHALL carry the run id, start time, input mode, satellite, area, the user input, the full reply text, whether the reply asked a question back, the artifacts the turn showed, the run outcome, the reasoning of its model calls (or a withheld marker), and its tool steps (tool id, argument summary, state, duration, result summary except for sensitive tools). A background result entry SHALL carry the task id, the time the result was ready and the result body. Turns without a recorded reply (errors, cancellations) SHALL be listed with their outcome and no reply. A session of another owner or an unknown id SHALL return 404.

#### Scenario: Mixed input modes
- **WHEN** a session holds a voice turn from satellite `kitchen` followed by a text turn
- **THEN** the detail lists both turns in order, the first with input mode `voice` and satellite `kitchen`, the second with input mode `text`

#### Scenario: Long reply
- **WHEN** a turn's reply is longer than the recorder's content limit for tool results
- **THEN** the detail returns the whole reply

#### Scenario: Artifacts kept
- **WHEN** a turn showed an image artifact
- **THEN** the turn entry lists that artifact with id, name, kind and content URL

#### Scenario: Background result in place
- **WHEN** a turn of the session moved to the background and its task produced a result
- **THEN** the detail lists a background result entry after that turn, with the task id and the result body

#### Scenario: Reopened chat shows reasoning
- **WHEN** a web turn recorded reasoning and two tool calls, and the conversation is opened again later
- **THEN** the turn entry carries the reasoning and both tool steps, and the chat shows them collapsed

### Requirement: History in the web UI
The web UI SHALL show the conversation list as part of the chat destination, as a side list next to the chat on desktop and as its own screen on phones. Each entry SHALL show the title and the last-turn time. Voice entries SHALL be marked with a microphone and the satellite or area name. API entries SHALL be marked as API. Opening an entry SHALL show the conversation with its turns, each voice turn marked as spoken. Voice and API conversations SHALL be read-only: the composer SHALL be replaced by a note and a "new chat" action. A conversation SHALL have its own URL (`/chat/{id}`) so it survives a reload.

#### Scenario: Voice turn marked
- **WHEN** the user opens a voice conversation from satellite `kitchen`
- **THEN** each turn shows a microphone mark and `kitchen`, and there is no composer

#### Scenario: Reload keeps the chat
- **WHEN** the user reloads the browser on `/chat/{id}` of a web conversation
- **THEN** the conversation is shown again and the next message continues the same session

#### Scenario: History on a phone
- **WHEN** the chat destination is opened at 375 px width
- **THEN** the current chat is shown and the conversation list is reachable in one tap

### Requirement: Background results in the open chat
When a background task created by a handoff produces its result, the system SHALL publish a `conversation.result` event on the live topic `conversations` with the session id, task id, time and body. An open chat showing that session SHALL add the result as a reply marked as a background result. After the live stream reconnects, the chat SHALL load the session again so that a result published during the gap is shown.

#### Scenario: Result arrives while the chat is open
- **WHEN** a text turn moved to the background and its task finishes while the user keeps the chat open
- **THEN** the chat shows the task's result as a new reply below the "working on it" reply, without a reload

#### Scenario: Result while the stream was down
- **WHEN** the result is published while the live stream is reconnecting
- **THEN** after the reconnect the chat shows the result

#### Scenario: Other session open
- **WHEN** the result belongs to session S1 and the chat shows session S2
- **THEN** S2 does not change and S1 shows the result when it is opened
