---
icon: fa-solid fa-bell
description: "Routing, noise control and follow-up settings for proactive notifications."
---

# `notifications`

Routing, noise control and follow-up settings for proactive notifications.

## Example

```yaml
notifications:
  quietHoursMode: hold
  coalesceWindow: "00:02:00"
  sourceVoiceInterval: "00:30:00"
  voicePerHour: 6
  followUpReplyWindow: "00:00:20"
  publicUrl: https://marshal.example.com/
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `notifications.quietHoursMode` | `hold` or `silentPush` | `hold` | What happens to `normal` notifications during quiet hours. `hold`: deliver when quiet hours end. `silentPush`: deliver at once as a silent push. |
| `notifications.coalesceWindow` | duration | `00:02:00` | `normal` notifications within this window become one message. `00:00:00` turns it off. Range 0 to 30 minutes. |
| `notifications.sourceVoiceInterval` | duration | `00:30:00` | At most one voice notification per source in this interval. Later ones are pushed. |
| `notifications.voicePerHour` | integer | `6` | Unsolicited voice notifications per hour. Beyond it they are pushed. Not negative. |
| `notifications.followUpTtl` | duration | `00:02:00` | How long a spoken question waits for its reply. Must be positive. |
| `notifications.followUpReplyWindow` | duration | `00:00:20` | A voice turn this soon after a spoken question from the same satellite counts as the reply, when the satellite sends no follow-up reference. `00:00:00` turns it off. Between 0 and `followUpTtl`. |
| `notifications.publicUrl` | URL | unset | Marshal's public URL. Push notifications link into the web UI with it; without it they carry no link. Also the fallback for `displays.mediaBaseUrl`. |

`critical` notifications ignore quiet hours and the caps.

## Validation

- `notifications.coalesceWindow must be between 0 and 30 minutes.`
- `notifications.followUpTtl must be positive.`
- `notifications.followUpReplyWindow must be between 0 and followUpTtl.`
- `notifications.voicePerHour cannot be negative.`
- `notifications.publicUrl must be an absolute http(s) URL.`

## Related

::: cards
- [Notifications guide](../../guides/notifications.md)
- [Notification priorities](../../reference/notifications-priorities.md)
- [`displays`](displays.md)
- [Voice follow-ups](../../guides/voice/follow-ups.md)
:::
