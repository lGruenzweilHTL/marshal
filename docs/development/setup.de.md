---
icon: fa-solid fa-laptop-code
description: "Installiere die Werkzeuge, die du brauchst, um Marshal zu bauen, seine Tests auszuführen und an der Weboberfläche und der Home-Assistant-Komponente zu arbeiten."
---

# Eine Entwicklungsmaschine einrichten

Installiere die Werkzeuge, die du brauchst, um Marshal zu bauen, seine Tests auszuführen und an der Weboberfläche und der Home-Assistant-Komponente zu arbeiten.

## Bevor du anfängst

Du brauchst Git und eine Maschine mit Linux, macOS oder Windows, auf der Docker läuft.

## Werkzeuge

| Werkzeug | Version | Wofür |
|---|---|---|
| .NET SDK | `10.0.101` oder ein späteres 10.0-Feature-Band | Alles unter `src/` und `tests/`. |
| Node.js | 22 | Die SPA in `web/`. |
| uv | aktuell | Das Python-Projekt in `ha/`. |
| Docker | aktuell | Testcontainers-Tests, Bau des Images. |

### .NET SDK

`global.json` pinnt das SDK auf `10.0.101` mit `rollForward: latestFeature`, jedes neuere 10.0.x-Feature-Band funktioniert also. Die Datei wählt außerdem Microsoft.Testing.Platform als Test-Runner. Prüfe das aktive SDK im Wurzelverzeichnis des Repositorys:

```sh
dotnet --version
```

Das Repository stellt lokale Werkzeuge aus `.config/dotnet-tools.json` wieder her, darunter `dotnet-ef`:

```sh
dotnet tool restore
```

### Node.js

`web/package.json` deklariert kein Feld `engines`, und das Repository hat keine `.nvmrc`. Das `Dockerfile` baut die SPA mit `node:22-alpine`; nutze lokal also Node 22, passend zum Image. Installiere die Abhängigkeiten:

```sh
cd web
npm ci
```

### uv und Python

Die Komponente in `ha/` braucht Python 3.14.2 oder neuer (`requires-python` in `ha/pyproject.toml`). uv installiert einen passenden Interpreter. Die Datei `uv.lock` pinnt die Testabhängigkeiten für Home Assistant.

```sh
cd ha
uv sync
```

### Docker

Mehrere Testprojekte starten Container über Testcontainers (zum Beispiel einen IMAP-Server und FreshRSS). Der Benutzer, der `dotnet test` ausführt, braucht Zugriff auf den Docker-Socket. Ohne ihn können diese Tests ihre Container nicht starten.

Docker baut auch das Release-Image. Siehe [Release-Prozess](release-process.md).

## Einmal bauen

Im Wurzelverzeichnis des Repositorys:

```sh
dotnet build
```

Der Build behandelt Warnungen als Fehler und setzt die Stilregeln aus `.editorconfig` durch (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` in `Directory.Build.props`). Führe `dotnet format` aus, bevor du committest.

## Prüfen, ob es funktioniert

```sh
dotnet test --project tests/Marshal.Core.Tests
```

Am Ende des Laufs sind alle Tests bestanden. Mach weiter mit [Bauen und testen](building-and-testing.md).

## Verwandte Seiten

::: cards
- [Überblick zur Entwicklung](index.md)
- [Weboberfläche](web-frontend.md)
:::
