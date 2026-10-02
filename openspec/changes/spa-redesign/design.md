# Design

## Context

See proposal.md (Why) for motivation. Screenshots of every current page at desktop and phone width: "Marshal UI before the redesign" (https://claude.ai/artifact/WmPxYsQmpEU1nPbWxuTKz5, private to the owner).

Current state that shapes the approach:

- `web/src` has 41 Vue components in feature folders, 32 test files using a hand-written `FakeServer` behind the API client's `fetch` option, and one global `styles.css` with 9 color tokens (light and dark). There is no shared component library.
- API calls go through `createApiSession` (`web/src/api/client.ts`): an `openapi-fetch` client plus a middleware that fetches the antiforgery token. Pages read `{ data, error, response }` themselves; 6 of 33 calling files catch exceptions, 11 use `problemMessage()`. A throw inside the middleware (token fetch failed) or a network error escapes every page.
- The router guard calls `user.load()`; a non-401 failure of `/me` throws out of the guard and leaves a blank page.
- The server already maps expected failures to ProblemDetails with a stable `code` extension (`ErrorProblems.cs`). Unhandled exceptions produce an empty 500: `AddProblemDetails()` is registered, `UseExceptionHandler()` is not.
- Over plain HTTP, `GET /api/v1/auth/antiforgery` throws inside `GetAndStoreTokens` because the antiforgery cookie is `SecurePolicy.Always`.
- The host serves the SPA under a configurable base path by rewriting `<base href>` in `index.html` at request time, and sends a CSP that allows scripts, styles, fonts and connections only from `'self'` (`SpaHosting.cs`).
- Push notifications reach the user through the Home Assistant companion app and deep-link into the SPA.

## Goals / Non-Goals

**Goals:**
- Every page touched once: rebuilt on the design system with i18n text and all states in the same edit.
- The error model makes unhandled failures hard to write: the type system and a test catch a call site that ignores failure.
- Preview mode doubles as the fixture source for component tests and for checking pages against the design canvas.

**Non-Goals:**
- Server-side rendering, a UI framework change or a CSS framework. Vue, Vite and plain CSS with custom properties stay.
- Visual regression testing in CI. Screenshots from preview mode are a manual review aid.

## Decisions

### 1. Order of work: foundations, design canvas, checkpoint, shell, areas

```
 1 foundations (no visual change)      2 design canvas                   3 checkpoint
   error model, i18n setup,              tokens, components,        user approves design;
   preview mode, server fixes,           every page phone +         view specs + tasks for
   PWA plumbing                          desktop, all states        areas updated here
        │                                     │                          │
        └──────────────┬──────────────────────┘                          ▼
                       └─────────────────────────────▶ 4 shell + component library
                                                       5 areas, one at a time
```

Foundations and the design work are independent and can overlap. The information architecture is an outcome of the design phase, so the view-level spec deltas and the per-area tasks are written at the checkpoint, inside this change, before any page is rebuilt.

Alternative: three separate changes (foundations, design, rebuild). Rejected because the rebuild's specs depend on the design, and splitting would archive half-finished intent.

### 2. Error model: a client that never throws

`ApiError` (TypeScript, `web/src/api/errors.ts`):

```ts
type ApiError =
  | { kind: 'network' }
  | { kind: 'problem'; status: number; code?: string; detail?: string; traceId?: string }
  | { kind: 'unexpected'; cause: unknown }
```

`createApiSession` exposes `call(request)`: it runs an `openapi-fetch` request and returns `{ data } | { error: ApiError }`. It never throws: network failures, middleware throws (antiforgery) and unreadable bodies become `ApiError`. Views use `call`, never `session.api` directly.

Messages come from one function, `errorMessage(error, t)`: a catalog entry for a known `code` (`errors.code.auth.https_required`), else the problem `detail`, else a catalog entry for the status or kind.

Two composables carry the view patterns:
- `useLoad(fetcher)` → `{ state: 'loading' | 'ready' | 'empty' | 'error', data, error, retry }`.
- `useSubmit(action)` → `{ busy, error, submit }`; the form keeps its values on failure.

Global net: `app.config.errorHandler`, `window` `unhandledrejection` and `router.onError` push into a banner store rendered by the shell. The router guard catches a failed `/me` and routes to a full-page error with retry.

401 after sign-in: `call` notifies the session store, which clears the user and routes to `/login?reason=expired&returnUrl=…`. 401 on `/me` at start keeps today's behavior (go to login, no message).

Enforcement: an architecture test (vitest, scanning `web/src/**/*.vue` and feature `.ts` files) fails when a file outside `web/src/api/` uses `session.api.`.

Alternative: `openapi-fetch` `onError` middleware only. It sees network errors but not middleware throws in all cases, and does not stop call sites from ignoring `error`.

### 3. i18n: vue-i18n with typed catalogs

- `vue-i18n` (Composition API mode, `legacy: false`).
- Catalogs per feature next to the code (`web/src/tasks/messages.ts` exports `{ en, de }`) and merged in `web/src/i18n/`. English is the source; the German catalog is typed against the English keys, so a missing German key fails `vue-tsc`.
- Language and time zone: a `locale` store fed by `/api/v1/me/preferences` after sign-in and by `navigator.languages` before. The preferences view updates the store after a successful save, so the switch needs no reload.
- `web/src/format.ts` is rewritten on `Intl` with the store's language and time zone: date, time, relative time ("in 5 min"), duration, number, percent, cost (two to four decimals, no currency, see proposal Non-goals).

Alternative: a hand-written `t()` with plain objects. Smaller, but pluralization and number/date integration would be rebuilt by hand.

### 4. Preview mode in the app, not in the browser

- `vite --mode preview` builds a separate entry (`web/src/preview/main.ts`) that creates the API session with a `fetch` backed by an in-app fake API (`web/src/preview/fakeApi.ts`) and scenario data (`web/src/preview/scenarios/`).
- The fake API reuses `FakeServer` from `web/src/testing/` and the sample data from the screenshot work (generated from the OpenAPI schemas, then hand-edited).
- A small floating panel (preview only) switches scenario (`normal`, `empty`, `failing`, `offline`) and language; the choice is kept in the URL query so a scenario can be linked.
- The production entry never imports `preview/`; a test asserts that the production bundle contains no file from `preview/`.
- Component tests import the same scenarios, so tests and preview show the same data.

Alternative: Mock Service Worker. It would add a dependency and a second service worker next to the PWA one.

### 5. Design system: tokens from design tokens, components in `web/src/ui/`

- design tokens (color with light and dark modes, spacing, radius, elevation) and text styles are the source. They are exported to `web/src/ui/tokens.css` as CSS custom properties; the export is a documented manual step at first.
- Components in `web/src/ui/` (names settle on the design canvas; expected: `AppButton`, `IconButton`, `Card`, `Badge`, `ListRow`, `Field`, `Sheet`, `Dialog`, `Banner`, `LoadState` for loading, empty and error, `TabBar`, `SideNav`, `PageHeader`).
- Fonts and icons are bundled and served from the same origin. Chosen on the design canvas: Bricolage Grotesque (titles, key figures), Instrument Sans (UI text) and JetBrains Mono (tool names, arguments, ids), all OFL, via `@fontsource` packages; Lucide icons (`lucide-vue-next`, ISC) at a 1.75 px stroke. The packages are added to the package index.
- Logo: a flat-cut MARSHAL wordmark whose first A is the mark (a solid A with a dot for the crossbar and a short accent wand beside its left leg), shipped as inline SVG, so it needs no font. The mark alone is the PWA icon and favicon (Foundations › Logo on the canvas).
- The tokens live in the "Marshal" design system (https://claude.ai/artifact/DXfMfWe5jKksRwqjNjEFAJ, private to the owner), installed on the design canvas: colour roles with light and dark values (WCAG AA checked for both), type styles, spacing, radius, shadow.
- `styles.css` is replaced area by area: new components use only tokens; the old global classes are deleted once the last old view is gone.

### 6. Phone and desktop layouts

- The shell switches between a bottom `TabBar` (phone) and a grouped `SideNav` (desktop) at 900 px. Tablets in portrait get the phone layout.
- Act surfaces are designed phone-first. Deep links from push land on focused screens: the approval screen shows the frozen action, arguments and a TTL countdown with approve and reject in thumb reach.
- Manage surfaces are designed desktop-first; their phone layout may be reduced (the memory graph and scatter become a list on phones).
- Layout differences use CSS (container and media queries) where the structure is the same, and separate components where it is not. Both layouts share the same `useLoad` / `useSubmit` logic.

### 7. Installable app: vite-plugin-pwa with a network-only shell document

- `vite-plugin-pwa` (Workbox `generateSW`) precaches scripts, styles, fonts and icons, and writes the manifest (`name`, `short_name`, icons, `display: standalone`, `start_url: "./"`, `scope: "./"`).
- `index.html` is **not** precached: the host rewrites its `<base href>` per configured base path at request time, so a cached copy would carry the wrong base. Navigations are network-only; when offline, the service worker answers with a static offline page that needs no base path.
- No runtime caching rule matches `/api/` or `/health`, so API calls always go to the network.
- `registerType: 'autoUpdate'`: a new deploy is picked up on the next start.
- The manifest and service worker are served from `'self'`, so the CSP stays as it is (`default-src 'self'` covers `worker-src` and `manifest-src`).

Alternative: a hand-written service worker. It would need its own precache manifest of hashed asset names; the plugin generates that.

### 8. Server fixes

- `GET /api/v1/auth/antiforgery` checks `Request.IsHttps` first and returns the existing `auth.https_required` error as ProblemDetails. The middleware's existing check for state-changing requests stays.
- Unexpected exceptions: `app.UseExceptionHandler()` early in the pipeline, with `AddProblemDetails(options => options.CustomizeProblemDetails = …)` setting `code: server.unexpected`, a generic `detail` and `traceId` (`Activity.Current?.Id ?? HttpContext.TraceIdentifier`). The exception handler middleware logs the exception; the trace id ties the log to the response.
- No new C# interfaces. Both changes use ASP.NET Core's built-in services.

### 9. Design canvas structure

The design lives in one Claude Design canvas, "Marshal Redesign" (https://claude.ai/artifact/9fkrDjPA9KxC6hQDf1kEs9, private to the owner). Figma was the first choice, but the account's Starter plan with a View seat allows only a handful of MCP calls per month, far below what a full design system needs.

The canvas has one page per part:

- **Information architecture**: destinations, grouping, phone tab bar, desktop side navigation, deep-link screens.
- **Foundations**: color tokens with light and dark values, font tokens, spacing, radius, elevation and the type scale, kept as one `tokens.json` (a Design System artifact installed into the canvas) and shown as specimens. The same names become the CSS custom properties in `web/src/ui/tokens.css`.
- **Components**: the `web/src/ui/` components as reusable artboards (`<dc-import>`), with variants for state, size and tone, in German and English.
- **Phone** and **Desktop**: every page, built from the component artboards, grouped by the information architecture.
- **States**: loading, empty, error, session expired, offline, HTTPS hint, global banner; at least one page per state in German.

The checkpoint (Decision 1) reviews this canvas with the user before any page is rebuilt.

## Risks / Trade-offs

- [A long-running redesign leaves the app half old, half new] → Areas are rebuilt one at a time behind the new shell; each area is complete (design, i18n, states, tests) before the next starts, and old views keep working inside the new shell until replaced.
- [German text is longer and breaks tight layouts] → German frames on the design canvas for every component and for the longest pages; preview mode switches language for checking.
- [Service worker serves a stale or broken shell] → `index.html` is never cached; assets are content-hashed; `autoUpdate`. A test checks that the generated service worker has no route for `/api/`.
- [The architecture test misses an indirect use of the raw client] → `session.api` is no longer part of the type returned to views; only `web/src/api/` can reach it.
- [Canvas tokens and `tokens.css` drift apart] → The export step is listed on the canvas foundations page and in the tasks; a later change can automate it.
- [Scope grows with the IA rethink] → The checkpoint lists any view the new IA adds that needs new API data; those become follow-up changes, not part of this one (proposal Non-goals).

## Migration Plan

No data migration. Deploying the PWA is optional for users. Rollback is a revert; a service worker from a reverted version is replaced on the next start because `autoUpdate` checks on every navigation.

## Open Questions

- Appearance (System / Light / Dark) appears on the Preferences design as a per-device setting stored in the browser. It needs no API; confirm at the checkpoint that it stays in scope.
