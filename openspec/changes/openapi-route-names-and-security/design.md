# Design

## Context

The v1 document comes from `Microsoft.AspNetCore.OpenApi` with Asp.Versioning's API explorer (`AddOpenApi` in `ApiServiceCollectionExtensions`). It has one document transformer (fixed title) and one schema transformer (typed ids). Endpoints set `.WithSummary(...)` with a sentence (133 calls) and never `.WithDescription(...)`. Authorization is declared per endpoint or group with `RequireAuthorization`, either by policy name (`Policies.Human`, `Policies.HumanOrAdmin`, scope names from `ApiScopes`, none for the default policy) or with a policy object (`HookEndpoints`' token-only policy; executor callback policies, whose endpoints are excluded from the description). The default authentication scheme forwards bearer requests to the token handler and everything else to the cookie handler. See proposal.md for motivation.

## Goals / Non-Goals

**Goals:**
- Route-based summaries and per-operation security requirements, as in the spec, computed from the same metadata that enforces authorization, so they cannot drift.
- Fail closed: a policy the mapping does not know breaks document generation (and with it `npm run gen:api` and the API tests), never produces a silently wrong document.

**Non-Goals:**
- Describing scopes that a browser session gets from its roles; `session` is listed without scopes.
- Modelling "either of two scopes" (the MCP policy); the MCP endpoint is not part of the document.

## Decisions

1. **Summaries in an operation transformer, descriptions in the endpoint files.** Every `.WithSummary("…")` becomes `.WithDescription("…")` (mechanical rename, same text), and an operation transformer `RouteSummaryTransformer` sets `Summary` to the operation's relative path with the version segment removed (`api/v1/conversation/turns` → `/conversation/turns`). Alternative considered: keep `WithSummary` and have the transformer move it to the description. Rejected: the source would then say "summary" for what ends up as the description, and new endpoints would keep writing sentences into the summary slot.
   The path comes from the API description's `RelativePath`, which already has the version substituted and route constraints removed, so the summary matches the document's path key.

2. **Credential mapping beside the policies.** `Policies` gets one table, `Policies.Credentials(string policyName)`, returning a small value `PolicyCredentials(bool Session, IReadOnlyList<string>? TokenScopes)` (`TokenScopes` null: tokens not accepted; empty: any token). Entries: each `ApiScopes` scope → session or token with that scope; `Human` → session; `HumanOrAdmin` → session or token with `admin`; the default policy (no name) → session or any token. A policy object is classified by its authentication schemes: only the token scheme → any token, no session; anything else is unknown. An unknown name or object throws `InvalidOperationException` naming the policy and the route.
   Alternative considered: introspect the combined `AuthorizationPolicy` requirements. Rejected: `Human` and `HumanOrAdmin` are assertion requirements that cannot be inspected, so the mapping would still need names.

3. **Combining policies.** An endpoint's metadata can carry several `IAuthorizeData` and policy objects (group plus endpoint). The combined value accepts a session only if every part does, and tokens only if every part does, with the union of the token scopes. `IAllowAnonymous` metadata after the last authorization metadata means no security, matching how ASP.NET Core evaluates it.

4. **Emitting security.** A document transformer `SecuritySchemesTransformer` adds `components.securitySchemes` (`apiToken`: `http`/`bearer`; `session`: `apiKey` in cookie `marshal_session`). The operation transformer `SecurityRequirementTransformer` writes one requirement object per accepted credential: `{apiToken: [scopes…]}` and/or `{session: []}`; anonymous operations get none. OpenAPI 3.1 allows role names in the array of non-OAuth schemes, which is where the scopes go.
   No new interfaces: both transformers implement the framework's `IOpenApiOperationTransformer` / `IOpenApiDocumentTransformer`; the route summary and security logic are one class each, unit-tested through the served document.

5. **Docs prep script.** `scripts/docs-openapi.mjs` keeps adding `servers` and drops its `securitySchemes` and `security` injection; the document's own metadata takes over.

## Risks / Trade-offs

- [The table has to know every new policy] → Fail-closed generation; the API tests read `/openapi/v1.json` on every run, so a missing entry fails CI.
- [Route summaries repeat information shown next to them] → Short, unique and stable names are the point; the description keeps the explanation.
- [Session scopes not shown] → `docs/api/authentication.md` lists the role scopes.

## Migration Plan

Regenerate `web/openapi/v1.json` and the SPA types with `npm run gen:api`. Nothing to migrate for clients: paths and payloads do not change.
