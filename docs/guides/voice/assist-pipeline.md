# Use Marshal in an Assist pipeline

Make Marshal the conversation agent of a Home Assistant Assist pipeline, so voice satellites and the Assist dialog talk to it.

## Before you start

- Marshal runs and Home Assistant can reach it over HTTP(S). See [connect Home Assistant](../../getting-started/connect-home-assistant.md).
- The Marshal custom integration is installed and set up. See [installation of the component](../../home-assistant-component/installation.md).
- The integration's API token has the `conversation:write` scope. See [machine tokens](../authentication/machine-tokens.md).
- A speech-to-text and a text-to-speech engine exist in Home Assistant.

## Steps

### 1. Check the Marshal conversation entity

After setup, Home Assistant has a conversation entity named **Marshal**. It supports German and English.

### 2. Choose Marshal in a pipeline

1. In Home Assistant open **Settings > Voice assistants**.
2. Open an existing pipeline or add one.
3. Set **Conversation agent** to **Marshal**.
4. Choose your speech-to-text and text-to-speech engines and the pipeline language (`de` or `en`).
5. Save.

### 3. Assign the pipeline to a satellite

Open the satellite's device settings in Home Assistant and select the pipeline. Assign the satellite's device to an area: Marshal uses the area to resolve "turn on the light" to the room you speak from.

## Check that it works

- Say a wake word and ask "What time is it?". Marshal answers in the pipeline language.
- Type a message in the Assist dialog of the Home Assistant app. Marshal treats it as a text turn.
- Open **Runs** in the Marshal web UI. The turn appears with its satellite and area.

## How a turn behaves

| Behavior | Detail |
|---|---|
| Streaming | Marshal streams the reply, so Home Assistant starts speaking before the reply is complete. Status messages are never spoken. |
| Spoken filler | Before a slow tool, such as a printer snapshot, Marshal speaks one short line ("Let me look at the printer.") when nothing was said yet. At most one per turn. Tune the threshold with `voice.fillerThresholdMs`; see [`tools`](../../configuration/marshal-yaml/tools.md). |
| Voice or text | Marshal treats turns that went through speech-to-text as voice, typed turns as text. |
| Latency cap | A voice turn that exceeds `agent.voiceLatencyCap` (default 8 seconds) gets a short "working on it" reply. The work continues as a background task and the result arrives by notification. See [`agent`](../../configuration/marshal-yaml/agent.md). |
| Questions | When Marshal asks you something (a confirmation, a clarification), the satellite keeps listening. |
| Errors | If Marshal is unreachable or fails, the satellite speaks a short error in the turn's language. |

## Troubleshooting

- "Marshal refused access": the token is wrong, revoked or lacks `conversation:write`. Create a new token and reconfigure the integration.
- "Marshal is not reachable right now": check the URL in the integration and the [health endpoint](../../reference/health-endpoints.md).

More in [component troubleshooting](../../home-assistant-component/troubleshooting.md).

## Related

- [Satellites](satellites.md)
- [Voice follow-ups](follow-ups.md)
- [Conversation API](../../api/conversation.md)
- [How the component works](../../home-assistant-component/how-it-works.md)
