# Spec Delta

## Purpose

Lets Marshal propose helpful things on its own, from deterministic detectors, while learning per user and detector whether such suggestions are wanted, so it stays quiet when it would be wrong.

## ADDED Requirements

### Requirement: Detectors
Suggestions SHALL come only from detectors implemented in code, each with a stable id, an owner-scoped evaluation and parameters in `suggestions.detectors.<id>` (with `enabled`). v1 detectors: `window_rain` (an HA window or door sensor is open while the weather forecast for the next hour has precipitation probability of at least `minProbability`, default 60), `energy_anomaly` (a device on the energy dashboard used at least `factor` times, default 1.5, its median daily consumption of the previous 14 days on the previous day), `print_done_away` (a print completed while the owner's `person` is not home). A detector SHALL produce candidates with facts (entity names, values, times) and a dedupe key; a candidate with a dedupe key seen in the last `cooldown` (per detector, default 12 hours) SHALL be dropped. Detectors SHALL run only for owners whose "help Marshal learn" preference is on.

#### Scenario: Window and rain
- **WHEN** `binary_sensor.bedroom_window` is open and the forecast has 80% precipitation probability within the hour
- **THEN** `window_rain` produces a candidate naming the bedroom window

#### Scenario: Toggle off
- **WHEN** the owner's "help Marshal learn" preference is off
- **THEN** no detector runs for that owner

### Requirement: Acceptance estimate and delivery
For each owner and detector, the system SHALL keep accepted and dismissed counts and SHALL estimate acceptance as (accepted + 1) / (accepted + dismissed + 2). The first `suggestions.explore` (default 3) candidates of a detector SHALL be sent to the inbox regardless. Afterwards a candidate SHALL be sent only when the estimate is at least `suggestions.minAcceptance` (default 0.3): as push with priority `normal` when the estimate is at least `suggestions.pushAcceptance` (default 0.6), otherwise to the inbox with priority `low`. Suggestions SHALL never use voice and SHALL respect quiet hours. Expired suggestions (default after 6 hours, or when the detector's condition no longer holds) SHALL be neither accepted nor dismissed and SHALL NOT change the counts.

#### Scenario: Unwanted detector goes quiet
- **WHEN** the owner dismissed 6 of 7 `energy_anomaly` suggestions
- **THEN** the estimate is 2/9 and new `energy_anomaly` candidates are not sent

#### Scenario: Wanted detector pushes
- **WHEN** the owner accepted 5 of 6 `window_rain` suggestions
- **THEN** the estimate is 6/8 and the next candidate is sent as push with priority `normal`

### Requirement: Wording and proposed actions
A suggestion's text SHALL be written by a `fast` tier model from the candidate's facts in the owner's language (at most two sentences). A detector MAY attach a proposed action (tool and arguments), which SHALL be frozen like a pending action (nonce, TTL equal to the suggestion's expiry) and executed by the server only when the user accepts; its guard tier SHALL still apply, and a confirm-tier action SHALL be shown with its reason. Suggestions SHALL offer Accept and Dismiss in the inbox and as push actions with nonces.

#### Scenario: Accept with action
- **WHEN** a `print_done_away` suggestion proposes switching off `switch.voron_power` and the user taps Accept on the push
- **THEN** the frozen action runs once and the detector's accepted count increases

#### Scenario: Dismiss
- **WHEN** the user dismisses a suggestion in the inbox
- **THEN** nothing runs and the dismissed count increases

### Requirement: Muting
The owner SHALL be able to mute a detector indefinitely or for a period, and to reset its counts, in the preferences view. Muted detectors SHALL produce no suggestions.

#### Scenario: Mute for a week
- **WHEN** the owner mutes `energy_anomaly` for 7 days
- **THEN** no `energy_anomaly` suggestion is sent in that period
