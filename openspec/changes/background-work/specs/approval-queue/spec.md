## ADDED Requirements

### Requirement: Background confirmations go to the approval queue
In background runs, a confirm-tier call SHALL create a frozen pending action (tool, args, task, owner, nonce, reasons) with a 10-minute TTL (configurable) and SHALL notify the owner with priority `normal` through push (actionable) and the web UI review queue.

#### Scenario: Approval requested
- **WHEN** a background run calls `printer_start_print`
- **THEN** a pending action with a 10-minute expiry exists and an actionable push with its reason was sent

### Requirement: Runs suspend instead of blocking
After queuing an approval, the run SHALL end with status `waiting`, trigger = the approval decision, and an automatic note in the task's `notes` naming the tool and an argument summary. No worker SHALL be held while waiting.

#### Scenario: Worker freed
- **WHEN** a run queues an approval
- **THEN** the run ends and its worker becomes free

### Requirement: Server executes approved actions
On approval (web UI, nonce-verified push action, or a voice follow-up confirmation), the server SHALL execute exactly the frozen action and resume the task with a trigger payload containing the decision and the action's result. Rejection or expiry SHALL resume the task with a payload saying so; expiry SHALL count as rejection.

#### Scenario: Approved
- **WHEN** the user approves in the web UI
- **THEN** the frozen action runs once, and the resumed run's payload contains `approved` and the result

#### Scenario: Expired
- **WHEN** nobody decides within 10 minutes
- **THEN** the action is `expired`, nothing executes, and the task resumes with `rejected: expired`

### Requirement: Reasons shown verbatim
Push notifications and the review queue SHALL show the server's reason sentences verbatim, including taint sources, with a link from the review queue to the tool result that tainted the run.

#### Scenario: Taint reason in the review queue
- **WHEN** an approval was escalated by taint from mail
- **THEN** the review queue shows the mail-injection reason and links to the mail tool step
