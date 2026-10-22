# Proposal

## Why

The v1 OpenAPI document names every operation with a sentence-long summary ("Runs one conversation turn and streams the reply as server-sent events …"), so generated references such as the docs site's API pages show those sentences as page titles and navigation entries. The document also declares no security at all, so readers and generated clients cannot tell which endpoint takes a machine token, a browser session or nothing, and the docs build has to inject guessed security metadata.

## What Changes

- Each operation's summary becomes its route below the version base, e.g. `/conversation/turns` for `POST /api/v1/conversation/turns`. The current summary text moves to the operation's description.
- The document declares two security schemes: `apiToken` (HTTP bearer, a machine token) and `session` (the `marshal_session` cookie of the web interface).
- Each operation carries the security requirement its endpoint actually enforces, derived from the endpoint's authorization metadata: none for anonymous endpoints, the session only for endpoints limited to people, a token with the required scope or a session for scope-protected endpoints, and so on.
- Generating the document fails when an endpoint uses an authorization policy the mapping does not know, so a new policy cannot ship undocumented.
- The docs prep script (`scripts/docs-openapi.mjs`) stops adding security; it only adds servers.
- No path, parameter, request or response changes. The v1 wire contract stays as published.

## Non-goals

- No CORS support and no change to which credential an endpoint accepts.
- No per-scope OAuth flows or OpenID Connect description of the login providers.
- No change to operation ids, tags or schemas.
- No new API version: summaries, descriptions and security metadata document the existing contract and do not change it.

## Capabilities

### New Capabilities

### Modified Capabilities
- `conversation-api`: adds a requirement for the content of the versioned OpenAPI documents (operation names and security requirements).

## Impact

- `Marshal.Api`: OpenAPI configuration (operation and document transformers), endpoint registrations (`WithSummary` text moves to `WithDescription`), authorization policy catalog.
- `web/openapi/v1.json` and `web/src/api/schema.d.ts` regenerate (comments only).
- `scripts/docs-openapi.mjs`, `docs/api/openapi.md`.
