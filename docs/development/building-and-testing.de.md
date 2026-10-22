---
icon: fa-solid fa-vial
description: "Die Solution bauen und die Testsuiten für .NET, Web und Home Assistant ausführen."
---

# Bauen und testen

Die Solution bauen und die Testsuiten für .NET, Web und Home Assistant ausführen.

## Bevor du anfängst

Schließ die [Einrichtung](setup.md) ab. Der vollständige .NET-Build und die API-Tests sind langsam. Baue und teste beim Iterieren das Projekt, das du geändert hast, nicht die ganze Solution.

## .NET

`global.json` wählt Microsoft.Testing.Platform als Runner. Führe mit `--project` jeweils ein Testprojekt aus:

```sh
dotnet test --project tests/Marshal.Memory.Tests
```

Jedes Projekt unter `src/` hat ein passendes Projekt `tests/Marshal.<Name>.Tests`. Zum Beispiel deckt `tests/Marshal.Memory.Tests` das Projekt `src/Marshal.Memory` ab. Filtere mit den Filteroptionen des Runners auf einen Test, zum Beispiel:

```sh
dotnet test --project tests/Marshal.Core.Tests --filter-method "*Confirm_KeywordWithPunctuation*"
```

Den vollständigen Build der Solution startest du mit:

```sh
dotnet build
```

### Regeln für Tests

- **Schreib zuerst den fehlschlagenden Test.** Marshal folgt TDD.
- Nutze xUnit mit einfachem `Assert`. Es gibt keine Mocking- oder Assertion-Bibliothek.
- Benenne Tests nach `MethodOrBehavior_Condition_ExpectedResult`.
- Spiegle die Struktur der Quellen: ein Testprojekt pro Quellprojekt.
- Teste Datenzugriffe mit einem zweiten Fake-Benutzer, um zu belegen, dass zwischen Eigentümern nichts durchsickert.

### Fakes und die Regel ohne echtes LLM

Tests brauchen nie ein echtes LLM, Embedding-Modell oder Live-Konto. Nutze die handgeschriebenen Fakes in `tests/Marshal.Testing`:

| Fake | Ersetzt |
|---|---|
| `FakeChatClient` | `IChatClient`, vorgegebene Antworten. |
| `FakeEmbeddingGenerator` | `IEmbeddingGenerator`. |
| `FakeTool`, `FakeToolSource`, `FakeToolSourceProvider` | Werkzeuge und Werkzeugquellen. |
| `FakeSecretStore` | `ISecretStore`. |
| `FakeMcpServer` | Einen MCP-Server im selben Prozess. |
| `TestDatabase`, `TestExecutionContext` | Eine isolierte SQLite-Datenbank und einen Ausführungskontext mit Eigentümer. |
| `InMemoryPendingActionStore`, `InMemorySessionStore`, `InMemoryRunRecorder` | Speicher für die Subsysteme Bestätigung, Sitzung und Durchlauf. |

Zeitabhängiger Code nimmt einen `TimeProvider`; Tests nutzen `FakeTimeProvider` aus `Microsoft.Extensions.TimeProvider.Testing`. HTTP- und Websocket-Server im selben Prozess nutzen `Microsoft.AspNetCore.TestHost`; API-Tests nutzen `WebApplicationFactory`. Leg einen neuen Fake in `Marshal.Testing` an, wenn mehr als ein Testprojekt ihn braucht.

Alles, was sich nicht mocken lässt, etwa das Verhalten eines echten Modells, bleibt aus der Testsuite draußen.

### Container

Tests mit Testcontainers brauchen einen erreichbaren Docker-Daemon. Braucht der Zugriff auf Docker auf deiner Maschine eine Gruppe, führe den Testbefehl in einer Shell mit dieser Gruppe aus.

### Hürde für die Abdeckung

Code, der über Risikostufen, Sichtbarkeit, Bestätigung oder Eigentum entscheidet, braucht 100 % Branch-Abdeckung und muss sicher scheitern ([Style Guide](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md), „Security-relevant code“). Das gilt für den Guard und die Risiko-Engine in `Marshal.Core`. Miss sie mit der Erweiterung für Codeabdeckung (`Microsoft.Testing.Extensions.CodeCoverage`):

```sh
dotnet test --project tests/Marshal.Core.Tests --coverage
```

Das Repository hat keine automatische Prüfung, die die Schwelle durchsetzt. Sieh dir die Abdeckung von Guard und Risiko-Engine selbst an, bevor du eine Änderung abschließt, die sie berührt.

## Web

Führe diese Befehle in `web/` aus:

| Befehl | Was er tut |
|---|---|
| `npm test` | Führt die Vitest-Komponententests aus. |
| `npm run typecheck` | Prüft die Typen von `.vue`-Dateien mit `vue-tsc`. |
| `npm run build` | Erzeugt Typen aus `openapi/v1.json`, prüft die Typen und baut dann mit Vite. |

Komponententests binden die App gegen einen handgeschriebenen Fake-API-Server ein (`web/src/testing/fakeServer.ts`). Siehe [Weboberfläche](web-frontend.md).

## Home-Assistant-Komponente

```sh
cd ha
uv run pytest
```

Die Tests nutzen `pytest-homeassistant-custom-component`, das die getestete Version von Home Assistant pinnt.

## Prüfen, ob es funktioniert

- `dotnet build` endet ohne Warnungen.
- Das Testprojekt, das du geändert hast, besteht.
- `npm test` und `npm run build` bestehen, wenn du `web/` oder einen API-Vertrag berührt hast.
- `uv run pytest` besteht, wenn du `ha/` berührt hast.

## Verwandte Seiten

::: cards
- [Einrichtung](setup.md)
- [Eine Integration hinzufügen](adding-an-integration.md)
:::
