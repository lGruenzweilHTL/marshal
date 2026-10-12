# Web frontend

Work on the Vue single-page app in `web/`: its folders, the generated API client, texts, tests and PWA setup.

## Before you start

Install Node 22 and run `npm ci` in `web/` ([setup](setup.md)). The .NET host serves the built app from `wwwroot`; the Dockerfile builds it.

## Layout

`web/src` is organized by feature, not by technical kind. Each folder holds its views, components, composables, tests and `messages.ts`.

| Folder | Holds |
|---|---|
| `api/` | The typed client (`client.ts`), generated `schema.d.ts`, load and submit composables, error mapping. |
| `i18n/` | Locale handling, the merged catalog, shared texts (`common.ts`). |
| `layout/`, `ui/` | App shell, shared components, design tokens, fonts, icons. |
| `chat/`, `printers/`, `tasks/`, `memory/`, `artifacts/`, `mail/`, `runs/`, ... | One folder per feature. |
| `testing/` | `fakeServer.ts`, `fixtures.ts`, `mountApp.ts`, `fakeLiveEvents.ts`. |
| `architecture/` | Tests that guard the structure, such as the raw client rule and the production bundle. |

Put new code in the feature folder it belongs to. Create a new folder for a new feature.

## The generated API client

The API client is generated from Marshal's OpenAPI document. Never edit `src/api/schema.d.ts` by hand.

```sh
cd web
npm run gen:api
```

`gen:api` runs the host with `--export-openapi` to write `openapi/v1.json`, then runs `openapi-typescript` to write `src/api/schema.d.ts`. `npm run gen:types` repeats only the second step. `npm run build` runs `gen:types` and `vue-tsc` before Vite, so a contract change the SPA does not handle fails the build. The Dockerfile does the same against a freshly exported document.

Call the API through the client in `src/api/client.ts` (built on `openapi-fetch`). A test in `architecture/` rejects raw `fetch` use. The OpenAPI generator fails on `TimeSpan` in a contract: send durations as numbers with the unit in the name. See [API versioning](api-versioning.md).

## Texts and languages

The UI is available in German and English, with `vue-i18n` in Composition API mode.

- Each feature has a `messages.ts` that exports `en` and `de`.
- `en` is the source. Type `de` as `Catalog<typeof en>`; a missing or extra German key then fails `vue-tsc`.
- Register the feature in `src/i18n/messages.ts` under its own key. Shared texts live in `src/i18n/common.ts`.
- Do not hard-code user-visible strings in components.

## Tests

`npm test` runs Vitest with `@vue/test-utils` and `jsdom`. Tests mount the app with `testing/mountApp.ts` against the hand-written fake API server in `testing/fakeServer.ts`; add fixtures to `testing/fixtures.ts`. Tests never need the real backend. Write the failing test first.

## PWA

`vite-plugin-pwa` produces the web app manifest and a Workbox service worker for the app shell only. Navigations are network-only and an offline page is static. PWA icon PNGs are rendered at build time from the logo SVGs by `scripts/appAssets.ts` (`@resvg/resvg-js`). Design tokens come from `npm run gen:tokens`.

## Commands

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server. |
| `npm run dev:preview` | Dev server in `preview` mode. |
| `npm run gen:api` | Export OpenAPI and regenerate types. |
| `npm run typecheck` | `vue-tsc --noEmit`. |
| `npm run build` | Generate types, type-check, build. |
| `npm test` | Vitest. |

## Related

- [Building and testing](building-and-testing.md)
- [Web UI tour](../guides/web-ui/tour.md)
