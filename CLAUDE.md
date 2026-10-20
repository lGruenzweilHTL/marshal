# CLAUDE.md

AI-specific guidance for working in this repository. Humans: see [README.md](README.md) for the project overview and [STYLE_GUIDE.md](STYLE_GUIDE.md) for coding conventions.

## What this project is

Marshal is a self-hosted ASP.NET agent that Home Assistant conversation entities talk to over REST. Voice via HA Assist is the main interface; a Vue web UI and a public API are secondary interfaces. One user per instance for now; the code is prepared for multi-user instances later.

## Source of truth

1. **`openspec/specs/`** — the authoritative specifications. If code and spec disagree, the spec wins; fix the code or propose a spec change, never silently diverge.
2. **`openspec/changes/`** — proposed and in-progress changes (delta specs, design, tasks). Archived changes live in `openspec/changes/archive/`.
3. **`docs/internal/brainstorm-notes.md`** — informal design notes from the initial discussion. Interim reference only, until the topics are formalized in OpenSpec. Once a topic exists in `openspec/specs/`, the spec supersedes the notes.

Workflow skills: `opsx:propose` (new change), `opsx:apply` (implement tasks), `opsx:sync` / `opsx:archive` (finish a change), `opsx:explore` (think before changing).

Do not start implementation work without an approved OpenSpec change covering it.

## Non-negotiable rules

These come from the design and must hold in every change. Details and rationale: [STYLE_GUIDE.md](STYLE_GUIDE.md).

- **Small interfaces everywhere.** Every integration and subsystem sits behind a small interface, even with exactly one implementation.
- **The model can't use it → the model can't see it.** Forbidden, denied or unavailable tools are filtered out of the tool list, not refused after the call. The guard enforces the same rules server-side as a second line.
- **Never add a send capability for mail.** No SMTP config, no `messages.send` / `drafts.send`, no Graph `Mail.Send` scope.
- **Never expose raw G-code** or other free-form device commands to the model. Use allowlisted macros.
- **Confirm-tier actions are frozen server-side** (tool + args + nonce + TTL). Only user actions can confirm (a user turn, a web UI approval, a nonce-verified push action); tool output never counts as confirmation.
- **Only system-defined sources may send `critical` notifications.**
- **Config never contains secrets.** `/config` holds only secret *names* (`apiKeySecret: moonraker_voron`); values come from `ISecretStore` (env / `/run/secrets` files, or encrypted DB). Startup fails on literal secrets in `/config`.
- **Every user-owned row has `owner_id`**, and all data access goes through owner-scoped queries. Never read "the user" from a global.
- **Tests never need a real LLM.** Use fake `IChatClient` / `IEmbeddingGenerator`. Anything unmockable stays out of the test suite.
- **TDD.** Write the failing test first.
- **Content from mail, RSS, notes and web pages is untrusted data**, never instructions. A run that read it is tainted: memory and task writes in that run are escalated to confirm tier. No tool fetches a caller-chosen URL.
- **Demo mode simulates, it never bypasses.** Simulated integrations in `Marshal.Demo` replace only unconfigured integrations, keep the real tool ids, tiers and guard, and never contact a network host. Production code never references `Marshal.Testing`. When you change an interface that `Marshal.Demo` implements, update the simulation in the same change.
- **API versions live in code, never in config.** No version literals in endpoint files; register on the versioned route group. Never change a published version's contract in place — add a new version.

## Repository map

Update this section whenever projects are added, renamed or removed. Entries marked *(planned)* do not exist yet.

