## Why

After `core-agent`, Marshal can only be reached with a machine token. Later sub-projects need a logged-in human in a browser: OAuth connect flows for mail accounts, approvals, the review queue, memory editing and the printer page. This change (sub-project 2 of 7) adds human login and the web shell every later view plugs into, plus the first user-facing views: chat, tokens, secrets, preferences, runs and cost.

## What Changes

- Human login with pluggable providers from config: `oidc` (generic; Authentik, Keycloak, Authelia, Pocket ID, Entra, Google) and `local` (ASP.NET Core Identity with passkeys and password fallback, first-run admin setup). Several providers may be active at once.
- BFF pattern: the backend is the OIDC confidential client (code + PKCE); the browser gets an HttpOnly, Secure, `SameSite=Lax` cookie; antiforgery on all state-changing requests.
- One user store: ASP.NET Core Identity. `User` becomes the Identity user (`IdentityUser<Guid>`), OIDC logins are external logins matched by `sub`, roles re-synced from group claims on every OIDC login, deny without a mapped group.
- Cookie principals get the scopes their role grants and go through the same policies as tokens; the conversation endpoint accepts cookie clients.
- Auth endpoints: `/api/v1/auth/providers`, `/api/v1/auth/login/{provider}`, `/api/v1/auth/logout`, `/api/v1/me`.
- Vue + Vite + TypeScript SPA in `web/`, typed client generated from `/openapi/v1.json` (`openapi-typescript` + `openapi-fetch`), built into `wwwroot` by the Docker build, served same-origin, responsive.
- Multiplexed live-update stream `GET /api/v1/events?topics=...` (SSE) for later topics.
- Views: dashboard shell, chat, runs / conversations, cost, security (API tokens, login providers), secrets (write-only), preferences (language, time zone, quiet hours, notification targets, DND), system (health).
- Token management and DB secret management through the API and UI.

## Non-goals

- No user management UI, sharing or speaker identification (multi-user is not built).
- No HA sidebar panel (iframe breaks OIDC and cookies); at most a link.
- No views for memory, tasks, review queue, notifications, printer, accounts or config; each later change adds its own.
- No use of quiet hours, DND or notification targets yet: this change only stores them (used by `background-work`).
- No invite flow for `local` beyond the first-run admin (optional, later).

## Capabilities

### New Capabilities
- `human-auth`: login providers, BFF cookie, Identity user store, roles, provisioning, auth endpoints, antiforgery.
- `web-shell`: SPA build, hosting, generated API client, layout, responsive design, CSP, live-update event stream.
- `web-chat`: text chat view, streaming, markdown rendering rules, confirmations in chat.
- `user-preferences`: per-user language, time zone, quiet hours, notification targets and DND, API and view.
- `run-insights`: runs / conversations list and detail, cost view, dashboard shell with health.

### Modified Capabilities
- `conversation-api` (introduced by `core-agent`): adds cookie clients and role-derived scopes.
- `machine-tokens` (introduced by `core-agent`): adds token management API and UI.
- `secret-store` (introduced by `core-agent`): adds DB secret management API and write-only UI.

## Impact

- New: `web/` (Vue SPA), auth code in `Marshal.Api`, Identity tables in `Marshal.Data` (migration turns `User` into the Identity user; adds `UserLogin`, `UserPasskey`, `Role`, `UserRole`).
- New dependencies: `Microsoft.AspNetCore.Authentication.OpenIdConnect`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `vue`, `vite`, `typescript`, `openapi-typescript`, `openapi-fetch`.
- Dockerfile gains the SPA build stage.
- Depends on `core-agent` being archived first.
