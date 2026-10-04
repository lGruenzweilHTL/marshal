## MODIFIED Requirements

### Requirement: Dashboard shell
Home SHALL be the landing page and SHALL be built from widgets, each reading existing API data, ordered by what needs the user. It SHALL include widgets for recent runs, today's cost and system health; a health warning SHALL move the health widget up.

#### Scenario: Health warning on dashboard
- **WHEN** `/health` reports a warning
- **THEN** Home shows the health widget with the warning

### Requirement: System view
The system view SHALL show health details, version information and the kill switch, and SHALL link to the configuration view for admins.

#### Scenario: Version shown
- **WHEN** the system view is opened
- **THEN** it shows the app version and the supported API versions

#### Scenario: Kill switch in the system view
- **WHEN** the system view is opened while no task runs
- **THEN** the kill switch is shown with its current state
