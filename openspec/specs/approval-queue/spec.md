# approval-queue Specification

## Purpose
How confirm-tier actions of background runs wait for the user: frozen server-side, decided in the web UI, by push or by voice, and executed by the server, never by the model.
## Requirements
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

### Requirement: Approvals without a task
A non-interactive run that does not belong to a task (an AI task, an MCP `ask_marshal` turn) SHALL NOT hold a pending action in its session. A confirm-tier call, or a `persists` call escalated by taint, SHALL create a frozen pending action in the approval queue with the same TTL, notification and reasons as background approvals, and the run SHALL continue with a tool result saying the action waits for approval. On approval the server SHALL execute exactly the frozen action and SHALL notify the owner of the result with priority `low`; on rejection or expiry nothing SHALL execute. No later input to the same session SHALL confirm the action.

#### Scenario: Approved later
- **WHEN** an AI task's `calendar_create_event` call waits in the approval queue and the user approves it
- **THEN** the event is created once and the owner gets a `low` notification with the result

#### Scenario: No in-session confirmation
- **WHEN** a non-interactive session receives "yes" after its run queued an approval
- **THEN** the pending action stays pending in the approval queue

### Requirement: Pending input requests
When an MCP server answers a tool call with an input request, the system SHALL create a pending input request (owner, server, tool, frozen arguments, the server's request in form or URL mode, run, task if any, nonce) with the approval TTL, and SHALL NOT pass the request's content to the model. The model SHALL receive a tool result saying the server needs input from the user in the web UI. The review queue SHALL show form requests as a form generated from the requested schema and URL requests as a link plus a "Done" action. Only a user action in the web UI SHALL answer; the server SHALL then repeat the frozen call once with the answer and handle the result like an approved action's result. In background runs the run SHALL suspend as for approvals. A push notification with a link to the request SHALL be sent; it SHALL NOT carry answer actions.

#### Scenario: Form request in a web turn
- **WHEN** a web turn's `paperless_upload` call returns a form request for a correspondent
- **THEN** the chat shows the form, the model's tool result only says input is needed in the web UI, and after the user submits, the call is repeated with the answer and its result appears in the conversation

#### Scenario: URL request from a voice turn
- **WHEN** a voice turn's call returns a URL request to log in
- **THEN** the spoken reply says to continue in the web UI, a push with a link is sent, and the URL is never part of the model context

#### Scenario: Expired request
- **WHEN** nobody answers within the TTL
- **THEN** the request expires, nothing is sent to the server, and a suspended task resumes with `rejected: expired`