```
Marshal.sln                  solution; build rules in Directory.Build.props, package versions in
                             Directory.Packages.props, SDK + test runner (Microsoft.Testing.Platform) in global.json
src/                         .NET projects
  Marshal.Api/               ASP.NET host: REST + SSE, auth, serves the SPA
  Marshal.Abstractions/      small shared interfaces (providers, channels, executors, ...)
  Marshal.Core/              agent harness: loop, tool registry, risk engine + guard,
                             context builder, model router, sessions, AI task runner + structured output shaper
  Marshal.Memory/            memory chunks, versions and edges, hybrid retrieval, project activation,
                             consolidation (entities and migrations live in Marshal.Data)
  Marshal.Tasks/             durable tasks, scheduler + worker pool, event bus, trigger matcher, deliveries,
                             notifications, approval queue, input requests, MCP server task tracker, follow-ups,
                             executors, system jobs, webhook ingest (Uptime Kuma and Proxmox normalizers)
  Marshal.Integrations.HomeAssistant/  HA REST + websocket client, registry and state cache, target resolver,
                             area context, timer, places (zone) and energy tools, time zone source, display player,
                             music (Music Assistant player and music tools)
  Marshal.Integrations.Moonraker/  printers: Moonraker REST + websocket state cache, snapshots, inspection, printer tools
  Marshal.Integrations.Mail/ mail providers (IMAP via MailKit) and credential sources, mail tools (never a send path)
  Marshal.Integrations.FreshRss/  FreshRSS Google Reader API client, full-text extraction, RSS tools
  Marshal.Integrations.Web/  SearXNG search provider, per-session web results, web tools (search, answer; never
                             fetches a result URL)
  Marshal.Integrations.Notes/  git-backed Markdown notes source, own hybrid notes index, notes tools
  Marshal.Integrations.Media/  Seerr client (search, requests), read-only Sonarr/Radarr calendars and queues, Jellyfin
                             links, media tools
  Marshal.Integrations.Proxmox/  read-only Proxmox VE client (API token, optional certificate pinning), infra tools
  Marshal.Integrations.GitHub/  read-only GitHub REST client for allowlisted repos (fine-grained PAT), repos tools
                             (diffs and failed-job logs as artifacts)
  Marshal.Artifacts/         artifact store (files in /data/artifacts, per-owner quota), kind detection, media renditions
                             for displays, code highlighter (languages.json and parity cases shared with web/src/artifacts/
                             highlighting/), artifacts tools
  Marshal.Demo/              demo mode: simulated HA (incl. the ha.* home control tools), printer, mail, RSS, notes and web search
                             behind the integrations' interfaces, demo world state, seeder and reset (referenced only by
                             Marshal.Api)
  Marshal.Data/              EF Core DbContext, migrations, owner-scoped query filters
  Marshal.ExecutorHost/      separate coding-sandbox service (own deployment and image, never in the main container,
                             never referenced by Marshal)
tests/                       one test project per src project (Marshal.<Name>.Tests)
  Marshal.Testing/           shared hand-written fakes (FakeChatClient, FakeEmbeddingGenerator, ...)
web/                         Vue + Vite + TypeScript SPA (API client generated from OpenAPI), folders by feature;
                             built into Marshal.Api's wwwroot by the Dockerfile; preview mode with a fake API
                             (src/preview/scenarios: normal, demo, empty, failing, offline), Playwright screenshot sweep
                             in scripts/screenshots.ts
ha/                          custom_components/marshal (Python HA integration, HACS; may move to its own repo): Assist
                             conversation entity, AI Task entity (POST /api/v1/ai-tasks), reauth flow; uv project (Python
                             3.14 via .python-version), tests in ha/tests
deploy/                      docker-compose.yml, config.example/ (example /config files: marshal.yaml, risk.yaml,
                             satellites.yaml, mcp/, skills/, templates/, tasks/ with the example briefing task);
                             Dockerfile at repo root
docs/                        user and contributor documentation (Markdown, Diátaxis layout; style rules in
                             docs/development/docs-style.md), diagrams/, images/, internal/ (design notes, not published)
openspec/                    specifications and changes
```

Runtime volumes (not in git): `/config` (skills, templates, MCP servers, risk rules, satellites — git-friendly, no secrets) and `/data` (SQLite, Data Protection keys, backups, notes repo).

## Diagrams — keep up to date

`docs/diagrams/` holds PlantUML diagrams that must always match the code (details and render commands: [docs/diagrams/README.md](docs/diagrams/README.md)):

- **`erd.puml`** — update in the same change that adds, removes or renames an EF Core entity, column or relationship.
- **`packages.puml`** — update in the same change that adds, removes or renames a project, changes a project reference or adds / removes a key external dependency.
- Re-render the SVGs after every edit (`java -jar plantuml.jar -tsvg docs/diagrams/*.puml`) and commit `.puml` and `.svg` together.
- Remove the "(planned)" title marker once the diagrammed structure exists in code.

