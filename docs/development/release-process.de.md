---
icon: fa-solid fa-tag
description: "Wie ein Release von Marshal gebaut und veröffentlicht wird; eine Automatisierung gibt es noch nicht."
---

# Release-Ablauf

Wie ein Release von Marshal gebaut und veröffentlicht wird. Eine Automatisierung gibt es noch nicht: Der einzige Workflow, `.github/workflows/docs.yaml`, veröffentlicht die Doku-Website. Kein CI führt Tests aus oder veröffentlicht Images, und noch keine Version hat ein Tag.

## Versionen

- App-Version: `<Version>` in `Directory.Build.props` (in der Systemansicht sichtbar) oder `-p:Version=<x.y.z>` beim Build.
- Home-Assistant-Komponente: `version` in `ha/custom_components/marshal/manifest.json`, verteilt über HACS (`ha/hacs.json`).
- API-Versionen sind getrennt und stehen im Code ([API-Versionierung](api-versioning.md)). Doku-Versionen folgen der API-Version, nicht der App-Version (siehe `CLAUDE.md`).

## Das Image

Das `Dockerfile` im Wurzelverzeichnis baut ein Image, den API-Host plus die SPA:

| Stufe | Macht |
|---|---|
| `build` | Veröffentlicht `src/Marshal.Api` in Release, exportiert das OpenAPI-Dokument (`marshal --export-openapi`), kopiert `web/src/artifacts/highlighting/languages.json` für den Code-Highlighter. |
| `web` | `npm ci` und `npm run build` gegen das exportierte Dokument; eine API-Änderung, die die SPA nicht behandelt, lässt den Image-Build scheitern. |
| `runtime` | `aspnet:10.0` mit `git` und `openssh-client`, läuft als unprivilegierter Benutzer `app` auf Port 8080, verlinkt `marshal` nach `/usr/local/bin`. |

`Marshal.ExecutorHost` hat sein eigenes Image und ist nie Teil dieses Images.

## Checkliste für ein Release

::: steps
1. **Prüfen.** Jede Änderung ist archiviert. Alle .NET-Testprojekte, `npm test`, `npm run build` und `uv run pytest` laufen durch. Diagramme und Doku passen zum Code.
2. **Version.** Setz die App-Version und, wenn sich `ha/` geändert hat, die Version der Komponente.
3. **Changelog.** Verschieb die Einträge unter `[Unreleased]` in `CHANGELOG.md` (Keep a Changelog) unter die neue Version. Nenn Migrationen, neue API-Versionen und Konfigurationsänderungen; die [Anleitung zum Aktualisieren](../installation/upgrading.md) muss sie erwähnen.
4. **Bauen und taggen.** `docker build -t marshal:<version> .`, `git tag v<version>`, beides pushen. HACS übernimmt die Komponente aus dem Tag.
:::

## Prüfen, ob es funktioniert

- Das neue Image startet und `GET /health` antwortet.
- Die Systemansicht zeigt die neue Version.
- Ein bestehendes `/data`-Volume wird beim ersten Start migriert.

## Verwandte Seiten

::: cards
- [Aktualisieren](../installation/upgrading.md)
- [API-Versionierung](api-versioning.md)
:::
