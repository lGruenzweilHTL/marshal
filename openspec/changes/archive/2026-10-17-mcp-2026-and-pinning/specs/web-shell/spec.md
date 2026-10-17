## ADDED Requirements

### Requirement: Health banner
Every page of the web UI SHALL show a banner while health warnings exist, with the highest severity's color, the number of warnings, the first warning's message and a link to the system view (and, for admins, the warning's own link). The user SHALL be able to dismiss the banner; it SHALL reappear when the set of warning codes changes. The banner SHALL update from the `health` live topic.

#### Scenario: Banner on a pending approval
- **WHEN** an MCP tool waits for approval and an admin opens the chat
- **THEN** the chat page shows a banner linking to the configuration view

#### Scenario: Dismissed until changed
- **WHEN** the user dismisses the banner and later a second warning appears
- **THEN** the banner shows again
