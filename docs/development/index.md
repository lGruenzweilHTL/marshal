---
title: Development
icon: fa-solid fa-screwdriver-wrench
order: 90
description: "Rules and project conventions for changing Marshal: setup, tests, specs, integrations, tools, endpoints, migrations and docs."
---

# Development

Rules and project conventions for changing Marshal. The pages assume you know .NET, Vue, EF Core and OpenSpec; they cover only what is specific to Marshal.

Read [`CLAUDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/CLAUDE.md) (non-negotiable rules, repository map, package index) and [`STYLE_GUIDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md) (design philosophy, C# rules) first. Both are authoritative; these pages do not repeat them.

## Pages

::: cards
- [Setup](setup.md)
- [Building and testing](building-and-testing.md)
- [OpenSpec workflow](openspec-workflow.md)
- [Adding an integration](adding-an-integration.md)
- [Adding a tool](adding-a-tool.md)
- [Web frontend](web-frontend.md)
- [Database migrations](database-migrations.md)
- [API versioning](api-versioning.md)
- [Release process](release-process.md)
- [Documentation style](docs-style.md)
- [Maintaining the diagrams](../diagrams/README.md)
:::

## Rules that shape every change

- No implementation without an approved OpenSpec change. Specs in `openspec/specs/` win over code and docs.
- Every integration and subsystem sits behind a small interface.
- The model cannot see a tool it cannot use; the guard enforces the same rules server-side.
- Test first. Tests never need a real LLM.
- Diagrams, docs, `CLAUDE.md` and the demo simulation change in the same commit as the code they describe.

## Related

::: cards
- [Architecture](../concepts/architecture.md)
- [Security model](../concepts/security-model.md)
:::
