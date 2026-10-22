---
icon: fa-solid fa-comments
order: 20
---

# Conversation endpoint

`POST /api/v1/conversation/turns` runs one conversation turn and streams the reply as server-sent events. The Home Assistant component uses it for every Assist turn; you can use it from any client.

## Request

Requires the scope `conversation:write` (see [authentication](authentication.md)). Body is JSON.

| Field | Type | Required | Description |
|---|---|---|---|
| `text` | string | yes | What the user said or typed. Empty or whitespace returns `400` `conversation.text_required`. |
| `conversationId` | string | token clients: yes | Token clients send their own conversation id and reuse it for follow-up turns. Browser sessions omit it to start a conversation, or send Marshal's session id from an earlier `final` event. |
| `language` | string | no | `de` or `en`. The reply uses it. |
| `inputMode` | string | no | `voice` if the text came from speech-to-text, otherwise `text` (default). |
| `deviceId` | string | no | Home Assistant device id. |
| `satelliteId` | string | no | Satellite entity id, for example `assist_satellite.kitchen`. Selects the [satellite profile](../configuration/satellites-yaml.md). |
| `areaId` | string | no | Area id of the satellite. |
| `haUserId` | string | no | Home Assistant user id, if known. |
| `followupRef` | string | no | Nonce from a [voice follow-up](../guides/voice/follow-ups.md). Unknown or expired values are ignored. |

```json
{
  "conversationId": "01JABC",
  "text": "How far along is the print?",
  "language": "en",
  "inputMode": "text"
}
```

::: request v1 CreateConversationTurn
body:
  conversationId: "01JABC"
  text: How far along is the print?
  language: en
  inputMode: text
:::

A token client without `conversationId` gets `400` `conversation.id_required`. A browser session that sends a session id it does not own, or one that has ended, gets `404` `session.not_found` and no run starts.

A turn that carries `satelliteId`, `deviceId` or `haUserId` is recorded as a voice session; otherwise as an API session.

## Response

`200` with `Content-Type: text/event-stream`. Each event has a name and a JSON `data` line. Send `Accept: text/event-stream`.

| Event | Data | When |
|---|---|---|
| `delta` | `{ "text": "…" }` | Zero or more times. A chunk of the reply text. In a voice turn the first delta can be a short spoken filler sent before a slow tool runs; it is part of `speech`. |
| `status` | `{ "text": "…" }` | Optional. A progress message, for example when a turn is moving to the background. Do not speak it. |
| `artifact` | `{ "id", "name", "kind", "url" }` | Once per artifact the turn shows. `url` is the content URL. |
| `reasoning` | `{ "text": "…" }` | Browser sessions only. Zero or more times. A chunk of the model's reasoning. |
| `tool` | `{ "id", "tool", "arguments", "state", "durationMs", "result" }` | Browser sessions only. Twice per tool call: when it starts (`state` `running`) and when it ends (same `id`, `state` `ok`, `error`, `denied` or `pending`, with `durationMs` and an optional `result`). |
| `widget` | `{ "kind", "toolCallId", "data" }` | Browser sessions only. After a successful call of a tool with a built-in widget: `kind` `printer` (from `printer.status`) or `music` (from `music.queue`, `music.play`). `data` is the widget's starting state. |
| `app` | `{ "appId", "server", "tool", "toolCallId" }` | Browser sessions only. After a call of an MCP server tool with an app (`apps: true`). Load it from `GET /api/v1/apps/{appId}`; its HTML is at `/mcp-apps/{appId}`. |
| `final` | `{ "speech", "continueConversation", "conversationId" }` | Exactly once, last. |

Token clients (Home Assistant, API) never receive `reasoning`, `tool`, `widget` or `app` events. Their stream is unchanged.

The `tool` event fields:

| Field | Description |
|---|---|
| `id` | The call id. The ending event repeats it. |
| `tool` | The tool id, for example `printer.status`. |
| `arguments` | A one-line summary of the arguments (`key=value` pairs, cut to 120 characters). |
| `state` | `running`, `ok`, `error`, `denied` (the guard refused it) or `pending` (waits for confirmation). |
| `durationMs` | How long the call took. Ending events only. |
| `result` | The first line of the result (cut to 120 characters) or the error code. Absent for sensitive and redacted tools. |

The `final` event fields:

| Field | Description |
|---|---|
| `speech` | The full reply text. Equal to the concatenated deltas, or the whole reply if none were streamed. |
| `continueConversation` | `true` when Marshal asked something (a confirmation or clarification) and the client should keep listening. |
| `conversationId` | Token clients: the id you sent. Browser sessions: Marshal's session id. |

```text
event: delta
data: {"text":"The print is "}

event: delta
data: {"text":"at 42 percent."}

event: final
data: {"speech":"The print is at 42 percent.","continueConversation":false,"conversationId":"01JABC"}

```

Validation errors (`400`, `404`, `403`) are returned as [problem details](index.md#errors) before the stream starts. After the stream starts, read until `final`. A stream that ends without `final` is an error.

## Latency cap

Each turn has a hard latency cap: `agent.voiceLatencyCap` (default 8 seconds) for voice turns and `agent.textLatencyCap` (default 60 seconds) for text turns. When a turn reaches the cap, Marshal sends a short reply that it is working on it and will report back, ends the turn, and finishes the work as a background task owned by the same user. Voice results arrive on the originating satellite; text results arrive in the web interface. The model can also move a turn to the background itself.

The background task records the session it came from. Its result joins that conversation: the web chat history lists it after the backgrounded turn, the open chat shows it live (`conversation.result` on the [live-event stream](live-events.md)), and the model sees it as an earlier reply in the next turn of the session.

Set client timeouts above the text cap. The Home Assistant component allows 75 seconds in total. See [agent configuration](../configuration/marshal-yaml/agent.md).

## Confirmations

When a turn ends with a pending confirm-tier action, `continueConversation` is `true`. The user's next turn (same `conversationId`) answers the question. Marshal freezes the action on the server; only a user turn, a web approval or a verified push action confirms it. See [risk and confirmation](../concepts/risk-and-confirmation.md).

## Related

- [Examples](examples.md)
- [How the Home Assistant component handles turns](../home-assistant-component/how-it-works.md)
