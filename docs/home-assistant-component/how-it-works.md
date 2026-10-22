---
icon: fa-solid fa-gears
order: 30
description: "What the component sends to Marshal for each Assist turn, how it handles the reply, and how timeouts, errors and follow-ups behave."
---

# How the Home Assistant component works

This page describes what the component sends to Marshal for each Assist turn, how it handles the reply, and how timeouts, errors and follow-ups behave.

## One turn

1. Assist hands the turn to the Marshal conversation entity.
2. The component sends `POST /api/v1/conversation/turns` with the fields below and reads the [event stream](../api/conversation.md).
3. `delta` events go to Home Assistant's chat log as streamed assistant content, so a streaming text-to-speech engine can start before the reply is complete.
4. The `final` event sets the spoken response and `continue_conversation`.

`status` events are never spoken. If no deltas arrived, the `final` speech is used as the reply.

## Context sent

The component sends only turn fields. It sends no entity states and no other Home Assistant data; Marshal reads what it needs from Home Assistant itself.

| Marshal field | Source in Home Assistant |
|---|---|
| `text` | The user's text |
| `language` | Primary subtag of the turn language (`de-AT` becomes `de`) |
| `conversationId` | Home Assistant's conversation id (the component generates one when none exists and returns it) |
| `deviceId` | The turn's device id |
| `satelliteId` | The satellite entity id |
| `areaId` | Area of the device; if none, area of the satellite entity or its device |
| `haUserId` | The context user id |
| `inputMode` | `voice` when the pipeline run started at wake word or speech-to-text, `text` when typed |
| `followupRef` | Nonce from a follow-up marker, see below |

Input mode is read from the running Assist pipeline. If that cannot be read, a turn with a satellite id counts as voice and anything else as text.

Marshal uses `satelliteId` and `areaId` for the room context and the [satellite profile](../configuration/satellites-yaml.md) (verbosity, notes, denied tools).

## Timeouts

| Step | Limit |
|---|---|
| Connect | 5 s |
| Whole turn | 75 s |
| Version and token checks | 10 s |

Marshal enforces its own latency cap per turn (`agent.voiceLatencyCap`, default 8 s; `agent.textLatencyCap`, default 60 s). On reaching it, Marshal speaks a short "working on it" reply and continues in the background; the result is delivered later as a [notification](../guides/notifications.md). The component timeout is a safety net above the text cap.

## Continuing a conversation

When Marshal asks a question, for example to confirm an action, the `final` event has `continueConversation = true` and the component passes that to Home Assistant, so the satellite keeps listening. Marshal decides this; a trailing question mark alone does not.

## Follow-ups

When Marshal asks something by voice on its own initiative (for example a delivered result or an approval question), it calls `assist_satellite.start_conversation` with the extra system prompt `marshal-followup:<nonce>`. When the spoken reply arrives, the component finds the marker and sends the nonce as `followupRef`. It forwards nothing else from the extra system prompt. Marshal links the reply to the pending action, task or result if the nonce is known, unexpired, unused and from the same satellite; otherwise it handles a normal turn. If no reply comes, nothing happens and the follow-up expires. See [follow-ups](../guides/voice/follow-ups.md).

## Errors

If Marshal is unreachable, times out, rejects the token or returns an error, the component does not raise. It speaks a short error in the turn language (German or English; other languages get English) and logs the details at warning level.

| Case | English message |
|---|---|
| Unreachable or timeout | Marshal is not reachable right now. |
| `401` or `403` | Marshal refused access. Check the integration's API token. |
| Other error, or stream without a final event | Marshal could not handle that right now. |

## Related

::: cards
- [Conversation endpoint](../api/conversation.md)
- [Troubleshooting](troubleshooting.md)
- [Privacy](../concepts/privacy.md)
:::
