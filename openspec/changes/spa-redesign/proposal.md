# Proposal

## Why

The web app works and is well tested, but it is a prototype on screen: 295 lines of global CSS, no shared components, 15 equal links in one navigation list, one breakpoint that squeezes desktop pages onto phones, English-only text and inconsistent error handling. Several failures leave the user with nothing on screen: a first-run setup over plain HTTP, any failed widget load, and a failed `/me` call, which blanks the whole app. Push notifications deep-link into the web app from the Home Assistant companion app, so the most time-critical moments (approvals, printer failures) start on a phone, where the current layout is weakest.

Error handling, i18n and a new layout each touch every page. Doing them in one pass, page by page against a new design system, avoids rewriting the same 41 components three times.

## What Changes

- **Design in Figma first.** A new design system (tokens, type, components, states) and every page, designed from scratch in Figma for desktop and phone, including loading, empty, error and session-expired states and German text. The information architecture is rethought; the approved design decides which views exist and how they are grouped.
- **Phone and desktop are designed separately, not just reflowed.** The surfaces you act on (dashboard, approvals and review, chat, notifications, printer, task results, kill switch) are designed phone-first with a bottom tab bar and deep-link landing screens. The surfaces you manage (memory graph, scatter and editor, runs, cost, configuration, mail accounts, security, secrets) are designed desktop-first with a reduced phone layout.
- **One error model in the SPA.** Every API failure (network error, ProblemDetails response, a request that throws before it is sent) becomes a typed error with a human-readable message in the user's language. Forms show it inline; loads show a "could not load" state with a retry; anything uncaught reaches a global banner. A session that expires mid-use returns to the login page with a message. No failure leaves the screen blank or unchanged.
- **i18n.** All UI text in German and English. The language and time zone come from the user's preferences; before sign-in, from the browser. Dates, numbers and durations are formatted for that language and time zone.
- **Two server fixes behind the SPA errors.** `GET /api/v1/auth/antiforgery` over plain HTTP returns the `auth.https_required` problem instead of an empty 500. Unexpected server exceptions return a ProblemDetails body with a stable code and a trace id instead of an empty 500.
- **Preview mode.** `npm run dev:preview` runs the SPA against a built-in fake API with sample data, signed in, without the .NET host. It can switch scenarios (normal, empty, errors) and language, so every page and state can be checked against the Figma design.
- **Installable app (PWA).** A web app manifest and a service worker that caches only the app shell, so Marshal can be added to the home screen and opens full screen. API responses are never cached.

## Capabilities

### New Capabilities

None. The views keep their existing capabilities; what changes is how the shell presents them.

### Modified Capabilities

- `web-shell`: "Responsive layout" is replaced by a phone/desktop split; new requirements for the design system, navigation, API error presentation, i18n, preview mode and the installable app.
- `human-auth`: "Antiforgery" gains a scenario for the token request over plain HTTP.
- `data-ownership`: a new requirement that unexpected failures are returned as ProblemDetails.

View specs (`web-chat`, `memory-view`, `background-views`, `config-view`, `run-insights`, `approval-queue` and others) are updated in this change after the Figma design is approved, where the new information architecture moves, merges or removes views.

## Non-goals

- New features or API endpoints beyond the two error fixes. The redesign presents what the API offers today.
- Changing the data layer that already works: the generated API client, session handling, live events and SSE chat stay; pages are rebuilt on top of them.
- Web push notifications. Push keeps going through the Home Assistant companion app; the PWA does not subscribe to push.
- Offline use. The service worker caches the app shell only; without a connection the app says it cannot reach Marshal.
- Languages other than German and English.
- Showing a currency for costs; the API does not expose one.
- The Home Assistant custom component and the executor host.

## Impact

- `web/`: nearly every view, a new component library, global styles replaced by design tokens, new `i18n/`, `preview/` and error-handling modules, router and app shell rewritten, tests updated and extended.
- New web dependencies: an i18n library and a PWA build plugin (package index in `CLAUDE.md` updated).
- `src/Marshal.Api`: antiforgery token endpoint, exception handler registration; API tests.
- Figma: a new design file (design system and page designs), linked from the change.
- `docs/images/` placeholders can be replaced with real screenshots from preview mode.
