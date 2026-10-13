# Spec Delta

## MODIFIED Requirements

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
