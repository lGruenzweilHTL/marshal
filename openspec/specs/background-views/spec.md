# background-views Specification

## Purpose
The web UI for background work: tasks and watches, the review queue for approvals and code changes, the notification history and the dashboard widgets.
## Requirements
### Requirement: Tasks view
The web UI SHALL list tasks and watches with status, trigger, next run, budget use and latest result; the detail SHALL show goal, notes, runs, deliveries and the result `body` rendered as markdown. Users SHALL be able to edit the goal, cancel tasks and use the kill switch. Executor sessions SHALL be shown with status. The views SHALL use owner-scoped REST endpoints registered on the versioned route group; goal edits and cancellations SHALL be audited user actions.

#### Scenario: Edit the briefing goal
- **WHEN** the user edits the goal of task `briefing` in the UI
- **THEN** the next run uses the new goal and an audit event is written

### Requirement: Review queue
The review queue SHALL show pending approvals (frozen action, reasons, expiry, approve / reject) and coding proposals (diff, merge / reject). Approve, reject and merge SHALL be audited user actions.

#### Scenario: Approve on the phone
- **WHEN** the user opens an approval from a push deep link on a phone and approves
- **THEN** the frozen action executes and the task resumes

### Requirement: Notification history view
The UI SHALL list notifications with content, priority, channel, outcome and reason, filterable by outcome.

#### Scenario: Filter held
- **WHEN** the user filters by `held`
- **THEN** only notifications held by quiet hours are listed

### Requirement: Dashboard widgets
The dashboard SHALL add widgets for running tasks and next scheduled runs, pending approvals with quick actions, recent notifications, and the kill switch.

#### Scenario: Pending approval widget
- **WHEN** an approval is pending
- **THEN** the dashboard shows it with approve / reject buttons

