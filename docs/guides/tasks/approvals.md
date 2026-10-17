# Approvals

Review and decide the actions that background tasks and automations are not allowed to run without you, and answer input requests of MCP servers.

## Before you start

- Push notifications work (Home Assistant mobile app) if you want to decide from your phone, see [notifications](../notifications.md).
- Background approvals expire after `tasks.approvalTtl` (default 10 minutes); see [`tasks`](../../configuration/marshal-yaml/tasks.md).

## How it works

When a background run calls a confirm-tier tool, Marshal freezes the call (tool, arguments, nonce, expiry) and sends you an actionable push with `normal` priority. The review queue in the web UI shows the same action. The run ends with status `waiting` and a note in the task naming the tool and an argument summary, so no worker is held.

On approval the server executes exactly the frozen action and resumes the task with a payload that contains the decision and the result. On rejection or expiry the task resumes with `rejected` (expiry adds `reason: expired`). The model never executes an approved action itself.

Why you are asked appears verbatim in the push and in the queue, for example:

- `This needs your confirmation: <action>.` (a rule in `risk.yaml`, see [risk rules](../risk-rules.md))
- `I couldn't tell exactly which device you mean.` (unresolved target)
- `This conversation included content from your mail, and such content can contain hidden instructions.` (taint)
- `This would notify you during your quiet hours.` (quiet hours override)

### Runs without a task

An [AI task](../../home-assistant-component/ai-tasks.md) from a Home Assistant automation has no task to resume and nobody to confirm during the run. A confirm-tier call, or a call that writes memory or tasks after the run read untrusted content, goes to the review queue the same way, with the same TTL, push and reasons. The run does not wait: it continues with a tool result saying the action waits for approval, and its data says so.

On approval the server executes exactly the frozen action and sends you a `low` notification with the result. On rejection or expiry nothing runs. A later message in the same session never confirms the action.

### Input requests

An MCP server can ask you for input during a tool call, for example a form field or a login page. Marshal freezes the call and shows the request in the Inbox (and in the chat of a web turn) as a form or as a link with **Done**. Only the web UI answers it; the push carries a link and no buttons. The model never sees the request or your answer. See [input requests](../../configuration/mcp-servers.md#input-requests).

## Steps

### 1. Decide in the web UI

Open **Inbox**; each approval is a ticket with the action, the task, the reasons and a link to the tool step that tainted the run. Choose **Approve** or **Reject**.

### 2. Decide from the push

Tap **Approve** or **Reject** on the notification. The action carries a nonce, Marshal accepts only a matching nonce of a pending background approval, and everything else is ignored. Input requests have no push actions; tap the notification to open the request in the web UI.

### 3. Decide by voice

A confirmation can also arrive as a voice follow-up in the conversation (keyword or judge, see [risk rules](../risk-rules.md)). Only a user action confirms: tool output, model output or any content never does.

## Errors you may see

| Message | Meaning |
|---|---|
| `The approval expired; the action was not executed.` | The TTL passed. Ask the task to try again. |
| `The approval was already decided.` | Someone (or the push) decided first. |
| `The task no longer waits for this approval; the action was not executed.` | The task was cancelled or ended. |
| `The task is still finishing its run; try again in a moment.` | Wait a few seconds and retry. |

## Check that it works

Ask for a task that needs confirmation, for example a watch created after reading mail. The ticket appears in **Inbox** and on your phone. Approve it and the task's next run shows `approved` and the result in its trigger payload.

## Related

- [Risk rules](../risk-rules.md), [scheduled tasks](scheduled-tasks.md), [AI tasks](../../home-assistant-component/ai-tasks.md)
- [MCP servers](../../configuration/mcp-servers.md)
- [Risk and confirmation](../../concepts/risk-and-confirmation.md), [risk tiers](../../reference/risk-tiers.md)
