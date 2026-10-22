---
title: Development
icon: fa-solid fa-screwdriver-wrench
order: 90
description: "How to change Marshal: set up a machine, build and test, follow the OpenSpec workflow, and add integrations, tools, endpoints and migrations."
---

# Development

How to change Marshal: set up a machine, build and test, follow the OpenSpec workflow, and add integrations, tools, endpoints and migrations.

## Where to start

::: steps
1. **Read [`CLAUDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/CLAUDE.md)** for the non-negotiable rules and the repository map. It is written for AI agents and is equally useful for people.
2. **Read [`STYLE_GUIDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md)** for the design philosophy and the C# rules.
3. **Read [documentation style](docs-style.md)** before you touch a page in `docs/`.
4. **Set up your machine:** [setup](setup.md).
5. **Pick up a change** through the [OpenSpec workflow](openspec-workflow.md). Marshal does not accept implementation work without an approved change.
:::

## Pages

::: cards
- [Setup](setup.md)
  Install the .NET SDK, Node, uv and Docker.
- [Building and testing](building-and-testing.md)
  Build and run the .NET, web and Home Assistant test suites.
- [OpenSpec workflow](openspec-workflow.md)
  Propose, implement, sync and archive a change.
- [Adding an integration](adding-an-integration.md)
  Add a new external system behind a small interface.
- [Adding a tool](adding-a-tool.md)
  Add a model-facing tool to an existing source.
- [Web frontend](web-frontend.md)
  Work on the Vue SPA, its API client and its texts.
- [Database migrations](database-migrations.md)
  Change the EF Core model.
- [API versioning](api-versioning.md)
  Add or change a public API endpoint.
- [Release process](release-process.md)
  Build the image and publish a version.
:::

## Repository map

```tree
Marshal.sln  # solution
src/  # .NET projects
tests/  # one test project per src project (Marshal.<Name>.Tests), plus Marshal.Testing for shared fakes
web/  # Vue + Vite + TypeScript SPA
ha/  # Home Assistant custom component (Python, uv project)
deploy/  # docker-compose.yml and config.example/
docs/  # documentation (this folder), diagrams, images
openspec/  # specifications (specs/) and changes (changes/)
```

The projects in `src/`:

| Project | Holds |
|---|---|
| `Marshal.Api` | ASP.NET host: REST and SSE endpoints, authentication, serves the SPA. |
| `Marshal.Abstractions` | Small shared interfaces (providers, channels, executors). |
| `Marshal.Core` | Agent loop, tool registry, risk engine and guard, context builder, model router, sessions. |
| `Marshal.Memory` | Memory chunks, versions, edges, hybrid retrieval, consolidation. |
| `Marshal.Tasks` | Durable tasks, scheduler, event bus, triggers, deliveries, notifications, approvals, webhook ingest. |
| `Marshal.Artifacts` | Artifact store, kind detection, display renditions, code highlighter. |
| `Marshal.Data` | EF Core `DbContext`, migrations, owner-scoped query filters. |
| `Marshal.Integrations.*` | One project per external system: HomeAssistant, Moonraker, Mail, FreshRss, Notes, Media, Proxmox, GitHub. |
| `Marshal.ExecutorHost` | Separate coding-sandbox service with its own image. The main container never references it. |

`CLAUDE.md` keeps the authoritative map. Update it when you add, rename or remove a project.

## Rules that shape every change

- Specs in `openspec/specs/` win over code, notes and this documentation.
- Every integration sits behind a small interface.
- The model cannot see a tool it cannot use.
- Tests come first and never need a real LLM.
- Diagrams (Mermaid, in the pages that explain them) change in the same commit as the code they describe. See [maintaining the diagrams](../diagrams/README.md).

## Related

::: cards
- [Architecture](../concepts/architecture.md)
- [Security model](../concepts/security-model.md)
:::
