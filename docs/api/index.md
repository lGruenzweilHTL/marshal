# API overview

Marshal exposes a versioned REST API under `/api/v{n}`. This page covers the base path, versioning, authentication, error format and rate limits that apply to every endpoint.

The public, integration-relevant endpoints are the conversation endpoint, the live event stream, inbound webhooks and version discovery. All other endpoints exist for Marshal's own web interface; see [SPA-internal endpoints](#spa-internal-endpoints).

## Base path

All versioned endpoints live under `/api/v1`. If you serve Marshal under a path prefix (`http.pathBase`), put the prefix in front of every path, for example `https://example.com/marshal/api/v1/conversation/turns`.

JSON is camelCase. Time stamps are ISO 8601 strings.

| Path | Versioned | Authentication | Purpose |
|---|---|---|---|
| `POST /api/v1/conversation/turns` | yes | token or session | [One conversation turn, streamed](conversation.md) |
| `GET /api/v1/events` | yes | token or session | [Live update stream](live-events.md) |
| `POST /api/v1/hooks/{source}` | yes | token | [Inbound webhooks](webhooks.md) |
| `GET /api/v1/me` | yes | token or session | Current principal: id, display name, roles, scopes, demo mode |
| `GET /api/versions` | no | none | Supported and deprecated API versions |
| `GET /openapi/v1.json` | no | none | [OpenAPI document](openapi.md) |
| `GET /health` | no | none | Liveness and readiness; see [health endpoints](../reference/health-endpoints.md) |

Unversioned endpoints also include the OIDC protocol callbacks (`/signin-oidc`, `/signout-callback-oidc`) and the web interface routes.

## Versioning

The API version is part of the URL (`/api/v1/...`). Marshal declares the versions it serves in code; you cannot change them in configuration.

- **Published versions do not change in place.** A breaking change ships as a new version (`/api/v2/...`) that runs next to the old one.
- **Deprecated versions** keep working and are listed under `deprecated` in version discovery before they are removed.
- Every versioned response carries an `api-supported-versions` header.

Discover the versions without authentication:

```http
GET /api/versions
```

```json
{ "supported": ["1.0"], "deprecated": [] }
```

A request to an unknown route or an unsupported version, such as `/api/v9/conversation/turns`, returns `404` with the problem code `api.unsupported_version` and the list of supported versions in the `supportedVersions` extension.

The [Home Assistant component](../home-assistant-component/how-it-works.md) calls `/api/versions` during setup and refuses to connect when version `1.0` is missing.

## Authentication

Two kinds of callers exist. Both are authorized by the same scope policies.

| Caller | Credential | Used by |
|---|---|---|
| Machine client | `Authorization: Bearer <token>` with a scoped API token | Home Assistant component, scripts, webhook senders |
| Person in a browser | `marshal_session` cookie after login, plus an antiforgery header on writes | Marshal's web interface |

A request with an `Authorization: Bearer` header is always treated as a token request. Details, scopes and token management are in [authentication](authentication.md).

## Errors

Expected failures return [problem details](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`). The `title` and the `code` extension carry a stable, machine-readable error code. Match on `code`, not on `detail`, which is human-readable text and may change.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "conversation.text_required",
  "status": 400,
  "detail": "text is required.",
  "code": "conversation.text_required"
}
```

| Status | Meaning |
|---|---|
| `400` | The request is invalid (missing field, unknown topic, unreadable body). |
| `401` | No credential, or the token is unknown, revoked or expired. |
| `403` | The credential lacks the required scope. |
| `404` | The resource, source or API version does not exist, or belongs to another user. |
| `409` | The request conflicts with the current state. |
| `413` | The request body is too large (webhooks: over 64 KB). |
| `422` | The request is well-formed but the operation failed. |
| `429` | Rate limit exceeded (webhooks only). |
| `503` | A required dependency is unavailable. |

An unhandled server error returns `500` with the code `server.unexpected`, a generic `detail` and a `traceId` extension. Quote the trace id when you look for the matching entry in the server log; the response never contains exception text.

## Rate limits

Only the webhook endpoint is rate limited: each token may send `webhooks.requestsPerMinute` requests per minute (default `60`, fixed one-minute window). Excess requests get `429` with the code `webhook.rate_limited`. See [webhooks](webhooks.md#flood-control).

The conversation endpoint has no request-rate limit. Each turn has a latency cap instead: when a turn takes too long, Marshal answers that it is working on it and finishes the work as a background task. See [conversation](conversation.md#latency-cap).

## SPA-internal endpoints

The web interface uses the following groups. They require a logged-in person (cookie session), are not meant for machine clients, and are not covered by a stability promise beyond the versioning rules above. Their request and response shapes are in the [OpenAPI document](openapi.md).

| Group | Paths under `/api/v1` | Purpose |
|---|---|---|
| Auth | `/auth/providers`, `/auth/login/{provider}`, `/auth/logout`, `/auth/antiforgery`, `/auth/local/*` | Login, logout, first-run setup, passkeys |
| Tokens and secrets | `/me/tokens`, `/me/secrets`, `/secrets/instance` | Manage API tokens and UI-managed secrets |
| Preferences | `/me/preferences` | Per-user settings |
| Runs and cost | `/runs`, `/runs/{id}`, `/cost` | Run recording and cost |
| Conversations | `/conversations`, `/conversations/{id}` | Chat history: web, voice and API conversations with their turns and background results |
| Tasks | `/tasks`, `/tasks/{id}`, `/tasks/kill-switch` | Task list and kill switch |
| Approvals | `/approvals` | Approval queue |
| Notifications | `/notifications` | Notification history |
| Memory | `/memory/*` | Chunks, projects, graph, export |
| Printers | `/printers/*` | Status, files, controls, snapshots, webcam |
| Mail | `/mail/*` | Mail account setup and OAuth |
| Artifacts | `/artifacts/*` | Upload, list, content, previews |
| Displays | `/displays/*` | Display surfaces |
| Proposals | `/proposals/*` | Review and merge agent proposals |
| System and config | `/system`, `/config` | System status and configuration summary (admin) |
| Executor callbacks | `/executor/callbacks/*` | Used by the executor host with its own callback token |

## Related

- [Authentication](authentication.md)
- [Conversation endpoint](conversation.md)
- [Live events](live-events.md)
- [Webhooks](webhooks.md)
- [OpenAPI and client generation](openapi.md)
- [Examples](examples.md)
- [API versioning (contributors)](../development/api-versioning.md)
