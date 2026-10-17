## ADDED Requirements

### Requirement: Approvals without a task
A non-interactive run that does not belong to a task (an AI task, an MCP `ask_marshal` turn) SHALL NOT hold a pending action in its session. A confirm-tier call, or a `persists` call escalated by taint, SHALL create a frozen pending action in the approval queue with the same TTL, notification and reasons as background approvals, and the run SHALL continue with a tool result saying the action waits for approval. On approval the server SHALL execute exactly the frozen action and SHALL notify the owner of the result with priority `low`; on rejection or expiry nothing SHALL execute. No later input to the same session SHALL confirm the action.

#### Scenario: Approved later
- **WHEN** an AI task's `calendar_create_event` call waits in the approval queue and the user approves it
- **THEN** the event is created once and the owner gets a `low` notification with the result

#### Scenario: No in-session confirmation
- **WHEN** a non-interactive session receives "yes" after its run queued an approval
- **THEN** the pending action stays pending in the approval queue
