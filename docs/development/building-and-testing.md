# Building and testing

Build the solution and run the .NET, web and Home Assistant test suites.

## Before you start

Finish [setup](setup.md). The full .NET build and the API tests are slow. Build and test the project you changed, not the whole solution, while you iterate.

## .NET

`global.json` selects Microsoft.Testing.Platform as the runner. Run one test project at a time with `--project`:

```sh
dotnet test --project tests/Marshal.Memory.Tests
```

Each project under `src/` has a matching `tests/Marshal.<Name>.Tests` project. For example, `src/Marshal.Memory` is covered by `tests/Marshal.Memory.Tests`. Filter to one test with the runner's filter options, for example:

```sh
dotnet test --project tests/Marshal.Core.Tests --filter-method "*Confirm_KeywordWithPunctuation*"
```

Run the full solution build with:

```sh
dotnet build
```

### Test rules

- **Write the failing test first.** Marshal follows TDD.
- Use xUnit with plain `Assert`. There is no mocking or assertion library.
- Name tests `MethodOrBehavior_Condition_ExpectedResult`.
- Mirror the source layout: one test project per source project.
- Test data access with a second fake user to prove there is no leakage between owners.

### Fakes and the no-real-LLM rule

Tests never need a real LLM, embedding model or live account. Use the hand-written fakes in `tests/Marshal.Testing`:

| Fake | Replaces |
|---|---|
| `FakeChatClient` | `IChatClient`, scripted responses. |
| `FakeEmbeddingGenerator` | `IEmbeddingGenerator`. |
| `FakeTool`, `FakeToolSource`, `FakeToolSourceProvider` | Tools and tool sources. |
| `FakeSecretStore` | `ISecretStore`. |
| `FakeMcpServer` | An in-process MCP server. |
| `TestDatabase`, `TestExecutionContext` | An isolated SQLite database and an execution context with an owner. |
| `InMemoryPendingActionStore`, `InMemorySessionStore`, `InMemoryRunRecorder` | Stores for the confirmation, session and run subsystems. |

Time-dependent code takes a `TimeProvider`; tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`. In-process HTTP and websocket servers use `Microsoft.AspNetCore.TestHost`; API tests use `WebApplicationFactory`. Add a new fake to `Marshal.Testing` when more than one test project needs it.

Anything that cannot be mocked, such as a real model's behavior, stays out of the test suite.

### Containers

Tests that use Testcontainers need a reachable Docker daemon. On a machine where Docker access needs a group, run the test command in a shell that has that group.

### Coverage gate

Code that decides risk tiers, visibility, confirmation or ownership must have 100 % branch coverage and fail closed ([style guide](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md), "Security-relevant code"). This applies to the guard and the risk engine in `Marshal.Core`. Measure it with the code coverage extension (`Microsoft.Testing.Extensions.CodeCoverage`):

```sh
dotnet test --project tests/Marshal.Core.Tests --coverage
```

The repository has no automated check that enforces the threshold. Inspect the coverage output for the guard and risk engine yourself before you finish a change that touches them.

## Web

Run these from `web/`:

| Command | What it does |
|---|---|
| `npm test` | Runs the Vitest component tests. |
| `npm run typecheck` | Type-checks `.vue` files with `vue-tsc`. |
| `npm run build` | Generates types from `openapi/v1.json`, type-checks, then builds with Vite. |

Component tests mount the app against a hand-written fake API server (`web/src/testing/fakeServer.ts`). See [web frontend](web-frontend.md).

## Home Assistant component

```sh
cd ha
uv run pytest
```

The tests use `pytest-homeassistant-custom-component`, which pins the Home Assistant version under test.

## Check that it works

- `dotnet build` ends without warnings.
- The test project you changed passes.
- `npm test` and `npm run build` pass if you touched `web/` or an API contract.
- `uv run pytest` passes if you touched `ha/`.

## Related

- [Setup](setup.md)
- [Adding an integration](adding-an-integration.md)
