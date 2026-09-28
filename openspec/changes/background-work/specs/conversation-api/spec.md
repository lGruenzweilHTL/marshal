## MODIFIED Requirements

### Requirement: Latency cap
The system SHALL enforce a hard latency cap per turn (default ~8 s for voice, ~60 s for text, configurable). Reaching the cap SHALL speak a short "working on it, I'll tell you when done" reply, end the turn, and continue the run as a background task owned by the same user. The original run SHALL be recorded with outcome `backgrounded`, and the task SHALL deliver its result by voice to the originating satellite (voice turns) or to the web UI (text turns).

#### Scenario: Long voice request
- **WHEN** a voice turn exceeds the voice cap
- **THEN** the final event says Marshal is working on it, the run is recorded as `backgrounded`, and the result later arrives through notifications

## ADDED Requirements

### Requirement: Self-backgrounding
The system SHALL provide `agent.move_to_background(statusMessage)` (safe), visible only in interactive turns. Calling it SHALL speak the status message and continue the run as a background task, like the latency cap.

#### Scenario: Model backgrounds early
- **WHEN** the model calls `agent_move_to_background` with "I'll look into it"
- **THEN** the final event contains that message and a background task continues the work

### Requirement: Background rules after the switch
On any switch to background, the tool list SHALL be recomputed with background rules (advisor visible, confirm-tier → approval queue), and the originating satellite's `denyTools` / `denyConfirm` SHALL keep applying through the task's `origin_satellite_id`.

#### Scenario: Policy kept
- **WHEN** a turn from a satellite with `denyConfirm: [printer.*]` is backgrounded
- **THEN** the background run cannot queue a printer approval

### Requirement: followupRef handling
A turn's `followupRef` SHALL be resolved as defined in `voice-followup`.

#### Scenario: Valid reference
- **WHEN** a turn carries a valid `followupRef` for a pending action
- **THEN** the reply enters the confirmation flow of that action
