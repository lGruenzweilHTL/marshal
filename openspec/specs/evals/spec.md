# evals Specification

## Purpose
Turns rated runs into eval cases and replays them against a candidate configuration, so model, template and prompt changes can be checked against real past turns before they go live.

## Requirements

### Requirement: Run ratings
A human principal SHALL be able to rate any of their own runs `up` or `down`, with an optional note (at most 1,000 characters), and to change or remove the rating. The rating SHALL be stored on the run whatever the "help Marshal learn" preference says. Machine tokens SHALL NOT rate.

#### Scenario: Rate a voice run afterwards
- **WHEN** the user opens a voice run in the run detail and rates it `down` with a note
- **THEN** the run stores the rating and the note

#### Scenario: Token principal
- **WHEN** a machine token posts a rating
- **THEN** the response is 403

### Requirement: Eval cases
An eval case SHALL snapshot a run: owner, source run, input text, input mode, language, satellite and area, session history before the turn, the turn-level context contributions, the recorded tool calls with arguments and results, the reply, the rating and the note. A case SHALL be created automatically when a run is rated while the owner's "help Marshal learn" preference is on, and when an admin saves a run as a case. A case whose recorded tool results include references instead of content (sensitive tools) SHALL be marked `incomplete`. Cases SHALL be owner-scoped, listable, editable (expected behavior note) and deletable, and SHALL NOT be removed by the retention purge.

#### Scenario: Rating creates a case
- **WHEN** the toggle is on and the user rates a run `up`
- **THEN** an eval case with expectation `good` exists for that run

#### Scenario: Toggle off
- **WHEN** the toggle is off and the user rates a run
- **THEN** the rating is stored and no eval case is created

#### Scenario: Sensitive tool in the run
- **WHEN** a saved run contains a `mail_read` result stored as a reference
- **THEN** the case is marked `incomplete`

### Requirement: Replay
An admin SHALL be able to start an eval run over selected cases with a candidate configuration: a model per tier (from the configured models), an optional template override and an optional base prompt override. Each case SHALL be replayed through the agent loop with the candidate configuration, the case's history and input, the case's turn-level context, and the current static context. Tools SHALL NOT be executed: a call whose tool id and canonical arguments equal a recorded call SHALL receive the recorded result; any other call SHALL receive a "no recording" error result and the case SHALL be marked `diverged`. Replays SHALL make no writes to memory, tasks, notifications or integrations, SHALL NOT appear in the runs list, and SHALL count against the global daily cost cap. Before starting, the view SHALL show an estimate of the cost.

#### Scenario: Same behavior
- **WHEN** a case recorded `ha_HassTurnOff` for the kitchen light and the candidate model makes the same call
- **THEN** the call gets the recorded result and the case's tool comparison passes

#### Scenario: Different tool
- **WHEN** the candidate model calls `ha_HassTurnOff` for the lamp instead
- **THEN** the call gets a "no recording" result and the case is `diverged`

#### Scenario: No side effects
- **WHEN** a replayed model calls `memory_remember`
- **THEN** no chunk is written

### Requirement: Comparison and report
Each replayed case SHALL get a result: `pass` (same tool sequence and arguments as recorded for a `good` case, or a different sequence for a `bad` case), `changed` (same tools, different arguments or reply), `diverged`, or `failed` (error or limit). When a judge model is selected, the report SHALL also show a judge score for the reply against the recorded reply and the case's note. The report SHALL show totals per result, cost, and per case the recorded and replayed tool calls and replies side by side. Eval runs and their results SHALL be kept until deleted.

#### Scenario: Bad case fixed
- **WHEN** a `bad` case recorded the wrong tool and the candidate makes a different call that has no recording
- **THEN** the case is `diverged` and the report shows both calls with the case's note, so the admin can judge the fix

#### Scenario: Report totals
- **WHEN** an eval run over 50 cases ends with 47 pass, 2 changed and 1 diverged
- **THEN** the report shows these totals and the run's cost
