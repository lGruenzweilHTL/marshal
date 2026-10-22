# Contributing to Marshal

How to set up a development environment, which rules every change follows, and what a pull request needs. The full contributor documentation is in [docs/development/](docs/development/index.md).

## Before you start

- Read [CLAUDE.md](CLAUDE.md) for the non-negotiable design rules (small interfaces, tool visibility, no mail send path, no secrets in `/config`, owner-scoped data).
- Read [STYLE_GUIDE.md](STYLE_GUIDE.md) for C# conventions and [docs/development/docs-style.md](docs/development/docs-style.md) for documentation.
- The specifications in `openspec/specs/` are the source of truth. If code and spec disagree, fix the code or propose a spec change. Never diverge silently.

## Development setup

Marshal needs the .NET SDK pinned in `global.json`, Node.js for the web UI, `uv` for the Home Assistant component, and Docker for the container tests.

```sh
dotnet build Marshal.sln
dotnet test --project tests/Marshal.Core.Tests
cd web && npm ci && npm test
cd ha && uv run pytest
```

Tests run on Microsoft.Testing.Platform, one test project per source project. Build and test per unit of work, not the whole solution at once. The detailed steps are in [setup](docs/development/setup.md) and [building and testing](docs/development/building-and-testing.md).

## Rules for every change

- **Test first.** Write the failing test, then the code. See [TDD](docs/development/building-and-testing.md).
- **No real LLM in tests.** Use the fakes in `tests/Marshal.Testing` (`FakeChatClient`, `FakeEmbeddingGenerator`). There are no mocking or assertion libraries.
- **OpenSpec first.** Do not start implementation without an approved OpenSpec change that covers it. Propose, implement, sync and archive as described in [the OpenSpec workflow](docs/development/openspec-workflow.md).
- **Keep documentation current.** A change that adds or renames a configuration key, tool, event, endpoint or integration updates the matching page in `docs/` in the same commit.
- **Keep diagrams current.** Update the Mermaid data model (`docs/diagrams/erd.md`) and the package diagram in `docs/concepts/architecture.md` when entities or project references change.

## Commits

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`, `build:`. Write short messages that state the intent.

Do not add a `Co-Authored-By` trailer or any other AI attribution to commits or pull request descriptions.

## Pull request checklist

- [ ] An OpenSpec change covers the work, and its tasks are ticked
- [ ] Tests were written first and pass
- [ ] No test needs a real LLM or a real external service
- [ ] No secret value in `/config`, examples or tests
- [ ] Documentation and diagrams are updated
- [ ] Commits follow Conventional Commits and carry no AI attribution trailer
- [ ] Published API versions are unchanged; a contract change adds a new version ([API versioning](docs/development/api-versioning.md))

## Related

- [Adding an integration](docs/development/adding-an-integration.md)
- [Adding a tool](docs/development/adding-a-tool.md)
- [Database migrations](docs/development/database-migrations.md)
- [Code of conduct](CODE_OF_CONDUCT.md) and [security policy](SECURITY.md)
