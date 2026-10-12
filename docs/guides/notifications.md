# Control how Marshal notifies you

Choose where proactive messages reach you, when Marshal stays quiet and how to see why a message went where it did.

## Before you start

- Home Assistant is connected ([connect Home Assistant](../getting-started/connect-home-assistant.md)). Voice needs satellites; push needs the Home Assistant companion app.
- Set `notifications.publicUrl` in `marshal.yaml` if push notifications should link into the web UI.

## Steps

### 1. Set your notification targets

In **Settings > Preferences**, section **Notifications**:

- **Home Assistant person:** your `person.*` entity. Marshal uses it to know whether you are home.
- **Push targets:** one per line. Critical alerts go to all of them.

### 2. Set quiet hours

In **Preferences > Quiet hours**, add weekly windows with a day, from and to time in your time zone. A window may run past midnight.

### 3. Tune noise control

Edit `notifications` in `marshal.yaml`:

```yaml
notifications:
  quietHoursMode: hold            # hold or silentPush
  coalesceWindow: "00:02:00"
  sourceVoiceInterval: "00:30:00"
  voicePerHour: 6
  followUpReplyWindow: "00:00:20"
  publicUrl: https://marshal.example.com/
```

Key list: [`notifications` reference](../configuration/marshal-yaml/notifications.md).

### 4. Pause voice for a while

Set **Do not disturb until** in Preferences. Voice notifications go to push until then. `critical` ignores it.

## Priorities

| Priority | Behavior |
|---|---|
| `critical` | Always voice and push, ignoring quiet hours, do-not-disturb and caps. Only system sources (print babysitter, e-stop, printer errors) send it. The model cannot: `notifications_notify` accepts only `normal` and `low`. |
| `normal` | Follows channel choice, quiet hours and noise control. |
| `low` | Never voice. Goes to the inbox, or a silent push when push was requested. |

## Channels

- **voice:** an announcement on a satellite, or a conversation when a reply is expected ([follow-ups](voice/follow-ups.md)).
- **push:** the Home Assistant companion app.
- **inbox:** the Inbox in the web UI.

Open browsers also get every notification on the live stream, whatever the channel.

Channel choice, in order: the delivery's own channel and conditions; then presence (you are home: voice in the room whose occupancy `binary_sensor.*` is on; away: push); then push as fallback. Notifications go to a person, not a device. A failed voice delivery falls back to push. Without a push target the notification lands in the inbox.

## Quiet hours

During quiet hours `normal` notifications are held until the window ends (`quietHoursMode: hold`) or sent at once as a silent push (`silentPush`). `low` goes to the inbox. A task or watch with a quiet-hours override, set only through a confirm-tier call, bypasses quiet hours until the given time.

## Noise control

- Several `normal` notifications within `coalesceWindow` become one message. Notifications with actions or a follow-up are never merged.
- At most one voice notification per source within `sourceVoiceInterval`; others go to push.
- At most `voicePerHour` voice notifications per hour; beyond that they go to push.

## Actionable push

Push notifications for approvals carry Approve and Reject actions. The reply returns as a `mobile_app_notification_action` event and counts only with a valid, unexpired nonce. An unknown nonce approves nothing and is logged. See [approvals](tasks/approvals.md).

## Check that it works and see why

Open **Inbox** and filter the history by outcome. Each entry shows content, priority, channel, outcome and reason.

| Outcome | Meaning |
|---|---|
| `pending` | Waiting, for example in the coalescing window. |
| `delivered` | Sent on a channel. |
| `coalesced` | Merged into another message. |
| `held` | Held by quiet hours. |
| `inboxed` | Put in the inbox. |
| `expired` | Not delivered in time. |
| `fallback` | Delivered by a delivery's fallback channel. |

A message pushed instead of spoken because of the hourly cap has outcome `delivered`, channel `push` and reason `voice_cap`. Other reasons you may see: `source_voice_cap`, `dnd`, `quiet_hours`, `away`, `no_room`, `presence_unknown`, `voice_failed`, `no_push_target`, `push_failed`.

## Tools for the model

`notifications_notify`, `notifications_inbox_list` and `notifications_inbox_consume` are safe-tier tools. The briefing lists the inbox and consumes items it covered. See [tools reference](../reference/tools.md) and [priorities](../reference/notifications-priorities.md).

## Related

- [Scheduled tasks](tasks/scheduled-tasks.md) for deliveries, conditions and expiry
- [Voice follow-ups](voice/follow-ups.md)
- [User preferences](web-ui/language.md)
