# Design

## Context

- Integrations register in `MarshalApp.Create` (`AddMarshalHomeAssistant`, `AddMarshalPrinters`, `AddMarshalMail`, `AddMarshalFreshRss`, `AddMarshalNotes`, ...). Their tool sources hide their tools until the client reports `IsConfigured` (for example `RssTools.IsAvailableAsync` reads `IRssClient.IsConfigured`).
- The seams are already small interfaces:
  - Home Assistant: `IHomeAssistantClient`, `IHomeAssistantCache`, `IHomeAssistantCommands`, `IMusicPlayer`, `ICalendarProvider`.
  - Printers: `IPrinterDirectory`, `IPrinterClient`, `IPrinterStateCache`, `ISnapshotSource`, `IPrinterWebcams`.
  - Mail: `IMailProvider`, `IMailAccountStore`.
  - RSS: `IRssClient`, `IFullTextExtractor`.
  - Notes: `INotesSource`.
- `Marshal.Testing` holds test fakes. Production code must not reference it, so simulated integrations need their own project.
- The CLI lives in `src/Marshal.Api/Cli` (`MarshalCli`, `TokenCommand`).

## Goals / Non-Goals

**Goals:**
- Demo implementations behind the existing interfaces, so tool code, guard and recording stay the real ones.
- Seeding through the same stores the app writes with, so seeded rows are valid by construction.
- Exact, auditable reset.

**Non-Goals:**
- Protocol-level fakes (a simulated HA websocket server). Interface-level simulation is enough, because the integration clients are already covered by their own tests against in-process fake servers.

## Decisions

**New project `Marshal.Demo`**, referenced only by `Marshal.Api`. It references the five integration projects, Marshal.Abstractions and Marshal.Data. One entry point: `AddMarshalDemo(configuration)`, called after the integrations in `MarshalApp.Create`. For each integration it checks the integration's own options ("configured" = the same check its client uses for `IsConfigured`). When the integration is not configured, it replaces that integration's interface registrations (`services.Replace`) with simulated ones that report `IsConfigured = true`. Alternative: a `demo` provider kind per integration in YAML. Rejected: it means more config to explain, and it allows mixing within one integration, which is a non-goal.

**One shared `DemoWorld`** (singleton) holds the simulated state: entities, printer job, mailbox, feeds, notes. All simulated services read and write it, so a light turned on by a tool shows in the HA cache, the area context and the dashboard. Its clock is the app's `TimeProvider`, so tests drive it with `FakeTimeProvider`. Initial content is loaded from embedded JSON and Markdown resources in the project, plus a few embedded PNG snapshots.

**Simulated implementations (v1), each a small class per interface:**
- Home Assistant: `DemoHomeAssistantClient`, `DemoHomeAssistantCache` (raises the same state-changed events on the event bus, so triggers and conditional deliveries work), `DemoHomeAssistantCommands` (maps the service calls Marshal uses — `light.turn_on/off`, `climate.set_temperature`, `timer.*`, `media_player.*`, `music_assistant.*` — onto state changes; returns a `service_not_supported` error for others), `DemoMusicPlayer`, `DemoCalendarProvider`.
- Printers: `DemoPrinterDirectory` (one printer `voron`), `DemoPrinterClient` (allowlisted macros change the job state), `DemoPrinterStateCache` (progress from the start time and duration), `DemoSnapshotSource` / `DemoPrinterWebcams` (embedded images).
- Mail: `DemoMailAccountStore` (one account `demo`), `DemoMailProvider` (search, read and draft append into `DemoWorld`; the interface has no send member, and none is added). Mail accounts are database rows, so "configured" is decided at runtime: both wrap the real registrations (decorators), the demo account shows only while the owner has no account of their own, and every other account goes to the real provider. The new-mail watcher gets the real store, so it never connects for the demo account.
- RSS: `DemoRssClient`, `DemoFullTextExtractor`. FreshRSS is configured only once its API password resolves from the secret store, so these also wrap the real client and step aside while it reports `IsConfigured`.
- Notes: `DemoNotesSource` (embedded Markdown files; the existing notes index indexes them as usual). Wraps the real source like RSS, because a local notes directory is only known at runtime.
- Home Assistant home control: lights and climate are controlled through HA's MCP server (`mcp/ha.yaml`), not through `IHomeAssistantClient`. `DemoHaToolSource` (an `IToolSource` with namespace `ha`) offers the subset Marshal's skills and templates rely on, with the ids that server reports: `ha.intent__HassTurnOn`, `ha.intent__HassTurnOff`, `ha.intent__HassLightSet`, `ha.intent__HassClimateSetTemperature` and `ha.homeassistant__GetLiveContext`. Arguments follow HA's intent slots (`name`, `area`, `floor`, `domain`, `device_class`, plus `brightness` / `temperature`), so `HaTargetResolver` resolves targets against the demo cache as usual. Like `McpToolSource`, the tools default to `confirm`, so the same `risk.yaml` rules decide their tier. The source offers its tools only when Home Assistant is simulated and no MCP server with namespace `ha` is configured (checked at runtime against `IMcpServerCatalog`, which hot-reloads). Results are short text like HA's (`Turned on the light`), never a protocol-level MCP simulation.

