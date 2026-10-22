# Tasks

## 1. Route summaries

- [ ] 1.1 Write failing tests in `OpenApiExportTests`: `CreateConversationTurn` has summary `/conversation/turns` and a non-empty description; every operation's summary equals its path without `/api/v1`. Verify they fail.
- [ ] 1.2 Rename `.WithSummary(` to `.WithDescription(` in `Marshal.Api` and add the route summary operation transformer; verify the 1.1 tests pass.

## 2. Security requirements

- [ ] 2.1 Write failing tests: the document declares `apiToken` and `session`; `CreateConversationTurn` accepts `apiToken` with `conversation:write` or `session`; `ListApiTokens` requires `session` only; `GetMe` accepts any `apiToken` or `session`; `ListLoginProviders` has no security; `PostWebhook` requires `apiToken` only. Unit-test the credential table and combination (scope policy plus `Human`, unknown policy throws). Verify they fail.
- [ ] 2.2 Add `PolicyCredentials` and `Policies.Credentials`, the security schemes document transformer and the security requirement operation transformer; verify the 2.1 tests and the full `Marshal.Api.Tests` suite pass.

## 3. Regenerate and document

- [ ] 3.1 Run `npm run gen:api` in `web/` and `npm run build`; verify `web/openapi/v1.json` changes only in summaries, descriptions and security, and the SPA build passes.
- [ ] 3.2 Drop the security injection from `scripts/docs-openapi.mjs`, update `docs/api/openapi.md` and `docs/api/authentication.md`; verify `npx clay-doc@5 build --strict` passes and an operation page shows the route as its title and its own credentials.
