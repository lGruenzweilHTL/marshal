## ADDED Requirements

### Requirement: Rating and saving runs
The run detail SHALL show the run's rating and note and SHALL let the owner rate the run. For admins, it SHALL offer "save as eval case" (also when the toggle is off) and SHALL show when a case exists for the run, with a link to it. The runs list SHALL be filterable by rating.

#### Scenario: Save as case
- **WHEN** an admin clicks "save as eval case" on a run
- **THEN** an eval case is created and the run detail links to it

#### Scenario: Filter by rating
- **WHEN** the user filters the runs list by `down`
- **THEN** only runs rated `down` are listed