Tool risk tiers come from the real tool sources and `risk.yaml`, unchanged. No new tool ids are introduced: the demo `ha` tools reuse the ids of HA's MCP server, so no model-facing tool needs a new tier decision.

**Seeding through `IDemoSeeder`** (Marshal.Demo, v1 `DemoSeeder`). As implemented: sessions, runs with steps, the pending approval and memory go through the real stores and recorder (`EfSessionStore`, `DbRunRecorder`, `EfPendingActionStore`, `IMemoryStore`), each driven by a seed clock set to the item's time, so payloads are valid by construction and times are relative. Tasks and notifications are written as rows, because their stores cannot backdate results; `Marshal.Demo` does not reference `Marshal.Tasks`. The original plan was to write everything through the existing stores and recorder: `ISessionStore`, `IRunRecorder` (runs and steps, including voice runs with satellites `kitchen` and `office`), `ITaskStore` (a briefing task with two past results, and a backgrounded-turn task with `origin_session_id` once `chat-history-and-background` has landed), `INotificationSender`'s store, the approval queue (one pending printer approval with a long TTL), and the memory store (chunks embedded with the configured `IEmbeddingGenerator`; when no embedding model is configured, chunks are written without vectors and found by keyword search only). Each write is recorded in the new table `demo_seed (entity, row_id, seeded_at)`. Alternative: a `demo` column on every table. Rejected: too invasive and easy to forget on new tables.

Seed data is a C# script of builder calls with relative times (`now - 1 day at 07:00`). It is not a JSON fixture, so types and stores keep it valid.

**When seeding runs.** A hosted service `DemoSeedService` seeds at startup when `demo.enabled`, an owner exists and the data was never seeded for the owner (no `demo_seed` rows and no `demo.seed` audit event, so a restart after a reset does not seed again). The database initializer creates the instance owner before hosted services start and first-run setup claims that same user; until an owner exists the service checks again every few seconds, which covers an owner created later without an owner-created event. `marshal demo seed` and `marshal demo reset` (new `DemoCommand` in `Marshal.Api/Cli`) run against the database without starting the web host; they build the host's services (for the memory store and its embedding model) and initialize the database like startup. Reset deletes the rows in reverse dependency order (steps, runs, sessions, ...) by the ids in `demo_seed`, inside one transaction, and writes audit events `demo.reset` / `demo.seed`. Each row is deleted inside a savepoint: a seeded row that a user row still refers to (a seeded conversation the user continued) is kept and counts as the user's from then on.

**UI flag.** `GET /api/v1/me` gains `demo: { enabled, showBadge }` (an additive field, so the v1 contract stays compatible). The system view has no integration list, so `GET /api/v1/system` gains `simulatedIntegrations` (snake_case names, empty without demo mode) and the view lists them in a "Demo mode" section, each marked simulated. The shell shows the badge next to the logo (desktop) or in its own row above the page (phone, which has no shell header).

**Run order with the chat changes.** This change goes after `chat-history-and-background` and `chat-reasoning`, so the seed includes conversations, background results and reasoning steps. If it ships earlier, those seed parts are skipped behind a check.

## Risks / Trade-offs

- [Demo mode turned on in a real instance by mistake] → It only fills unconfigured integrations. Seeded rows are recorded and removable. Startup logs a warning that demo mode is on. The badge is on by default.
- [Simulated behavior drifts from the real integrations] → The simulated classes implement the same interfaces, and a contract test per interface runs the same assertions against the demo implementation and the test fake where one exists.
- [The model calls a service the simulation does not know] → It gets a structured error result, as with a real HA error. The model can recover.
- [Reset misses rows written by later code as side effects of seeded rows] → Seeding writes only through stores whose rows the seeder records. Derived rows (for example notification deliveries) are deleted by cascade or listed explicitly. Covered by a test that seeds, resets, and compares row counts per table with an empty database.
- [Seeded memory without an embedding model] → Keyword-only retrieval. The demo still shows the memory view.

## Migration Plan

One EF migration adds `demo_seed`. Turning demo off leaves seeded rows in place until `marshal demo reset`, which is documented. Update `packages.puml` (new project) and `erd.puml` (new table), and re-render both.
