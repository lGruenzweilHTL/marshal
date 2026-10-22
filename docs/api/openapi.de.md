---
icon: fa-solid fa-file-code
order: 70
description: "Marshal veröffentlicht ein OpenAPI-Dokument pro API-Version."
---

# OpenAPI und Client-Generierung

Marshal veröffentlicht ein OpenAPI-Dokument pro API-Version. Nutze es, um jeden Endpunkt anzusehen und einen typisierten Client zu erzeugen. Diese Website zeigt es als [API-v1-Referenz](v1/index.md).

## Dokumente

| URL | Authentifizierung | Inhalt |
|---|---|---|
| `/openapi/v1.json` | keine | OpenAPI-Dokument der API-Version 1 |

Marshal liefert nur das rohe JSON-Dokument aus; eine eingebaute interaktive Dokumentationsseite hat es nicht. Lies es in der [API-v1-Referenz](v1/index.md) auf dieser Website, öffne es in einem beliebigen OpenAPI-Viewer oder importiere die URL in ein Werkzeug wie Postman.

```sh
curl https://marshal.example.com/openapi/v1.json -o marshal-v1.json
```

Das Dokument deckt auch die Endpunkte der Weboberfläche ab. Den Strom der [Live-Ereignisse](live-events.md) beschreibt es nur als Hülle.

## Die API-Referenz auf dieser Website

Die [API-v1-Referenz](v1/index.md) hat eine Seite pro Operation, Tag und Schema, gebaut aus dem Dokument im Repository (`web/openapi/v1.json`) bei jedem Build der Doku. Jede Operation ist nach ihrer Route unter `/api/v1` benannt und nennt die Zugangsdaten, die sie annimmt: ein Maschinen-Token (`apiToken`, mit den nötigen Scopes), die Browser-Sitzung (`session`) oder keine. Der Build ergänzt nur einen Überblickstext und die Server, die das ausgelieferte Dokument weglässt, weil die Weboberfläche die API auf ihrem eigenen Ursprung aufruft: `http://localhost:8080` und einen Platzhalter für deinen eigenen Host.

### Die API in der Doku ausprobieren

Jede Seite einer Operation und Seiten wie [API-Beispiele](examples.md) haben eine Anfragezelle: Füll die Parameter aus, wähle **Authorize**, um ein Token einzufügen, und führe sie mit <kbd>Strg</kbd> + <kbd>Enter</kbd> aus. Das Token bleibt in deinem Browser-Tab und geht nur an den Server, den du gewählt hast.

Die Anfrage geht von deinem Browser direkt an deine Instanz. Browser erlauben das nur, wenn die Instanz mit CORS-Headern für diese Website antwortet, und Marshal sendet keine. Bis dahin meldet eine Zelle die Anfrage als blockiert. Kopiere die Anfrage aus der Zelle als **curl**, **JavaScript** oder **Python** und führe sie dort aus, wo du bist, oder setz vor eine Testinstanz einen Reverse Proxy, der die Header ergänzt.

> [!WARNING]
> Lass eine Doku-Website nie mit einem mächtigen Token eine Produktivinstanz erreichen. Nutze eine Testinstanz, am besten im [Demo-Modus](../guides/demo-mode.md), und ein Token mit nur den Scopes, die du ausprobierst.

## Export ohne laufenden Server

`marshal --export-openapi <file>` schreibt das v1-Dokument in eine Datei, ohne den Server zu starten:

::: code-group
```sh [Container]
docker compose run --rm marshal marshal --export-openapi /tmp/v1.json
```

```sh [Quellbaum]
dotnet run --project src/Marshal.Api -- --export-openapi openapi/v1.json
```
:::

Das exportierte Dokument hat keinen Eintrag `servers`; setz die Basis-URL in deinem Client.

## Einen Client erzeugen

Jeder OpenAPI-Generator funktioniert. Marshals eigene Weboberfläche nutzt `openapi-typescript` und `openapi-fetch`:

```sh
npx openapi-typescript openapi/v1.json -o src/api/schema.d.ts
```

Im Repository exportiert `npm run gen:api` in `web/` das Dokument und erzeugt die Typen neu, eine inkompatible Änderung am Vertrag lässt also den Web-Build scheitern. Derselbe Commit aktualisiert `web/openapi/v1.json` und damit die API-Referenz auf dieser Website.

Erzeuge gegen das Dokument einer Version und pinne diese Version in deinem Client. Eine veröffentlichte Version ändert sich nie nachträglich; neues Verhalten kommt in einer neuen Version. Siehe [API-Überblick](index.md#versionierung).

## Verwandte Seiten

::: cards
- [API-v1-Referenz](v1/index.md)
- [Kommandozeile](../reference/cli.md)
- [Entwicklung der Weboberfläche](../development/web-frontend.md)
:::
