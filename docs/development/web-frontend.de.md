---
icon: fa-solid fa-window-maximize
description: "An der Vue-Single-Page-App in web/ arbeiten: ihre Ordner, der generierte API-Client, Texte, Tests und die PWA-Einrichtung."
---

# Weboberfläche

An der Vue-Single-Page-App in `web/` arbeiten: ihre Ordner, der generierte API-Client, Texte, Tests und die PWA-Einrichtung.

## Bevor du anfängst

Installiere Node 22 und führe `npm ci` in `web/` aus ([Einrichtung](setup.md)). Der .NET-Host liefert die gebaute App aus `wwwroot` aus; das Dockerfile baut sie.

## Aufbau

`web/src` ist nach Features gegliedert, nicht nach technischer Art. Jeder Ordner enthält seine Views, Komponenten, Composables, Tests und `messages.ts`.

| Ordner | Enthält |
|---|---|
| `api/` | Den typisierten Client (`client.ts`), das generierte `schema.d.ts`, Composables zum Laden und Absenden, die Abbildung von Fehlern. |
| `i18n/` | Behandlung der Locale, den zusammengeführten Katalog, gemeinsame Texte (`common.ts`). |
| `layout/`, `ui/` | App-Rahmen, gemeinsame Komponenten, Design-Tokens, Schriften, Icons. |
| `chat/`, `printers/`, `tasks/`, `memory/`, `artifacts/`, `mail/`, `runs/`, ... | Ein Ordner pro Feature. |
| `testing/` | `fakeServer.ts`, `fixtures.ts`, `mountApp.ts`, `fakeLiveEvents.ts`. |
| `architecture/` | Tests, die die Struktur schützen, etwa die Regel gegen den rohen Client und das Produktions-Bundle. |

Leg neuen Code in den Feature-Ordner, zu dem er gehört. Leg für ein neues Feature einen neuen Ordner an.

## Der generierte API-Client

Der API-Client wird aus Marshals OpenAPI-Dokument erzeugt. Bearbeite `src/api/schema.d.ts` nie von Hand.

```sh
cd web
npm run gen:api
```

`gen:api` startet den Host mit `--export-openapi`, um `openapi/v1.json` zu schreiben, und führt dann `openapi-typescript` aus, um `src/api/schema.d.ts` zu schreiben. `npm run gen:types` wiederholt nur den zweiten Schritt. `npm run build` führt vor Vite `gen:types` und `vue-tsc` aus, eine Änderung am Vertrag, die die SPA nicht behandelt, lässt also den Build scheitern. Das Dockerfile macht dasselbe gegen ein frisch exportiertes Dokument.

Ruf die API über den Client in `src/api/client.ts` auf (gebaut auf `openapi-fetch`). Ein Test in `architecture/` lehnt rohes `fetch` ab. Der OpenAPI-Generator scheitert an `TimeSpan` in einem Vertrag: Schick Dauern als Zahlen mit der Einheit im Namen. Siehe [API-Versionierung](api-versioning.md).

## Texte und Sprachen

Die Oberfläche gibt es auf Deutsch und Englisch, mit `vue-i18n` im Composition-API-Modus.

- Jedes Feature hat eine `messages.ts`, die `en` und `de` exportiert.
- `en` ist die Quelle. Typisiere `de` als `Catalog<typeof en>`; ein fehlender oder zusätzlicher deutscher Schlüssel lässt dann `vue-tsc` scheitern.
- Registriere das Feature in `src/i18n/messages.ts` unter einem eigenen Schlüssel. Gemeinsame Texte liegen in `src/i18n/common.ts`.
- Schreib für Benutzer sichtbare Zeichenketten nicht fest in Komponenten.

## Tests

`npm test` führt Vitest mit `@vue/test-utils` und `jsdom` aus. Tests binden die App mit `testing/mountApp.ts` gegen den handgeschriebenen Fake-API-Server in `testing/fakeServer.ts` ein; ergänze Fixtures in `testing/fixtures.ts`. Tests brauchen nie das echte Backend. Schreib zuerst den fehlschlagenden Test.

## Screenshot-Durchlauf

`npm run screenshots` (`scripts/screenshots.ts`) öffnet jede Route des Vorschaumodus bei 375 px und 1280 px, hell und dunkel, und schreibt pro Route und Variante ein PNG nach `web/.screenshots/<scenario>-<lang>/<width>-<theme>/` (von Git ignoriert). Nutze ihn, um Layout-Änderungen über alle Ansichten zu prüfen. Er führt außerdem Layout-Prüfungen aus, die jsdom nicht messen kann, zum Beispiel dass das einzeilige Eingabefeld im Chat auf den Senden-Knopf zentriert ist; eine gescheiterte Prüfung lässt den Befehl scheitern.

Optionen:

- `--scenario normal,empty,failing`: die Szenarien des Vorschaumodus, die erfasst werden. Standard sind diese drei. Der Vorschaumodus hat außerdem `demo` (die normalen Daten im Demo-Modus, mit Abzeichen und simulierten Integrationen) und `offline`. Layout-Prüfungen laufen nur für `normal`.
- `--lang de`: die Sprache der Oberfläche. Standard ist `en`. Deutsche Beschriftungen sind länger, führ ihn also einmal auf Deutsch aus, bevor du eine Layout-Änderung abschließt.
- `--only chat,home`: nur diese Routen, nach den Namen in der Routenliste des Skripts.
- `--no-shots`: nur die Layout-Prüfungen ausführen.

Das Skript lädt nie einen Browser herunter. Es nutzt `PLAYWRIGHT_CHROMIUM`, `/opt/pw-browsers/chromium` oder das neueste Chromium im Playwright-Cache. Fehlen dem Host die Systembibliotheken von Chromium, führ es im Playwright-Image aus, das zur gepinnten `playwright`-Version passt:

```sh
sg docker -c "docker run --rm -v $PWD/..:/work -w /work/web --user $(id -u):$(id -g) -e HOME=/tmp \
  mcr.microsoft.com/playwright:v1.63.0-noble node scripts/screenshots.ts --scenario normal"
```

Führ es in `web/` aus. Lass `sg docker -c` weg, wenn dein Benutzer in der Gruppe `docker` ist. Ein vollständiger Durchlauf aller drei Szenarien dauert etwa zehn Minuten.

## PWA

`vite-plugin-pwa` erzeugt das Web-App-Manifest und einen Workbox-Service-Worker nur für den App-Rahmen. Navigationen gehen nur über das Netzwerk, und eine Offline-Seite ist statisch. Die PNG-Icons der PWA rendert `scripts/appAssets.ts` (`@resvg/resvg-js`) beim Build aus den Logo-SVGs. Design-Tokens kommen aus `npm run gen:tokens`.

## Befehle

| Befehl | Zweck |
|---|---|
| `npm run dev` | Vite-Entwicklungsserver. |
| `npm run dev:preview` | Entwicklungsserver im Modus `preview`. |
| `npm run gen:api` | OpenAPI exportieren und Typen neu erzeugen. |
| `npm run typecheck` | `vue-tsc --noEmit`. |
| `npm run build` | Typen erzeugen, Typen prüfen, bauen. |
| `npm test` | Vitest. |
| `npm run screenshots` | Screenshot-Durchlauf und Layout-Prüfungen über jede Route (siehe oben). |

## Verwandte Seiten

::: cards
- [Bauen und testen](building-and-testing.md)
- [Rundgang durch die Weboberfläche](../guides/web-ui/tour.md)
:::
