---
icon: fa-solid fa-vial
description: "Testbefehle, Testregeln, gemeinsame Fakes und die Abdeckungsgrenze."
---

# Bauen und testen

Testbefehle, Testregeln, gemeinsame Fakes und die Abdeckungsgrenze. Die ganze Solution und `Marshal.Api.Tests` sind langsam: Teste beim Arbeiten das Projekt, das du geändert hast.

## Befehle

::: code-group
```sh [.NET]
dotnet test --project tests/Marshal.Memory.Tests
dotnet test --project tests/Marshal.Core.Tests --filter-method "*Confirm_KeywordWithPunctuation*"
```

```sh [web/]
npm test            # Vitest gegen src/testing/fakeServer.ts
npm run build       # gen:types + vue-tsc + Vite: scheitert an nicht behandelten Änderungen des API-Vertrags
```

```sh [ha/]
uv run pytest       # pytest-homeassistant-custom-component legt die HA-Version fest
```
:::

## Regeln

- Schreib zuerst den fehlschlagenden Test.
- xUnit v3 mit einfachem `Assert`. Keine Mocking- oder Assertion-Bibliotheken.
- Benenne Tests `MethodOrBehavior_Condition_ExpectedResult`.
- Ein Testprojekt pro Quellprojekt: `src/Marshal.<Name>` → `tests/Marshal.<Name>.Tests`.
- Tests des Datenzugriffs nutzen einen zweiten Fake-Benutzer, um zu zeigen, dass nichts zwischen Eigentümern durchsickert.
- Tests brauchen nie ein echtes LLM, Embedding-Modell oder Live-Konto. Was sich nicht faken lässt, bleibt aus der Suite.
- Zeitabhängiger Code nimmt einen `TimeProvider`; Tests nutzen `FakeTimeProvider`.

## Gemeinsame Fakes

`tests/Marshal.Testing` enthält die handgeschriebenen Fakes. Leg einen Fake dort an, wenn mehr als ein Testprojekt ihn braucht.

| Fake | Ersetzt |
|---|---|
| `FakeChatClient` | `IChatClient`, vorgegebene Antworten |
| `FakeEmbeddingGenerator` | `IEmbeddingGenerator` |
| `FakeTool`, `FakeToolSource`, `FakeToolSourceProvider` | Werkzeuge und Werkzeugquellen |
| `FakeSecretStore` | `ISecretStore` |
| `FakeMcpServer` | Einen MCP-Server im Prozess |
| `TestDatabase`, `TestExecutionContext` | Eine isolierte SQLite-Datenbank, einen Ausführungskontext mit Eigentümer |
| `InMemoryPendingActionStore`, `InMemorySessionStore`, `InMemoryRunRecorder` | Speicher für Bestätigungen, Sitzungen und Durchläufe |

HTTP- und Websocket-Fakes im Prozess nutzen `Microsoft.AspNetCore.TestHost`; API-Tests nutzen `WebApplicationFactory`.

## Abdeckungsgrenze

Code, der über Risikostufen, Sichtbarkeit, Bestätigung oder Eigentum entscheidet, braucht 100 % Branch-Abdeckung und muss sicher scheitern ([Style Guide](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md), „Security-relevant code“). Das betrifft den Guard und die Risiko-Engine in `Marshal.Core`:

```sh
dotnet test --project tests/Marshal.Core.Tests --coverage
```

Keine automatische Prüfung setzt die Grenze durch. Prüf die Abdeckung von Guard und Risiko-Engine selbst, bevor du eine Änderung abschließt, die sie berührt.

## Verwandte Seiten

::: cards
- [Einrichtung](setup.md)
- [Weboberfläche](web-frontend.md)
:::
