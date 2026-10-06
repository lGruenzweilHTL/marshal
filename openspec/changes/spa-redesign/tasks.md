# Tasks

## 1. Server error fixes

- [x] 1.1 Write a failing API test: `GET /api/v1/auth/antiforgery` over plain HTTP returns 400 ProblemDetails with code `auth.https_required`. Verify it fails.
- [x] 1.2 Check `Request.IsHttps` in the antiforgery token endpoint and return the existing error. Verify 1.1 passes and the existing antiforgery tests still pass.
- [x] 1.3 Write failing API tests: an endpoint that throws (a test-only service, armed after startup, makes `/me` throw) returns 500 ProblemDetails with code `server.unexpected` and a `traceId`, and the body does not contain the exception message. Verify they fail.
- [x] 1.4 Register `UseExceptionHandler()` with `CustomizeProblemDetails` for code, generic detail and trace id. Verify 1.3 passes and existing ProblemDetails responses are unchanged.

## 2. SPA error model

- [x] 2.1 Write failing tests for `call()`: success returns `data`; a ProblemDetails response returns `kind: 'problem'` with status, code, detail and trace id; a rejected `fetch` returns `kind: 'network'`; a throwing antiforgery token fetch returns an `ApiError` instead of throwing. Verify they fail.
- [x] 2.2 Implement `ApiError` and `call()` in `web/src/api/`, and stop exposing `session.api` to views. Verify 2.1 passes.
- [x] 2.3 Write failing tests for `errorMessage()` (code translation, then `detail`, then status/kind fallback) and for `useLoad` (loading, ready, empty, error, retry) and `useSubmit` (busy, error, values kept). Implement them. Verify the tests pass.
- [x] 2.4 Write failing tests: a 401 after sign-in routes to `/login?reason=expired&returnUrl=…`; a non-401 failure of `/me` at start shows a full-page error with retry; an uncaught error appears in the global banner. Implement the session, router and global handlers. Verify the tests pass.
- [x] 2.5 Write the architecture test that fails when a file outside `web/src/api/` uses the raw client. Verify it fails on the current views (it passes once the areas are rebuilt; mark it `todo` until the last area group completes).

## 3. i18n foundation

- [x] 3.1 Add `vue-i18n` (package index in `CLAUDE.md`), the `locale` store and catalog merging; English source catalog typed, German typed against it. Write a test that a German catalog missing an English key fails type checking (`vue-tsc` fixture). Verify `npm run build` passes.
- [x] 3.2 Write failing tests for the locale store: preferences after sign-in, browser language before sign-in, English fallback, switch without reload after a preferences save. Implement. Verify they pass.
- [x] 3.3 Write failing tests for the `Intl`-based `format.ts` (date, time, relative time, duration, number, percent, cost) in `de` and `en` with a non-UTC time zone. Rewrite `format.ts`. Verify they pass and existing callers still compile.

## 4. Preview mode

- [x] 4.1 Move the screenshot sample data (rewritten as typed TS) into `web/src/preview/scenarios/` (normal, empty, failing, offline) on top of `FakeServer`. Write a test that every GET endpoint in `openapi/v1.json` used by the SPA has data in the `normal` scenario. Verify it passes.
- [x] 4.2 Add the preview entry, `npm run dev:preview` and the scenario and language panel. Verify by running it: the current views render with sample data, and the `failing` scenario reaches the views (their current, incomplete error handling is expected).
- [x] 4.3 Write a test that the production build contains no module from `web/src/preview/`. Verify it passes.

## 5. Installable app

- [x] 5.1 Add `vite-plugin-pwa` (package index), manifest with placeholder icons, precache of assets only, network-only navigations with a static offline page. Write a test on the generated service worker: no route matches `/api/` or `/health`, and `index.html` is not precached. Verify it passes.
- [x] 5.2 Run the built app behind the host under a non-root base path. Verify the app installs, opens standalone, loads under the base path, and shows the offline page without a connection.

## 6. Design canvas

- [x] 6.1 Create the design canvas with the structure from design.md (Decision 9). Verify the file and its pages exist and the link is recorded in this change's design.md.
- [x] 6.2 Draft the information architecture: destinations, grouping, phone tab bar (at most five), desktop side navigation, deep-link screens. Verify by reviewing it with the user.
- [x] 6.3 Build the foundations: color variables with light and dark modes, spacing, radius, elevation, text styles, chosen font and icon set. Verify contrast (WCAG AA for text) in both modes.
- [x] 6.4 Build the components with variants and German and English content. Verify every component has focus, disabled, loading and error variants where they apply, and 44 px touch targets.
- [x] 6.5 Design every page for phone and desktop per design.md (Decision 6), plus the state frames (loading, empty, error, session expired, offline, HTTPS hint, global banner) with at least one German page per state. Verify every current view (see the before gallery) is either designed or explicitly dropped in the IA.
- [x] 6.6 Checkpoint with the user: review and approve the design. Verify approval is recorded in design.md.

## 7. Specs and tasks after the checkpoint

