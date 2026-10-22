---
icon: fa-solid fa-comment-dots
description: "When Marshal speaks a question on a satellite, your spoken reply goes to the approval, task or result the question belongs to."
---

# Answer Marshal by voice

When Marshal speaks a question on a satellite, your spoken reply goes to the approval, task or result the question belongs to.

## Before you start

- Voice is set up ([Assist pipeline](assist-pipeline.md)) and the Home Assistant integration passes follow-up references (it does by default).
- The satellite supports Home Assistant's `assist_satellite.start_conversation`.

## What follow-ups are

Marshal sets `followUp: true` on a voice delivery or asks a question that expects a reply. It then:

1. stores a follow-up record (owner, nonce, satellite, target, expiry),
2. calls `assist_satellite.start_conversation` on the satellite with the message,
3. passes `marshal-followup:<nonce>` as the extra system prompt. The integration sends only the nonce to Marshal, as `followupRef`.

Your reply links to the record only when the nonce is known, unexpired, unused and comes from the same satellite. Otherwise Marshal ignores the reference and handles the turn normally.

## The three targets

| Question about | What your reply does |
|---|---|
| A pending action (approval) | Enters the normal confirmation flow: a confirm keyword or the confirmation judge decides. Say "confirm" (or "bestätigt"). The reference only identifies the question. |
| A waiting task | The reply becomes the task's trigger payload and the task resumes. |
| A delivered result | Starts a new session seeded with the result's spoken text and body. "Tell me more about the second article" runs in that session without re-running the task. |

A neutral reply to an approval question leaves the approval pending and runs as a normal turn.

## Try it

1. Ask Marshal to brief you with a voice delivery that allows a reply (see [daily briefing](../tasks/daily-briefing.md)).
2. After the briefing, say "tell me more about the second article".
3. Marshal answers from the briefing without running it again.

## If you do not answer

Nothing happens. The record expires after `notifications.followUpTtl` (default 2 minutes) and the approval or task stays as it was. Approvals remain open in the Inbox.

## Fallback window

If a satellite or Home Assistant version does not pass the nonce, Marshal links the first voice turn from the same satellite within `notifications.followUpReplyWindow` (default 20 seconds) to the question. Set it to `"00:00:00"` to turn the fallback off. See [`notifications`](../../configuration/marshal-yaml/notifications.md).

## Troubleshooting

- The reply is handled as a new conversation: the nonce did not arrive and the reply came after the window. Reply sooner or raise the window.
- A reply from another satellite never links.

## Related

::: cards
- [Approvals](../tasks/approvals.md)
- [Notifications](../notifications.md)
- [Risk and confirmation](../../concepts/risk-and-confirmation.md)
:::