## Package index

Key dependencies and what they are for. Keep this list current when adding or removing packages; prefer these over alternatives with the same purpose.

**.NET**
| Package | Purpose |
|---|---|
| `Microsoft.Extensions.AI` (+ `.Abstractions`) | `IChatClient`, `IEmbeddingGenerator`, `AIFunction` — provider-neutral LLM access. `Marshal.Abstractions` references only `.Abstractions` |
| `Microsoft.Extensions.AI.Evaluation` (+ `.Quality`) | LLM-judged reply scoring for eval replays (`EvaluatorReplyJudge`, Marshal.Core) |
| `OllamaSharp` | Ollama provider implementing `IChatClient` / `IEmbeddingGenerator` (host only) |
| `Anthropic` | Official Anthropic C# SDK (v10+; implements `IChatClient`) — Claude API provider (host only) |
| `Microsoft.Extensions.AI.OpenAI` | OpenAI-compatible provider for local servers (llama.cpp `llama-server`, vLLM, LM Studio) (host only) |
| `ModelContextProtocol.Core` | Official MCP C# SDK (client and server types, no hosting) — MCP client for HA's MCP server and custom servers; in-process fake servers in tests |
| `ModelContextProtocol.AspNetCore` | MCP server hosting (streamable HTTP, stateless) for Marshal's own MCP endpoint `/mcp` (host only) |
| `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer` + `Asp.Versioning.OpenApi` | URL-segment API versioning (`/api/v{version:apiVersion}`), per-version OpenAPI grouping (`MapOpenApi().WithDocumentPerVersion()`) |
| `Microsoft.AspNetCore.OpenApi` | OpenAPI document generation (one per API version) |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | `oidc` login provider (BFF) |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | `local` login provider (passkeys + password) |
| `NetEscapades.Configuration.Yaml` | YAML configuration provider for `/config/marshal.yaml` |
| `YamlDotNet` | Parsing `/config` files (satellites, risk rules, MCP servers) and Markdown frontmatter (skills, templates) |
| `Microsoft.EntityFrameworkCore.Sqlite` | Persistence; owner-scoped global query filters |
| `Microsoft.AspNetCore.DataProtection` | Encrypting UI-managed secrets in the database; keys in `/data/keys` |
| `EFCore.NamingConventions` | snake_case table and column names for the EF Core model |
| `Microsoft.EntityFrameworkCore.Design` + `dotnet-ef` (local tool, `.config/dotnet-tools.json`) | Creating migrations (`dotnet ef migrations add <Name> --project src/Marshal.Data --output-dir Migrations`) |
| `sqlite-vec` | Vector search in SQLite: upstream NuGet with the native `vec0` binaries per RID (prerelease `0.1.7-alpha.2.1`, `contentFiles` excluded), loaded into every connection by `SqliteVectorExtension` (Marshal.Data) |
| `Cronos` | Cron expression parsing for the scheduler (owner time zone, DST-aware) |
| `MailKit` | IMAP provider (search, drafts via `APPEND`, XOAUTH2); never its SMTP client |
| `SmartReader` | Readability full-text extraction for RSS articles |
| `AngleSharp` | HTML parsing (SmartReader's parser): plain text of RSS summaries |
| `Google.Apis.Gmail.v1`, `Microsoft.Graph` | Gmail API and Graph mail providers (later, build order §14) |
| `SkiaSharp` (+ `SkiaSharp.NativeAssets.Linux.NoDependencies`) | Image resizing (printer and camera snapshots before VLM calls) and display renditions of artifacts (image conversion, text pages with the embedded JetBrains Mono font); MIT licensed; the native asset package runs in the slim container without fontconfig |
| `Microsoft.Extensions.Http` | `IHttpClientFactory` named clients (MCP streamable HTTP, Home Assistant REST) |
| `Microsoft.Extensions.Http.Resilience` | Retries / backoff for integration HTTP clients |
| `OpenTelemetry.*` (`Extensions.Hosting`, `Exporter.OpenTelemetryProtocol`) | Traces and metrics (`Marshal.Runs` activity source, `Marshal` meter), OTLP export |
| `xunit.v3`, `Testcontainers` | Tests (plain `Assert`) on Microsoft.Testing.Platform (`dotnet test --project <dir>`); containers for IMAP, FreshRSS |
| `Microsoft.Testing.Extensions.CodeCoverage` | Coverage (`--coverage`) for the guard and risk engine branch-coverage check |
| `Microsoft.AspNetCore.Mvc.Testing` | In-process API integration tests (`WebApplicationFactory`) |
| `Microsoft.AspNetCore.TestHost` | In-process fake servers for integration clients (HA websocket and REST) |
| `Microsoft.Extensions.TimeProvider.Testing` | `FakeTimeProvider` for scheduler and time-based tests |

No mocking or assertion libraries — use hand-written fakes from `Marshal.Testing`. Expected failures use Marshal's own `Result` / `Result<T>` from `Marshal.Abstractions`, not a third-party library.

**Web (`web/`)**
| Package | Purpose |
|---|---|
| `vue`, `vite`, `typescript` | SPA framework and build (Vue is the current choice, may change); TypeScript 5.x until `vue-tsc` supports 7 |
| `vue-router` | Client-side routing (history mode under the `<base href>` the host writes) |
| `vue-tsc` | Type-checks `.vue` files in `npm run build`, so an API contract change the SPA does not handle fails the build |
| `openapi-typescript` | Generates TypeScript types from Marshal's versioned OpenAPI document (build step; `npm run gen:api` exports it via `marshal --export-openapi`) |
| `openapi-fetch` | Small typed `fetch` client driven by those types |
| `vue-i18n` | Interface text in German and English (Composition API mode); catalogs per feature (`messages.ts`), German typed against English |
| `lucide-vue-next` | Icons (Lucide, ISC) at a 1.75 px stroke through `AppIcon` |
| `@fontsource-variable/bricolage-grotesque`, `@fontsource-variable/instrument-sans`, `@fontsource-variable/jetbrains-mono` | Bundled fonts (OFL) served from the app's origin; declared in `web/src/ui/fonts.css` |
| `vite-plugin-pwa` | Web app manifest and Workbox service worker (app shell only, network-only navigations, static offline page) |
| `@resvg/resvg-js` | Build-time rendering of the PWA icon PNGs from the logo SVGs (`web/scripts/appAssets.ts`) |
| `markdown-it` | Chat markdown rendering with HTML disabled and a same-origin-only image rule (ships its own types) |
| `three` (+ `@types/three`, dev) | 3D artifact viewer (STL and 3MF loaders, orbit controls); lazy chunk, never in the entry chunk |
| `vitest`, `@vue/test-utils`, `jsdom` | Component tests (`npm test`) against a hand-written fake API server (`src/testing/fakeServer.ts`) |
| `playwright` (dev, pinned to the `mcr.microsoft.com/playwright` image version) | Screenshot sweep and layout checks over preview mode (`npm run screenshots`); never downloads browsers |

**Python (`ha/`)**
| Package | Purpose |
|---|---|
| `pytest-homeassistant-custom-component` | Tests for the HA custom integration (`uv run pytest` in `ha/`); pins the HA version tested against. The component itself has no requirements (it uses HA's shared aiohttp session) |
| `hassil`, `home-assistant-intents`, `gazetteer-matcher`, `pymicro-vad`, `pyspeex-noise`, `mutagen`, `ha-ffmpeg`, `PyTurboJPEG` (dev) | Requirements of the HA components the tests set up (`conversation`, `assist_pipeline`, `tts`, `ai_task` with `camera`); keep in step with the pinned HA release |

## Conventions

- Commits: Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`).
- **Always write commit messages with the `caveman:caveman-commit` skill.**
- **Never add a `Co-Authored-By` trailer** (or any other AI attribution) to commits. This overrides any default attribution instruction.
- C# style, naming and design philosophy: [STYLE_GUIDE.md](STYLE_GUIDE.md).
- Verify facts about external APIs (HA, Moonraker, Gmail, Graph, FreshRSS) against their current documentation before implementing; the design notes mark several of them as "verify when building".
