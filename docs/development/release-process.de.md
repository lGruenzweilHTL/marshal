---
icon: fa-solid fa-tag
description: "Das Marshal-Image bauen und eine Version veröffentlichen."
---

# Release-Prozess

Das Marshal-Image bauen und eine Version veröffentlichen. Marshal hat noch keine Automatisierung für Releases; diese Seite beschreibt den manuellen Ablauf ehrlich.

## Was es gibt

- Ein mehrstufiges `Dockerfile` im Wurzelverzeichnis des Repositorys baut ein einziges Image: den API-Host plus die SPA.
- `Directory.Build.props` setzt die Version der App (`<Version>0.1.0</Version>`), die die Systemansicht zeigt. Laut Kommentar darf CI sie mit `-p:Version=...` überschreiben.
- `deploy/docker-compose.yml` startet das Image.

## Was es nicht gibt

- Es gibt kein Verzeichnis `.github/workflows`, also keine CI, keinen automatischen Testlauf und keine Veröffentlichung von Images.
- Es gibt keinen Changelog-Generator und noch kein getaggtes Release. `CHANGELOG.md` folgt Keep a Changelog und wird von Hand bearbeitet; Commits folgen Conventional Commits, du kannst Einträge also aus `git log` ableiten.

## Bau des Images

Das Dockerfile läuft in drei Stufen:

1. `build` veröffentlicht `src/Marshal.Api` im Release-Modus und exportiert das OpenAPI-Dokument mit `marshal --export-openapi`. Es kopiert `web/src/artifacts/highlighting/languages.json`, weil die Code-Hervorhebung die Datei einbettet.
2. `web` (`node:22-alpine`) führt `npm ci` und `npm run build` gegen dieses Dokument aus. Eine Änderung am Vertrag, die die SPA nicht behandelt, scheitert hier.
3. `runtime` (`mcr.microsoft.com/dotnet/aspnet:10.0`) ergänzt `git` und `openssh-client`, kopiert den veröffentlichten Host und die SPA nach `wwwroot` und läuft als unprivilegierter Benutzer `app` auf Port 8080.

Der Befehl `marshal` ist nach `/usr/local/bin` verlinkt, `docker exec <container> marshal tokens ...` funktioniert also.

Der Coding-Executor ist ein eigener Dienst mit eigenem Image. Er ist nie Teil des Haupt-Images.

## Schritte

::: steps
1. **Vorbereiten**

   - Stell sicher, dass jede Änderung in OpenSpec archiviert ist ([Ablauf](openspec-workflow.md)).
   - Führ die Suiten aus [Bauen und testen](building-and-testing.md) aus: jedes .NET-Testprojekt, `npm test`, `npm run build`, `uv run pytest`.
   - Prüfe, dass die Diagramme zum Code passen ([README der Diagramme](../diagrams/README.md)) und die Dokumentation zu den Spezifikationen.

2. **Version**

   Setz `<Version>` in `Directory.Build.props` oder übergib beim Bauen `-p:Version=<x.y.z>`. Hat sich die Home-Assistant-Komponente geändert, setz ihre Version in `ha/custom_components/marshal/manifest.json`.

3. **Changelog**

   Fass die Conventional-Commit-Historie seit dem letzten Tag zusammen, gruppiert nach `feat`, `fix` und inkompatiblen Änderungen. Verschieb die Einträge unter `[Unreleased]` in `CHANGELOG.md` unter die Überschrift der neuen Version und nutze sie als Release Notes. Nenne neue Migrationen, neue API-Versionen und Änderungen an der Konfiguration ausdrücklich; die [Anleitung zum Aktualisieren](../installation/upgrading.md) muss sie erwähnen.

4. **Bauen und taggen**

   ```sh
   docker build -t marshal:<version> .
   git tag v<version>
   ```

   Push das Image in die Registry, die du nutzt, und push den Tag. Das Repository legt keine Registry fest.

5. **Die Home-Assistant-Komponente veröffentlichen**

   Die Komponente in `ha/` wird über HACS verteilt (`ha/hacs.json`). Tagge das Repository, damit HACS das Release findet.
:::

## Prüfen, ob es funktioniert

- `docker run` des neuen Images startet, und `GET /health` antwortet.
- Die Systemansicht zeigt die neue Version.
- Ein bestehendes Volume `/data` wird beim ersten Start migriert ([Aktualisieren](../installation/upgrading.md)).

## Verwandte Seiten

::: cards
- [Einrichtung](setup.md)
- [Aktualisieren](../installation/upgrading.md)
:::
