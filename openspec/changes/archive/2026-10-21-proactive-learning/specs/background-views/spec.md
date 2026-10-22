## MODIFIED Requirements

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
