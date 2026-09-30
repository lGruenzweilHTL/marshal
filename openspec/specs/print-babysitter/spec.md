# print-babysitter Specification

## Purpose
Watching running prints for failures with a vision model, pausing after two strikes and sending a critical alert.
## Requirements
### Requirement: Babysitter as system component
The print babysitter SHALL be a hosted service in the Moonraker integration, enabled per printer in config, started by `printer.started` and stopped by `printer.complete`, `printer.cancelled` or `printer.error` events. It SHALL NOT be an agent task, and the agent SHALL NOT be able to start, stop or reconfigure it.

#### Scenario: Starts with the print
- **WHEN** a `printer.started` event arrives for a printer with the babysitter enabled
- **THEN** the babysitter begins taking snapshots on its interval

### Requirement: Failure detection with two strikes
Snapshots SHALL be evaluated by `IPrintFailureDetector` (v1: VLM-based). Only two consecutive positive verdicts SHALL trigger action.

#### Scenario: Single false positive
- **WHEN** one frame is positive and the next is negative
- **THEN** no pause happens

### Requirement: Auto-pause and critical alert
On two strikes the babysitter SHALL call the pause command directly (no agent loop, no guard round trip) and send a `critical` notification with the snapshot as a system source.

#### Scenario: Spaghetti detected
- **WHEN** two consecutive frames are positive
- **THEN** the print is paused and a `critical` notification with the snapshot is delivered even during quiet hours

### Requirement: Status visible to agent and UI
`printer.status` and the printer page SHALL include babysitter state (active, last verdicts).

#### Scenario: Status tool
- **WHEN** the model calls `printer_status` during a watched print
- **THEN** the result includes the babysitter as active with its last verdicts

