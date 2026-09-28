## ADDED Requirements

### Requirement: Follow-up records
When a voice delivery or question expects a reply, the system SHALL call `assist_satellite.start_conversation` and store a follow-up record (owner, nonce, satellite, target: pending action or task, expiry) and pass `marshal-followup:<nonce>` as `extra_system_prompt`.

#### Scenario: Start conversation
- **WHEN** a delivery with `followUp: true` is delivered by voice
- **THEN** `start_conversation` is called with `extra_system_prompt = marshal-followup:<nonce>` and a follow-up record exists

### Requirement: Valid followupRef links the reply
A turn with a `followupRef` that is known, unexpired, unconsumed and from the same satellite SHALL create a new session linked to the follow-up's target and consume the record. Invalid references SHALL be ignored (normal turn).

#### Scenario: Different satellite
- **WHEN** a turn carries a valid nonce but comes from another satellite
- **THEN** the reference is ignored and the turn is handled normally

### Requirement: Three follow-up targets
- **Pending action:** the reply SHALL enter the normal confirmation flow (keyword or judge); the reference only identifies the question.
- **Waiting task:** the reply SHALL become the task's trigger payload and the task SHALL resume.
- **Delivered result:** the new session SHALL be seeded with the result's `speech` as an earlier assistant message, `body` as context, and a reference to the task; the reply SHALL be a normal turn in that session.

#### Scenario: Tell me more
- **WHEN** after the briefing the user says "tell me more about the second article"
- **THEN** the turn runs in a session seeded with the briefing result, without re-running the briefing task

#### Scenario: Approval by voice
- **WHEN** an approval question is asked by voice and the user answers "bestätigt"
- **THEN** the frozen action executes through the keyword path

### Requirement: No reply
If no reply arrives within the satellite's listening window, nothing SHALL happen and the record SHALL expire unconsumed.

#### Scenario: Silence
- **WHEN** the user does not answer
- **THEN** the follow-up record expires and the pending action or task is unchanged

### Requirement: Fallback when extra_system_prompt is unavailable
If `extra_system_prompt` does not reach custom conversation agents, the system SHALL instead link the first turn from the same satellite within a short reply window to the follow-up.

#### Scenario: Reply window
- **WHEN** fallback mode is active and a turn arrives from the same satellite within the window
- **THEN** it is linked to the follow-up target
