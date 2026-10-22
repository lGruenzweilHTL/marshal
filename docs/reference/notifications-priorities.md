---
icon: fa-solid fa-bell
description: "Notification priorities, channels, routing rules and the outcomes and reasons Marshal records for each notification."
---

# Notification priorities

Notification priorities, channels, routing rules and the outcomes and reasons Marshal records for each notification.

## Priorities

| Priority | Who may send it | Delivery |
|---|---|---|
| `critical` | System sources only. | Always by voice and push. Ignores quiet hours, voice do-not-disturb, coalescing and the voice caps. |
| `normal` | Anyone, including the agent. This is the default. | Routed by presence, with quiet hours, coalescing and voice caps. |
| `low` | Anyone, including the agent. | Never by voice. Goes to the inbox, or to a silent push when the delivery asks for push. |

A source is a system source when its name starts with `system:`. The print babysitter (`system:babysitter`) is one. The agent's `notifications.notify` accepts only `normal` and `low` and fails with `notifications.invalid` otherwise: "priority is normal or low; only system sources send critical notifications." The router applies the same rule to every other source.

If a critical notification reaches neither voice nor push (no satellite known, no push target), it goes to the inbox with reason `critical_undeliverable`.

## Channels

| Channel | Behavior |
|---|---|
| `voice` | Spoken on a satellite. Announce, or a conversation when a reply is expected (`followUp`). Voice targets a satellite entity id, or the room where you are. |
| `push` | A companion app push notification to your push targets. |
| `inbox` | Stored in your inbox until you or a task consumes the item. |
| `silent` | Task deliveries only: no notification. The result stays visible in the web UI. |

Notifications target a person, not a device. Open browsers also receive every notification on the live-event stream, whatever the channel.

## How Marshal routes a `normal` notification

Marshal checks these in order and uses the first that applies.

1. **Requested inbox or silent.** The delivery asked for `inbox` (reason `requested`) or `silent`.
2. **Quiet hours.** During your quiet hours, with `notifications.quietHoursMode: hold` Marshal holds the notification until quiet hours end (outcome `held`, reason `quiet_hours`). With `silentPush` it sends a silent push at once. A task or watch with `overrideQuietHoursUntil` skips this step until that time.
3. **Coalescing.** Notifications to the same channel and target within `notifications.coalesceWindow` become one message. The first one waits for the window to end; later ones get outcome `coalesced`. Notifications with push actions or a follow-up are never coalesced.
4. **Channel choice.**
   - A `push` delivery goes to push.
   - Otherwise Marshal tries voice. Voice do-not-disturb (reason `dnd`) turns voice off. An explicit target wins (reason `requested`). Otherwise your presence decides: home in an occupied room speaks on that room's satellite (reason `presence`); away goes to push.
   - Voice is skipped when the same source already spoke within `notifications.sourceVoiceInterval` (reason `source_voice_cap`) or when `notifications.voicePerHour` unsolicited voice notifications were already delivered in the last hour (reason `voice_cap`). The notification goes to push instead.
   - If speaking fails, the notification goes to push (reason `voice_failed`).
5. **Push fallback.** Without a push target the notification goes to the inbox (reason `no_push_target`). A failed push also goes to the inbox (reason `push_failed`).

Settings are in [`notifications`](../configuration/marshal-yaml/notifications.md).

## Outcomes

Marshal records every notification with its content, priority, chosen channel, outcome and the reason for the decision.

| Outcome | Meaning |
|---|---|
| `pending` | Not decided yet, for example waiting for the coalescing window. |
| `delivered` | Delivered on its channel. |
| `coalesced` | Merged into another notification. |
| `held` | Held until quiet hours end. |
| `inboxed` | Stored in the inbox. |
| `expired` | Its delivery window passed. |
| `fallback` | Delivered on the delivery's fallback channel after its conditions expired. |

## Reasons

| Reason | Seen with | Meaning |
|---|---|---|
| `requested` | `delivered`, `inboxed` | The delivery asked for this channel or target. |
| `presence` | `delivered` | Voice, chosen from where you are. |
| `no_room` | `delivered` (push) | You are home but no occupied room with a satellite is known. |
| `away` | `delivered` (push) | You are not home. |
| `presence_unknown` | `delivered` (push) | Your presence is unknown. |
| `dnd` | `delivered` (push) | Voice do-not-disturb is on. |
| `quiet_hours` | `held`, `delivered` | Quiet hours applied. |
| `coalesced` | `coalesced` | Merged into an earlier notification. |
| `source_voice_cap` | `delivered` (push) | The source already spoke within the interval. |
| `voice_cap` | `delivered` (push) | The hourly voice cap was reached. |
| `voice_failed` | `delivered` (push) | Speaking failed. |
| `low` | `inboxed`, `delivered` | Low priority. |
| `critical` | `delivered` | Critical delivery. |
| `critical_undeliverable` | `inboxed` | Critical, but neither voice nor push was available. |
| `no_push_target` | `inboxed` | No push target is configured. |
| `push_failed` | `inboxed` | The push failed. |

## Actionable push

Push notifications for approvals carry Approve and Reject actions through the Home Assistant companion app. The reply arrives as a `mobile_app_notification_action` event and counts only with a valid, unexpired nonce. A wrong nonce approves nothing and is logged. See [approvals](../guides/tasks/approvals.md).

## Related

::: cards
- [Notifications guide](../guides/notifications.md)
- [`notifications` configuration](../configuration/marshal-yaml/notifications.md)
- [Tools](tools.md)
  `notifications.*`
- [Events](events.md)
:::
