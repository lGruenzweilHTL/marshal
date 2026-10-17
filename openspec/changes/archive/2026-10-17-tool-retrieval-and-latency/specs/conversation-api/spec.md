## ADDED Requirements

### Requirement: Spoken filler
In a turn with `inputMode = voice`, before executing a tool that declares a filler line in the turn's language and whose expected duration exceeds `voice.fillerThresholdMs` (default 700), the system SHALL send that line as a `delta` event, unless a `delta` was already sent in the turn. At most one filler SHALL be sent per turn. The expected duration SHALL be the median of the tool's recorded durations over its last 20 calls, or the threshold plus one millisecond when fewer than 5 calls are recorded. Text turns SHALL NOT get fillers. A sent filler SHALL be part of the recorded reply text.

#### Scenario: Slow tool in a voice turn
- **WHEN** a German voice turn calls `printer_snapshot`, whose median duration is 2 s, before any delta was sent
- **THEN** the stream carries the German filler as a `delta` before the tool runs, and later deltas follow it

#### Scenario: Fast tool
- **WHEN** a voice turn calls a tool whose median duration is 120 ms
- **THEN** no filler is sent

#### Scenario: Model already spoke
- **WHEN** the model streamed text before calling a slow tool
- **THEN** no filler is sent

#### Scenario: Text turn
- **WHEN** a web text turn calls a slow tool with a filler line
- **THEN** no filler is sent
