## Context

`core-agent` delivers the conversation core with machine tokens only. Design sources: brainstorm notes §18 (web UI), §19 (auth and users), §20 (packaging). The instance is single-user today, but the auth model must not assume it. Marshal is LAN / VPN only behind a TLS reverse proxy.

## Goals / Non-Goals

**Goals:**
- `oidc` and `local` login on one Identity user store, BFF cookie, antiforgery, role provisioning.
- Same authorization policies for cookie and token principals.
- SPA skeleton with generated client, live-update stream, and the first views.

**Non-Goals:**
- User management, invites, sharing.
- Views owned by later changes (memory, tasks, review queue, notifications, printer, accounts, config).
- Applying quiet hours / DND (only stored here).

## Decisions

### D1. ASP.NET Core Identity as the only user store
`User` from `core-agent` becomes `IdentityUser<Guid>` + `DisplayName` via migration; its key is converted to `OwnerId` everywhere else, so no other table references Identity types. OIDC logins are `AspNetUserLogins` rows (`provider`, `sub`); passkeys use .NET 10 Identity passkey support (`AspNetUserPasskeys`).
*Alternative:* separate user table for OIDC and Identity for local — rejected, two stores for one concept and break-glass would need linking.

### D2. BFF with standard handlers
Cookie authentication + `OpenIdConnect` handler (code flow, PKCE, `SaveTokens = true` into the server-side auth ticket store). To keep the cookie small and tokens off the client, tickets are stored server-side with an `ITicketStore` backed by the DB (`AuthTicket` table, owner-scoped, expiring). `SameSite=Lax` so push deep links arrive logged in; antiforgery header token (`X-XSRF-TOKEN`) for every non-GET cookie request.
*Alternative:* SPA as public OIDC client with tokens in the browser — rejected (token exposure, SPA must know OIDC).

### D3. Scopes from roles
A claims transformation adds `scope` claims to cookie principals: `user` role → every scope except `admin`; `admin` → all. Endpoint policies are defined once per scope (`Policies.ConversationWrite`, ...), so cookie and token principals share them.

### D4. Interfaces introduced
| Interface | Purpose | v1 implementation |
|---|---|---|
| `ILoginProvider` | describes one configured provider for `/auth/providers` and login start | `OidcLoginProvider`, `LocalLoginProvider` |
| `IRoleProvisioner` | claims → roles, deny when unmapped | `GroupClaimRoleProvisioner` |
| `IUserPreferencesStore` | per-user preferences | `EfUserPreferencesStore` |
| `ILiveEventPublisher` / `ILiveEventStream` | publish owner-scoped events to SSE subscribers | `InProcessLiveEvents` (`Channel<T>` per subscriber) |
| `ITicketStore` (ASP.NET) | server-side auth tickets | `EfTicketStore` |

No new model-facing tools.

### D5. SPA structure
`web/` with Vite, Vue 3, Vue Router, TypeScript strict. Folders by feature (`chat/`, `runs/`, `security/`, `secrets/`, `preferences/`, `system/`, `dashboard/`). `npm run gen:api` runs `openapi-typescript` against the OpenAPI document produced by a build-time run of the API (`dotnet run -- --export-openapi`), so CI needs no running server. Markdown via `markdown-it` with a custom image rule (same-origin → `<img>`, otherwise link) and HTML disabled. Component tests with Vitest + Vue Test Utils, API client mocked.

### D6. Live-update stream
`GET /api/v1/events?topics=` returns `TypedResults.ServerSentEvents`. Topics are registered by feature (`runs` now; `tasks`, `approvals`, `notifications`, `printer` later). One stream per browser tab avoids the HTTP/1.1 connection limit. Events carry `topic`, `type`, `id`, payload; owner filtering happens in the publisher.

### D7. Instance time zone
`instance.timeZone` in `marshal.yaml` (IANA, default `UTC`) seeds new users. When `extensibility` adds the HA client, HA's configured time zone replaces the config default if `instance.timeZone` is not set.

## Risks / Trade-offs

- [Identity migration of the `User` table from `core-agent`] → done in a single migration before any real data exists; the instance owner row keeps its id.
- [`SameSite=Lax` allows top-level cross-site GET navigations with the cookie] → no state-changing GETs; antiforgery on all other methods.
- [Server-side ticket store adds a DB read per request] → cached in memory with short TTL.
- [IdP down = no login] → local break-glass admin can be added to the same user.

## Migration Plan

One EF migration converting `User` to Identity and adding Identity tables, `AuthTicket`. Existing machine tokens keep working. Rollback: previous image + automatic pre-migration DB copy.

## Open Questions

- Passkey API surface in the shipped .NET 10 Identity version — verify when building.
- Vue remains LEANING; switching later only affects `web/`.

## Implementation Notes

- **D1 adjusted: `User` is `IdentityUser<OwnerId>`, not `IdentityUser<Guid>`.** EF Core refuses a relationship whose
  foreign key (`OwnerId` on every user-owned table) and principal key (`Guid`) have different CLR types, even when the
  value converter maps both to the same column type. Keying Identity by `OwnerId` keeps the typed id on every table
  and the foreign keys in the database. `OwnerId` got a `TypeConverter` (`GuidIdTypeConverter<T>`) because Identity
  converts keys from their string form. Identity uses one key type for users and roles, so role ids are
  `OwnerId`-typed as well; they never leave the user store.
- **Identity schema version 3 (passkeys)** is configured in `MarshalDbContext` itself: Identity reads the schema
  version from the host's `IdentityOptions`, which the design-time factory and test contexts do not have. The base
  model (schema 1) plus the schema 3 additions (passkey table with JSON `data`, key lengths 128, phone length 256)
  keep migrations independent of the host.
- **Identity tables** are `users`, `roles`, `user_roles`, `user_logins`, `user_passkeys`, and the unused-but-required
  `user_claims`, `user_tokens`, `role_claims` (the EF user store expects them). They are the user store itself, keyed
  by user id and accessed only through ASP.NET Core Identity, so they carry no owner query filter.
- **Migration `IdentityUsers`** adds the Identity columns to `users` (the instance owner keeps its id) and backfills
  `normalized_user_name` so Identity can find pre-existing users.
