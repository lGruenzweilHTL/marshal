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
- **First login claims the instance owner** (spec change agreed with the user): `core-agent` seeds the instance
  owner on first start, so "no user exists" never holds. The owner counts as *unclaimed* while it has no password,
  passkey or external login. The local first-run setup and the first OIDC login with a mapped group claim it (same
  id, so voice sessions and CLI tokens stay with the human who logs in); afterwards setup returns 404 and new OIDC
  subjects create new users.
- **Passkeys verified (open question).** .NET 10 Identity ships passkeys on `SignInManager<TUser>`:
  `MakePasskeyCreationOptionsAsync(PasskeyUserEntity)` / `PerformPasskeyAttestationAsync(credentialJson)` for
  registration and `MakePasskeyRequestOptionsAsync(user?)` / `PerformPasskeyAssertionAsync(credentialJson)` for
  login; options and credentials are WebAuthn JSON (`PublicKeyCredential.toJSON()`). The ceremony state lives in a
  Data Protection cookie of the `IdentityConstants.TwoFactorUserIdScheme` scheme (registered here as
  `marshal_passkey`, 5 minutes, SameSite=Strict). Origin validation compares the client data origin with the
  request's `Origin` header. `IdentityPasskeyOptions.ServerDomain` (RP id) defaults to the request host;
  `auth.providers[local].serverDomain` sets it. Marshal calls `PerformPasskeyAssertionAsync` and signs in with its
  own session principal (not `PasskeySignInAsync`), after the lockout / `CanSignInAsync` checks and storing the new
  sign count. Registration checks that the attestation names the signed-in user. Tests drive both ceremonies with a
  fake ES256 authenticator (`none` attestation).
- **Local endpoints** live under `/api/v1/auth/local` (`setup`, `login`, `passkey/options`, `passkey`,
  `passkeys/creation-options`, `passkeys`); passwords need 12+ characters without composition rules, 5 failures
  lock the account for 5 minutes.
- **OIDC callbacks** are `/signin-oidc/{name}` and `/signout-callback-oidc/{name}` so several OIDC providers can be
  active; they stay unversioned. The OIDC schemes are registered through `AuthenticationOptions` from the bound
  `auth` options (not `AddOpenIdConnect`), because configuration added by the test host is not visible while services
  are registered. The IdP redirect URI to configure is `https://<host>/signin-oidc/<name>`.
- **Antiforgery** is a small middleware after authorization: every state-changing `/api` request that is not
  bearer-authenticated (cookie sessions and the anonymous login / setup calls) needs `X-XSRF-TOKEN`; the SPA fetches
  it from `GET /api/v1/auth/antiforgery` and again after login or logout (tokens are bound to the user). Such requests
  must arrive as https (`auth.https_required` otherwise); `http.trustForwardedHeaders` (default on) honors the TLS
  proxy's `X-Forwarded-*` headers.
- **Credential management is human-only.** `/me/tokens` and the secret endpoints require a cookie session
  (`Policies.Human`) on top of the scope policies, so a leaked machine token cannot mint tokens or change secrets.
  `/me` works for both principal kinds. An `OwnerContextMiddleware` makes the principal's owner the request's
  execution context, so endpoint queries go through the owner filter.
- **`instance.timeZone` replaces `owner.timeZone`** (a core-agent config key): the seeded instance owner and every
  new user start with the instance time zone (`IInstanceTimeZone`, UTC when unset; `extensibility` adds Home
  Assistant's). New OIDC users get a stored preferences row at creation. Preferences are validated by
  `UserPreferencesRules` (IANA ids via `TimeZoneInfo.HasIanaId`, `person.*` / `notify.*` targets).
- **Live events:** `ILiveEventStream` from D4 is named `ILiveEventSource` (CA1711 forbids the `Stream` suffix) and
  returns an `ILiveSubscription` that starts buffering at subscribe time, so events published before the SSE response
  starts are not lost. Topics register as `LiveTopic` singletons (`runs` from Marshal.Core); `LiveRunObserver`
  publishes `run.started` / `run.completed` from the run recorder's observer hook. The SSE event name is the event
  type, the data a `LiveEventMessage` (`topic`, `type`, `id`, `payload`); a `ping` event every 25 s keeps proxies
  from closing idle streams. Each subscriber has a bounded buffer (256) that drops the oldest events.
- **Runs and cost API:** `IRunQueries` (`EfRunQueries`) backs `GET /runs` (filters `source`, `outcome`, `minCost`,
  paging with `before` + `limit`), `GET /runs/{id}` and `GET /cost?from&to`. Cost sums the recorded model-call
  costs attributed to the local day their run started, in the user's time zone - the same basis as the advisor
  budget (`EfAdvisorUsage`); the advisor tier is reported on its own line and not in `byTier`. Enum values in the
  API are camelCase (`escalatedByTaint`); the spec's snake_case names are the stored values.
- **SPA login flow:** OIDC buttons are plain links to `/api/v1/auth/login/{name}?returnUrl=...`; the local provider
  has its own page (password, passkey via the browser's WebAuthn JSON helpers behind a small `Passkeys` interface)
  and the first-run setup page is linked while `GET /auth/local/setup` answers 200. Logout is a real form post with
  the antiforgery token in the `__RequestVerificationToken` form field (ASP.NET Core antiforgery reads the header or
  that field), so the browser follows the redirect to the IdP's end-session endpoint.
- **Chat confirmations:** the final event keeps its `{ speech, continueConversation, conversationId }` shape. The
  confirmation reason reaches the chat inside the speech: the runner tells the model to include it (core-agent),
  and the chat marks a reply with `continueConversation` as waiting for an answer.
- **System view data:** `GET /api/v1/system` (any signed-in principal) returns the app version (the assembly's
  informational version; `Version` in `Directory.Build.props`) and the supported / deprecated API versions. Health
  details come from the existing unversioned `/health`.
- **JSON numbers are strict:** `JsonSerializerOptions.Web` accepts numbers as strings, which made the OpenAPI
  document describe every number as `number | string`. The HTTP JSON options use `JsonNumberHandling.Strict`, so
  the generated SPA types are plain `number`. Only request bodies with quoted numbers are affected (now 400).
