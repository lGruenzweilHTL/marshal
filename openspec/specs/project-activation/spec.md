# project-activation Specification

## Purpose
TBD - created by archiving change memory. Update Purpose after archive.
## Requirements
### Requirement: Explicit activation
A project SHALL be activated deterministically, without a model call, when the turn text contains its name or an active alias.

#### Scenario: Alias match
- **WHEN** "the voice assistant" is an active alias of project Marshal and the user says it
- **THEN** project Marshal is active for this turn with reason `alias`

### Requirement: Inferred activation with confidence tiers
The system SHALL compare the turn embedding with each project's centroid (description + top chunks, minus rejected phrases as negative examples) and add a recency prior for recently touched projects. High confidence SHALL activate the project stickily, medium SHALL activate it for this turn only, low SHALL do nothing. Inferred activation SHALL never be asked about by voice.

#### Scenario: Medium confidence
- **WHEN** a turn matches a project centroid with medium confidence
- **THEN** the project is active for this turn and not for the next one unless matched again

### Requirement: Stickiness with decay
Sticky activations SHALL decay over turns within a session and SHALL be dropped on a topic shift (low similarity between consecutive turn embeddings).

#### Scenario: Topic shift
- **WHEN** a sticky project is active and the next turn is about something unrelated
- **THEN** the project is no longer active

### Requirement: Corrections and alias learning
The system SHALL provide `memory.correct_project(kind, project?)` (safe) with kinds `rejected`, `switched`, `confirmed`, and SHALL store every correction or confirmation as `ProjectFeedback` with the triggering phrase and its embedding. Phrases that led to an uncorrected inference SHALL become alias candidates and SHALL be promoted to active aliases after 3 confirmations (configurable). Rejected phrases SHALL act as negative examples.

#### Scenario: Wrong project
- **WHEN** the user says "not that project" after an inferred activation
- **THEN** the project is unloaded, a `rejected` feedback row is stored and the phrase counts as a negative example

#### Scenario: Alias promotion
- **WHEN** an alias candidate reaches 3 confirmations
- **THEN** it becomes an active alias

### Requirement: Transparency
Active projects per turn and the reason (alias, centroid score, sticky, recency) SHALL be recorded in the run's context-build step and shown in the web UI, never spoken.

#### Scenario: Run detail
- **WHEN** a run activated a project by centroid
- **THEN** the run detail shows the project and its score

