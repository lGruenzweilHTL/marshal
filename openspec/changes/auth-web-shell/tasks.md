## 1. Identity user store

- [x] 1.1 Write tests: `User` is an Identity user with key exposed as `OwnerId`; instance owner keeps its id after migration
- [x] 1.2 Migrate `User` to `IdentityUser<Guid>`, add `UserLogin`, `UserPasskey`, `Role`, `UserRole`; update `erd.puml` and re-render
- [x] 1.3 Write tests for `EfTicketStore` (store, renew, expire, owner-scoped)
- [x] 1.4 Implement `EfTicketStore` and `AuthTicket` table; add `AuthTicket` to `erd.puml` and re-render

## 2. Login providers

- [x] 2.1 Write tests: provider list endpoint, config validation (`clientCredentialSecret` resolves via `ISecretStore`)
- [x] 2.2 Implement provider options, `ILoginProvider`, `/api/v1/auth/providers`, `/auth/login/{provider}`, `/auth/logout`
- [x] 2.3 Write OIDC tests against a fake IdP: code + PKCE, cookie flags, no tokens to browser, match by `sub`, email change keeps user
- [x] 2.4 Implement OIDC handler wiring and external-login mapping
- [x] 2.5 Write provisioning tests: group → role mapping, unmapped denied, roles re-synced on login
- [x] 2.6 Implement `GroupClaimRoleProvisioner`
- [x] 2.7 Write local provider tests: first-run setup creates admin, setup 404 afterwards, password login, passkey registration and login
- [x] 2.8 Implement local provider with Identity passwords and passkeys

## 3. Authorization

- [x] 3.1 Write tests: cookie principal scopes from roles, same policies for cookie and token, antiforgery required on cookie POST
- [x] 3.2 Implement scope claims transformation, scope policies and antiforgery middleware
- [x] 3.3 Write tests for `/api/v1/me` (authenticated, anonymous 401)
- [x] 3.4 Implement `/api/v1/me`

## 4. Conversation endpoint for cookie clients

- [x] 4.1 Write tests: new web session without id, own session continued, foreign session 404, session id returned
- [x] 4.2 Extend the turn endpoint and session lookup for cookie principals

## 5. Tokens, secrets and preferences APIs

- [x] 5.1 Write token management tests: list / create / revoke, no scope escalation, callback tokens read-only, audit events
- [x] 5.2 Implement `/api/v1/me/tokens`
- [x] 5.3 Write secret management tests: write-only, admin-only instance secrets, env secrets listed without value, unresolved references flagged, audit without value
- [x] 5.4 Implement `/api/v1/me/secrets`, `/api/v1/secrets/instance` and reference scanning
- [x] 5.5 Write preferences tests: read / update, IANA validation, default time zone from `instance.timeZone`
- [x] 5.6 Implement `IUserPreferencesStore` and `/api/v1/me/preferences`

## 6. Live-update stream

- [x] 6.1 Write tests: owner filtering, unknown topic 400, multiple topics on one stream
- [x] 6.2 Implement `InProcessLiveEvents` and `GET /api/v1/events`; publish `runs` topic events from the run recorder

## 7. Runs and cost API

- [x] 7.1 Write tests for runs list filters, run detail fields (guard decisions, reasons, taint sources, redacted results) and cost aggregation per day / tier / advisor
- [x] 7.2 Implement runs and cost endpoints

## 8. SPA

- [x] 8.1 Scaffold `web/` (Vue 3, Vite, TypeScript strict, Vue Router, Vitest); add OpenAPI export mode to the API and `gen:api` script
- [x] 8.2 Write component tests for login page and app shell (provider buttons, logged-in layout, logout)
- [x] 8.3 Implement login page, layout, navigation, responsive shell
- [x] 8.4 Write chat component tests: streaming deltas, status events, new conversation, confirmation question with reason, external image rendered as link
- [x] 8.5 Implement chat view with safe markdown rendering
- [ ] 8.6 Write tests for runs list / detail and cost views
- [ ] 8.7 Implement runs, cost, dashboard (recent runs, today's cost, health) and system views
- [ ] 8.8 Write tests for security (tokens), secrets and preferences views
- [ ] 8.9 Implement security, secrets and preferences views

## 9. Hosting and packaging

- [ ] 9.1 Write API test: SPA fallback for deep links, CSP header (`img-src 'self' data:`), base path respected
- [ ] 9.2 Serve SPA from `wwwroot` with fallback and CSP
- [ ] 9.3 Add SPA build stage to the Dockerfile; update `packages.puml`, `CLAUDE.md` package index (incl. `markdown-it`, Vitest, Vue Router) and repository map
