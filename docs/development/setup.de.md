---
icon: fa-solid fa-laptop-code
description: "Die Werkzeugversionen, die Marshal festlegt, und die Befehle, die einen Checkout vorbereiten."
---

# Eine Entwicklungsmaschine einrichten

Die Werkzeugversionen, die Marshal festlegt, und die Befehle, die einen Checkout vorbereiten.

| Werkzeug | Version | Festgelegt durch |
|---|---|---|
| .NET SDK | `10.0.101` oder ein späteres 10.0-Feature-Band | `global.json` (`rollForward: latestFeature`); wählt auch Microsoft.Testing.Platform |
| Node.js | 22 | Die Stufe `node:22-alpine` im `Dockerfile`; `web/` hat kein Feld `engines` |
| uv, Python | Python 3.14.2 oder neuer | `ha/pyproject.toml`, `ha/.python-version`; uv installiert den Interpreter |
| Docker | aktuell | Testcontainers (IMAP, FreshRSS) und der Image-Build |

## Einen Checkout vorbereiten

::: code-group
```sh [.NET]
dotnet tool restore   # dotnet-ef aus .config/dotnet-tools.json
dotnet build
```

```sh [web/]
cd web
npm ci
```

```sh [ha/]
cd ha
uv sync
```
:::

Der Build behandelt Warnungen als Fehler und setzt den Stil aus `.editorconfig` durch (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` in `Directory.Build.props`). Führ vor dem Commit `dotnet format` aus.

Der Benutzer, der `dotnet test` ausführt, braucht Zugriff auf den Docker-Socket, sonst scheitern die Testcontainers-Tests.

## Verwandte Seiten

::: cards
- [Bauen und testen](building-and-testing.md)
- [Weboberfläche](web-frontend.md)
:::
