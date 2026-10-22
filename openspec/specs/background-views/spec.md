# background-views Specification

## Purpose
The web UI for background work: tasks and watches, the review queue for approvals and code changes, the notification history and the dashboard widgets.

## Requirements

### Requirement: Tasks view
The web UI SHALL list tasks and watches with status, trigger, next run, budget use and latest result; the detail SHALL show goal, tool policy, notes, runs, deliveries and the result `body` rendered as markdown. Users SHALL be able to edit the goal and the tool policy (adding only tools the task's template allows), cancel tasks and use the kill switch. Executor sessions SHALL be shown with status. The views SHALL use owner-scoped REST endpoints registered on the versioned route group; goal edits, policy edits and cancellations SHALL be audited user actions.

#### Scenario: Edit the briefing goal
- **WHEN** the user edits the goal of task `briefing` in the UI
- **THEN** the next run uses the new goal and an audit event is written

#### Scenario: Widen a policy in the UI
- **WHEN** the user adds `calendar.list_events` to a task's policy in the UI
- **THEN** the next run of the task can see `calendar_list_events` and an audit event is written

### Requirement: Review queue
The Inbox SHALL be the one destination for everything that waits for the user: pending approvals (frozen action, reasons, expiry, approve / reject), coding proposals (diff, merge / reject), skill proposals (preview, diff, download / dismiss), suggestions (text, proposed action, accept / dismiss) and notifications. Approvals and proposals SHALL be listed before suggestions, and suggestions before notifications; the navigation badge SHALL count pending approvals, open proposals and unread critical notifications. An approval opened from a push deep link SHALL land on a focused approval screen. Approve, reject, merge, accepting and dismissing SHALL be audited user actions.

#### Scenario: Approve on the phone
- **WHEN** the user opens an approval from a push deep link on a phone and approves
- **THEN** the frozen action executes and the task resumes

#### Scenario: One place for waiting items
- **WHEN** one approval, one proposal and two notifications exist
- **THEN** the Inbox lists the approval and the proposal first, then the notifications, and the Inbox badge counts the items that need the user

#### Scenario: Suggestion order
- **WHEN** one skill proposal, one suggestion and one notification exist
- **THEN** the Inbox lists the proposal, then the suggestion, then the notification

### Requirement: Notification history view
The Inbox SHALL list notifications with content, priority, channel, outcome and reason, filterable by outcome. A notification opened from a push deep link SHALL open that item in the Inbox.

#### Scenario: Filter held
- **WHEN** the user filters by `held`
- **THEN** only notifications held by quiet hours are listed

### Requirement: Dashboard widgets
Home SHALL be ordered by what needs the user: pending approvals with approve / reject first, then printer alerts, then running tasks with the next scheduled runs, then recent notifications. The kill switch SHALL be shown on Home while background tasks run and SHALL always be available in the system view.

#### Scenario: Pending approval widget
- **WHEN** an approval is pending
- **THEN** Home shows it at the top with approve / reject buttons

#### Scenario: Kill switch only while tasks run
- **WHEN** no background task runs
- **THEN** Home shows no kill switch and the system view still offers it
