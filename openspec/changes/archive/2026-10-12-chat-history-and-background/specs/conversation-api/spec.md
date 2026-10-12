# Spec Delta

## MODIFIED Requirements

### Requirement: Latency cap
The system SHALL enforce a hard latency cap per turn (default ~8 s for voice, ~60 s for text, configurable). Reaching the cap SHALL speak a short "working on it, I'll tell you when done" reply, end the turn, and continue the run as a background task owned by the same user. The original run SHALL be recorded with outcome `backgrounded`, and the task SHALL deliver its result by voice to the originating satellite (voice turns) or to the web UI (text turns). The task SHALL record the originating session. Its result SHALL be added to that session as a background result, and SHALL be part of the session's conversation history for later turns.

#### Scenario: Long voice request
- **WHEN** a voice turn exceeds the voice cap
- **THEN** the final event says Marshal is working on it, the run is recorded as `backgrounded`, and the result later arrives through notifications

#### Scenario: Result joins the session
- **WHEN** a text turn in web session S is backgrounded and its task produces the result "The printer finished at 14:02"
- **THEN** session S lists that result after the backgrounded turn, and the Inbox also receives it

#### Scenario: Follow-up question after the result
- **WHEN** the user then asks in session S "and how long did it take?"
- **THEN** the model's conversation history for that turn contains the background result as an assistant message
