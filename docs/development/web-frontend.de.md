---
icon: fa-solid fa-window-maximize
description: "Konventionen der Vue-SPA in web/: Ordner, der generierte API-Client, Texte, Tests, der Screenshot-Durchlauf und PWA-Assets."
---

# Weboberfläche

Konventionen der Vue-SPA in `web/`. Der .NET-Host liefert die gebaute App aus `wwwroot`; das `Dockerfile` baut sie.

## Aufbau

`web/src` ist nach Features gegliedert. Jeder Feature-Ordner enthält seine Views, Komponenten, Composables, Tests und `messages.ts`.

| Ordner | Enthält |
|---|---|
| `api/` | Typisierter Client (`client.ts`), generiertes `schema.d.ts`, Composables zum Laden und Absenden, Fehlerzuordnung |
| `i18n/` | Sprachumschaltung, zusammengeführter Katalog, gemeinsame Texte (`common.ts`) |
| `layout/`, `ui/` | App-Rahmen, gemeinsame Komponenten, Design-Tokens, Schriften, Icons |
| `chat/`, `printers/`, `tasks/`, `memory/`, ... | Ein Ordner pro Feature |
| `preview/` | Vorschaumodus mit Fake-API (Szenarien `normal`, `demo`, `empty`, `failing`, `offline`) |
| `testing/` | `fakeServer.ts`, `fixtures.ts`, `mountApp.ts`, `fakeLiveEvents.ts` |
| `architecture/` | Tests, die die Struktur absichern (kein rohes `fetch`, Produktions-Bundle) |

## API-Client

- `src/api/schema.d.ts` ist generiert; bearbeite es nie. `npm run gen:api` exportiert `openapi/v1.json` aus dem Host (`--export-openapi`) und erzeugt die Typen neu; `npm run gen:types` erzeugt nur die Typen.
- Ruf die API nur über `src/api/client.ts` (`openapi-fetch`) auf. Ein Test in `architecture/` lehnt rohes `fetch` ab.
- Verträge enthalten nie `TimeSpan`; Dauern sind Zahlen mit der Einheit im Namen ([API-Versionierung](api-versioning.md)).

## Texte

- Die `messages.ts` jedes Features exportiert `en` (die Quelle) und `de`, typisiert als `Catalog<typeof en>`; ein fehlender oder überzähliger deutscher Schlüssel lässt `vue-tsc` scheitern.
- Registrier das Feature in `src/i18n/messages.ts`. Gemeinsame Texte gehören in `src/i18n/common.ts`. Keine fest eingebauten sichtbaren Texte.

## Tests

Vitest mit `@vue/test-utils` und `jsdom`. Tests montieren die App mit `testing/mountApp.ts` gegen `testing/fakeServer.ts`; Fixtures gehören in `testing/fixtures.ts`. Tests brauchen nie das echte Backend.

## Screenshot-Durchlauf

`npm run screenshots` öffnet jede Route des Vorschaumodus mit 375 und 1280 px, hell und dunkel, schreibt PNGs nach `web/.screenshots/<scenario>-<lang>/<width>-<theme>/` und führt Layoutprüfungen aus, die jsdom nicht messen kann. Eine gescheiterte Prüfung lässt den Befehl scheitern. Lass ihn vor dem Abschluss einer Layoutänderung auch auf Deutsch laufen.

| Option | Standard | Wirkung |
|---|---|---|
| `--scenario <list>` | `normal,empty,failing` | Szenarien der Vorschau; Layoutprüfungen laufen nur für `normal` |
| `--lang de` | `en` | Sprache der Oberfläche |
| `--only <routes>` | alle | Routen nach ihren Namen im Skript |
| `--no-shots` | | Nur Layoutprüfungen |

Das Skript lädt nie einen Browser herunter: Es nutzt `PLAYWRIGHT_CHROMIUM`, `/opt/pw-browsers/chromium` oder das neueste Chromium im Playwright-Cache. Fehlen die Systembibliotheken für Chromium, führ es im Image aus, das zur festgelegten `playwright`-Version passt, aus `web/`:

```sh
docker run --rm -v $PWD/..:/work -w /work/web --user $(id -u):$(id -g) -e HOME=/tmp \
  mcr.microsoft.com/playwright:v1.63.0-noble node scripts/screenshots.ts --scenario normal
```

## PWA und Assets

`vite-plugin-pwa` cacht nur den App-Rahmen; Navigationen gehen nur übers Netz, die Offline-Seite ist statisch. `scripts/appAssets.ts` rendert die Icon-PNGs beim Build aus den Logo-SVGs. `npm run gen:tokens` erzeugt die Design-Tokens.

## Befehle

| Befehl | Zweck |
|---|---|
| `npm run dev` | Vite-Dev-Server |
| `npm run dev:preview` | Dev-Server im Vorschaumodus |
| `npm run gen:api` | OpenAPI exportieren, Typen neu erzeugen |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run build` | Typen erzeugen, Typen prüfen, bauen |
| `npm test` | Vitest |
| `npm run screenshots` | Screenshot-Durchlauf und Layoutprüfungen |

## Verwandte Seiten

::: cards
- [Bauen und testen](building-and-testing.md)
- [Rundgang durch die Weboberfläche](../guides/web-ui/tour.md)
:::
