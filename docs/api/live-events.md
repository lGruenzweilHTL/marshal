# Live events

`GET /api/v1/events` is a server-sent event stream with live updates for the web interface. It multiplexes several topics over one connection and delivers only events of the caller's owner.

## Request

```http
GET /api/v1/events?topics=runs,tasks
Accept: text/event-stream
```

| Parameter | Description |
|---|---|
| `topics` | Comma-separated topic names. Required. |

Any authenticated principal (cookie session or valid token) may subscribe. An empty `topics` returns `400` `events.topics_required`; an unknown topic returns `400` `events.unknown_topic` naming it.

## Stream format

The SSE event name is the event `type`. Every `data` line is an envelope:

```json
{ "topic": "runs", "type": "run.completed", "id": "…", "payload": { } }
```

The SSE `id` field repeats the envelope `id`. The stream does not replay missed events; after a reconnect, reload the current state from the REST endpoint.

Marshal sends a `ping` event (`{"topic":"system","type":"ping","id":"","payload":{}}`) every 25 seconds so proxies keep the connection open. Ignore it.

## Topics and events

| Topic | Event | Payload |
|---|---|---|
| `runs` | `run.started` | `runId`, `source` (`voice`, `web`, `task`, `api`), `trigger`, `startedAt` |
| `runs` | `run.completed` | `runId`, `outcome` (`ok`, `error`, `backgrounded`, `cancelled`), `cost`, `endedAt` |
| `printer` | `printer.state` | `printer` (name), `state` (new printer state, `null` when it went offline) |
| `tasks` | `task.changed` | `taskId`, `name`, `status`, `statusReason`, `nextRunAt`, `resultAt` |
| `notifications` | `notification.created`, `notification.updated` | `id`, `source`, `priority`, `text`, `outcome`, `channel`, `reason`, `taskId`, `createdAt` |
| `artifacts` | `artifact.created` | `id`, `name`, `kind`, `source` |
| `artifacts` | `artifact.deleted` | `id` |
| `health` | `health.changed` | `warnings`: the current [health warnings](../reference/health-endpoints.md#health-warnings), each with `component`, `code`, `severity`, `message` (`de`, `en`), `link`, `since` |
| `conversations` | `conversation.result` | `sessionId`, `taskId`, `resultAt`, `body`: a background task finished work handed off from that session |

Field names are camelCase. For the exact types of enum values, see the [OpenAPI document](openapi.md); the OpenAPI description covers only the envelope, and the payloads are defined by the web interface's `web/src/live/events.ts`.

## Related

- [Events reference](../reference/events.md)
- [Examples](examples.md)
