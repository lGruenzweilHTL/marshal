## MODIFIED Requirements

### Requirement: Tasks view
The web UI SHALL list tasks and watches with status, trigger, next run, budget use and latest result; the detail SHALL show goal, tool policy, notes, runs, deliveries and the result `body` rendered as markdown. Users SHALL be able to edit the goal and the tool policy (adding only tools the task's template allows), cancel tasks and use the kill switch. Executor sessions SHALL be shown with status. The views SHALL use owner-scoped REST endpoints registered on the versioned route group; goal edits, policy edits and cancellations SHALL be audited user actions.

#### Scenario: Edit the briefing goal
- **WHEN** the user edits the goal of task `briefing` in the UI
- **THEN** the next run uses the new goal and an audit event is written

#### Scenario: Widen a policy in the UI
- **WHEN** the user adds `calendar.list_events` to a task's policy in the UI
- **THEN** the next run of the task can see `calendar_list_events` and an audit event is written
