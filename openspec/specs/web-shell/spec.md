# web-shell Specification

## Purpose
The web app shell: SPA hosting, the generated API client, responsive layout, content security policy and the live-update stream.
## Requirements
### Requirement: SPA hosting
The web UI SHALL be a Vue + Vite + TypeScript SPA in `web/`, compiled into `wwwroot` by the Docker build and served same-origin by `Marshal.Api` under the configured base path. Unknown non-API paths SHALL return the SPA entry page.

#### Scenario: Deep link
- **WHEN** the browser requests `/runs/123` directly
- **THEN** the SPA entry page is returned and the SPA renders the run

### Requirement: Generated API client
The SPA SHALL use TypeScript types generated from `/openapi/v1.json` with `openapi-typescript` at build time and call the API through `openapi-fetch`. An API contract change that breaks the SPA SHALL fail the SPA build.

#### Scenario: Removed field
- **WHEN** a DTO field used by the SPA is removed from the v1 OpenAPI document
- **THEN** `npm run build` fails with a type error

### Requirement: Responsive layout
All views SHALL be usable on a phone-sized screen. Views that push notifications deep-link to (chat, approvals, task results, printer, notification history) SHALL be designed mobile-first.

#### Scenario: Phone width
- **WHEN** the chat view is rendered at 375 px width
- **THEN** there is no horizontal scrolling and all actions are reachable

### Requirement: Content Security Policy
The SPA SHALL be served with a CSP that allows scripts and styles only from the same origin and images only from `'self'` and `data:`.

#### Scenario: External image blocked
- **WHEN** a page tries to load an image from another origin
- **THEN** the browser blocks it by CSP

### Requirement: Live-update stream
`GET /api/v1/events?topics=...` SHALL deliver server-sent events for the requested topics over one connection, filtered to the current owner. Unknown topics SHALL be rejected. Event payload types SHALL be shared through a small hand-written TypeScript module.

#### Scenario: Owner filtering
- **WHEN** an event for owner B is published and owner A is connected
- **THEN** A does not receive it

#### Scenario: Unknown topic
- **WHEN** a client requests `topics=foo`
- **THEN** the response is 400 naming the unknown topic

