---
icon: fa-solid fa-vial
description: "Test commands, test rules, shared fakes and the coverage gate."
---

# Building and testing

Test commands, test rules, shared fakes and the coverage gate. The full solution and `Marshal.Api.Tests` are slow: test the project you changed while you iterate.

## Commands

::: code-group
```sh [.NET]
dotnet test --project tests/Marshal.Memory.Tests
dotnet test --project tests/Marshal.Core.Tests --filter-method "*Confirm_KeywordWithPunctuation*"
```

```sh [web/]
npm test            # Vitest against src/testing/fakeServer.ts
npm run build       # gen:types + vue-tsc + Vite: fails on unhandled API contract changes
```

```sh [ha/]
uv run pytest       # pytest-homeassistant-custom-component pins the HA version
```
:::

## Rules

- Write the failing test first.
- xUnit v3 with plain `Assert`. No mocking or assertion libraries.
- Name tests `MethodOrBehavior_Condition_ExpectedResult`.
- One test project per source project: `src/Marshal.<Name>` → `tests/Marshal.<Name>.Tests`.
- Data access tests use a second fake user to prove owners do not leak.
- Tests never need a real LLM, embedding model or live account. Anything that cannot be faked stays out of the suite.
- Time-dependent code takes a `TimeProvider`; tests use `FakeTimeProvider`.

## Shared fakes

`tests/Marshal.Testing` holds the hand-written fakes. Add a fake there when more than one test project needs it.

| Fake | Replaces |
|---|---|
| `FakeChatClient` | `IChatClient`, scripted responses |
| `FakeEmbeddingGenerator` | `IEmbeddingGenerator` |
| `FakeTool`, `FakeToolSource`, `FakeToolSourceProvider` | Tools and tool sources |
| `FakeSecretStore` | `ISecretStore` |
| `FakeMcpServer` | An in-process MCP server |
| `TestDatabase`, `TestExecutionContext` | An isolated SQLite database, an execution context with an owner |
| `InMemoryPendingActionStore`, `InMemorySessionStore`, `InMemoryRunRecorder` | Confirmation, session and run stores |

In-process HTTP and websocket fakes use `Microsoft.AspNetCore.TestHost`; API tests use `WebApplicationFactory`.

## Coverage gate

Code that decides risk tiers, visibility, confirmation or ownership needs 100 % branch coverage and fails closed ([style guide](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md), "Security-relevant code"). That covers the guard and the risk engine in `Marshal.Core`:

```sh
dotnet test --project tests/Marshal.Core.Tests --coverage
```

No automated check enforces the threshold. Inspect the guard and risk engine coverage yourself before you finish a change that touches them.

## Related

::: cards
- [Setup](setup.md)
- [Web frontend](web-frontend.md)
:::