- [x] 7.1 Add delta specs for the view capabilities the approved IA changes (`web-chat`, `memory-view`, `background-views`, `config-view`, `run-insights`, `approval-queue`, `printer`, `user-preferences` as needed). Verify `openspec validate spa-redesign --strict` passes.
- [x] 7.2 Export the design tokens to `web/src/ui/tokens.css` and record the export steps in the design canvas foundations page. Verify both themes render from the tokens in preview mode.
- [x] 7.3 Insert the build groups between this group and Integration: the shell and component library first, then one group per area of the approved IA, each with test-first tasks for its views (layouts, i18n text, all `useLoad` / `useSubmit` states). Verify `openspec validate spa-redesign --strict` passes.

## 8. Shell and component library

- [x] 8.1 Write failing component tests for the `web/src/ui/` library (`AppButton`, `IconButton`, `AppIcon`, `Badge`, `Card`, `ListRow`, `Field`, `Segmented`, `Switch`, `Sheet`, `Dialog`, `Banner`, `LoadState`, `PageHeader`, `AppBar`, `TabBar`, `SideNav`, `Logo`): variants, focus, disabled and loading states, 44 px targets, German labels. Implement them on `tokens.css` and `base.css` only (no literal colours, fonts or radii outside `tokens.css`). Verify they pass.
- [x] 8.2 Put the logo behind one component (`Logo` with `mark` and `wordmark` variants, SVG in `web/src/ui/logo/`) and generate the PWA icons and favicon from the same SVG. Verify no other file contains logo geometry.
- [x] 8.3 Write failing tests for the new shell: tab bar below 900 px and side navigation from 900 px, grouped as on the canvas, Inbox badge count, admin-only Configuration, More sheet, global banner, sign-out. Rebuild `AppShell` and the router on the approved IA (old paths redirect to their new places). Verify they pass.
- [x] 8.4 Add the appearance setting (System / Light / Dark, per device in `localStorage`, applied as `data-theme`). Write a test that it applies without a reload. Verify it passes.

## 9. Act: Home, Chat, Inbox, Printer

- [x] 9.1 Write failing tests for Home (approvals first, printer alert, running tasks, kill switch only while tasks run, notifications, health, recent runs, today's cost; loading, empty and error per widget). Rebuild Home. Verify they pass.
- [x] 9.2 Write failing tests for Chat (streaming reply, confirmation ticket in chat, failure of a turn, new conversation, phone layout). Rebuild Chat. Verify they pass.
- [x] 9.3 Write failing tests for Inbox (approvals and proposals before notifications, filter by outcome, approve / reject / merge with errors kept on screen) and the focused approval and notification deep-link screens. Rebuild them. Verify they pass.
- [x] 9.4 Write failing tests for Printer and the printer alert deep link (live state, confirm dialog for confirm-tier controls, offline and error states). Rebuild them. Verify they pass.

## 10. Activity: Tasks, Runs, Cost

- [x] 10.1 Write failing tests for Tasks, the task detail and the task result deep link (goal edit, cancel, all load and submit states). Rebuild them. Verify they pass.
- [x] 10.2 Write failing tests for Runs and the run detail (filters, guard decisions, redacted results). Rebuild them. Verify they pass.
- [x] 10.3 Write failing tests for Cost (per day, per tier, advisor line, formatted in both languages). Rebuild it. Verify they pass.

## 11. Memory: Chunks, Projects, Map, Audit

- [x] 11.1 Write failing tests for the chunk list and the editor with history (search, filters, pin, save errors keep the text). Rebuild them. Verify they pass.
- [x] 11.2 Write failing tests for Projects (aliases, alias candidates, correction log). Rebuild it. Verify they pass.
- [x] 11.3 Write failing tests for Map (graph / scatter toggle on desktop, list on phones) and Audit. Rebuild them. Verify they pass.

## 12. Connections and settings

- [x] 12.1 Write failing tests for Mail accounts. Rebuild it. Verify they pass.
- [x] 12.2 Write failing tests for Preferences (language switch without reload, time zone, quiet hours, appearance). Rebuild it. Verify they pass.
- [x] 12.3 Write failing tests for Security with tokens, passkeys and secrets (plaintext shown once, write-only secrets, unresolved references). Rebuild it; `/secrets` redirects to Security › Secrets. Verify they pass.
- [x] 12.4 Write failing tests for System (health, version, kill switch, link to Configuration for admins) and Configuration. Rebuild them. Verify they pass.

## 13. Sign-in and app states

- [x] 13.1 Write failing tests for Login (providers, `reason=expired` message, German before sign-in), local login and Setup (HTTPS problem shown, values kept). Rebuild them on the desktop constellation panel and the phone layout. Verify they pass.
- [x] 13.2 Rebuild the full-page `/me` failure, not-found and offline pages on the design system. Delete `styles.css` and every old global class. Verify `npm run build` passes and the architecture test from 2.5 passes without `todo`.

## 14. Integration

- [x] 14.1 Run the full .NET and web test suites (`dotnet test`, `npm test`, `npm run build`). Verify all pass, including the architecture test from 2.5 without `todo`.
- [x] 14.2 Walk every page in preview mode in both languages, both themes and all scenarios at phone and desktop width against the design canvas. Verify no page is blank in any scenario.
- [x] 14.3 Replace the `docs/images/placeholder-*.png` files with screenshots from preview mode and update `README.md`. Verify the README shows the new screenshots.
