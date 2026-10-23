---
icon: fa-solid fa-window-maximize
description: "Conventions of the Vue SPA in web/: folders, the generated API client, texts, tests, the screenshot sweep and PWA assets."
---

# Web frontend

Conventions of the Vue SPA in `web/`. The .NET host serves the built app from `wwwroot`; the `Dockerfile` builds it.

## Layout

`web/src` is organized by feature. Each feature folder holds its views, components, composables, tests and `messages.ts`.

| Folder | Holds |
|---|---|
| `api/` | Typed client (`client.ts`), generated `schema.d.ts`, load and submit composables, error mapping |
| `i18n/` | Locale handling, merged catalog, shared texts (`common.ts`) |
| `layout/`, `ui/` | App shell, shared components, design tokens, fonts, icons |
| `chat/`, `printers/`, `tasks/`, `memory/`, ... | One folder per feature |
| `preview/` | Preview mode with a fake API (scenarios `normal`, `demo`, `empty`, `failing`, `offline`) |
| `testing/` | `fakeServer.ts`, `fixtures.ts`, `mountApp.ts`, `fakeLiveEvents.ts` |
| `architecture/` | Tests that guard the structure (no raw `fetch`, production bundle) |

## API client

- `src/api/schema.d.ts` is generated; never edit it. `npm run gen:api` exports `openapi/v1.json` from the host (`--export-openapi`) and regenerates the types; `npm run gen:types` only regenerates.
- Call the API only through `src/api/client.ts` (`openapi-fetch`). A test in `architecture/` rejects raw `fetch`.
- Contracts never contain `TimeSpan`; durations are numbers with the unit in the name ([API versioning](api-versioning.md)).

## Texts

- Every feature's `messages.ts` exports `en` (the source) and `de` typed as `Catalog<typeof en>`, so a missing or extra German key fails `vue-tsc`.
- Register the feature in `src/i18n/messages.ts`. Shared texts go in `src/i18n/common.ts`. No hard-coded user-visible strings.

## Tests

Vitest with `@vue/test-utils` and `jsdom`. Tests mount the app with `testing/mountApp.ts` against `testing/fakeServer.ts`; fixtures go in `testing/fixtures.ts`. Tests never need the real backend.

## Screenshot sweep

`npm run screenshots` opens every preview route at 375 and 1280 px, light and dark, writes PNGs to `web/.screenshots/<scenario>-<lang>/<width>-<theme>/` and runs layout checks that jsdom cannot measure. A failed check fails the command. Run it in German too before you finish a layout change.

| Option | Default | Effect |
|---|---|---|
| `--scenario <list>` | `normal,empty,failing` | Preview scenarios; layout checks run only for `normal` |
| `--lang de` | `en` | Interface language |
| `--only <routes>` | all | Routes by their names in the script |
| `--no-shots` | | Layout checks only |

The script never downloads a browser: it uses `PLAYWRIGHT_CHROMIUM`, `/opt/pw-browsers/chromium` or the newest Chromium in the Playwright cache. Without Chromium's system libraries, run it in the image that matches the pinned `playwright` version, from `web/`:

```sh
docker run --rm -v $PWD/..:/work -w /work/web --user $(id -u):$(id -g) -e HOME=/tmp \
  mcr.microsoft.com/playwright:v1.63.0-noble node scripts/screenshots.ts --scenario normal
```

## PWA and assets

`vite-plugin-pwa` caches the app shell only; navigations are network-only, the offline page is static. `scripts/appAssets.ts` renders the icon PNGs from the logo SVGs at build time. `npm run gen:tokens` generates the design tokens.

## Commands

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server |
| `npm run dev:preview` | Dev server in preview mode |
| `npm run gen:api` | Export OpenAPI, regenerate types |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run build` | Generate types, type-check, build |
| `npm test` | Vitest |
| `npm run screenshots` | Screenshot sweep and layout checks |

## Related

::: cards
- [Building and testing](building-and-testing.md)
- [Web UI tour](../guides/web-ui/tour.md)
:::
