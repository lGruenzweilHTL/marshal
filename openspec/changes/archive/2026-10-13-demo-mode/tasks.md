# Tasks

## 1. Project and configuration

- [x] 1.1 Create `src/Marshal.Demo` and `tests/Marshal.Demo.Tests`, reference `Marshal.Demo` only from `Marshal.Api`, update the CLAUDE.md repo map and `docs/diagrams/packages.puml` and re-render; verify `dotnet build` passes
- [x] 1.2 Write failing tests for `DemoOptions` binding (`enabled` default false, `showBadge` default true) and that demo mode does not relax the model requirement; implement and verify they pass; add the `demo` section to `deploy/config.example/marshal.yaml`

## 2. Demo world and registration

- [x] 2.1 Write failing tests for `DemoWorld` with `FakeTimeProvider`: initial state loads from embedded resources, print progress advances and finishes, a state change raises the state-changed event; implement and verify they pass
- [x] 2.2 Write failing tests for `AddMarshalDemo`: with demo off nothing is replaced; with demo on, unconfigured integrations get demo implementations and configured ones keep theirs (one test per integration); implement and verify they pass

## 3. Simulated integrations (each: failing test first, then implementation)

- [x] 3.1 Home Assistant: client, cache, commands (supported services change state, others return `service_not_supported`), music player, calendar; verify the HA tool tests pass when run against the demo implementations
- [x] 3.1a Write failing tests for `DemoHaToolSource`: tools offered only when HA is simulated and no MCP server `ha` exists; `intent__HassTurnOn` with `name` or `area` changes the demo states; `homeassistant__GetLiveContext` lists exposed entities with states; default tier `confirm` and targets resolved by `HaTargetResolver`; implement and verify they pass
- [x] 3.2 Printer: directory, client (allowlisted macros only), state cache, snapshots and webcam; verify a cancel through the tools is frozen as a pending action and executes on confirmation, and a pause (safe tier) runs at once
- [x] 3.3 Mail: account store, provider (search, read, draft append visible in later listings); verify by test that no send member exists and no network connection is opened
- [x] 3.4 RSS and full text, notes source; verify the RSS and notes tools return the demo content and the notes index indexes the sample notes
- [x] 3.5 Write an end-to-end test with a fake chat client: the host is configured with only a model and demo on, the tool list contains the five integrations' tools, and a light turned on is reported `on` by a later state read; verify it passes

## 4. Seeding

- [x] 4.1 Add the `demo_seed` entity and migration, update `erd.puml` and re-render; verify the migration test passes
- [x] 4.2 Write failing `DemoSeeder` tests: seeds conversations (web and voice from `kitchen` and `office`), runs with steps, tasks with results, notifications, one pending approval and memory chunks with edges, all for the owner, with times relative to the seed time, and each row recorded in `demo_seed`; implement and verify they pass
- [x] 4.3 Add the seed parts for background results and reasoning steps behind a check for those features; verify by test that they are skipped when the features are missing and seeded when present
- [x] 4.4 Write failing tests for `DemoSeedService`: seeds once at startup with an owner, not again after a restart, and after first-run setup creates the owner; implement and verify they pass

## 5. Reset and CLI

- [x] 5.1 Write failing tests for reset: seed, add a user task, reset; seeded rows are gone, the user task stays, and per-table row counts equal those before seeding; audit events `demo.reset` and `demo.seed` are written; implement and verify they pass
- [x] 5.2 Add `marshal demo reset|seed` to the CLI with a CLI test; verify both commands run against a test database without starting the web host

## 6. UI

- [x] 6.1 Write a failing API test that `/api/v1/me` carries `demo { enabled, showBadge }` and that the system integration list marks simulated integrations; implement, regenerate OpenAPI and web types, and verify `npm run build` passes
- [x] 6.2 Write failing component tests for the shell badge (shown, hidden by `showBadge: false`, both layouts) and for the system view's simulated markers; implement, add preview scenarios, and verify they pass
- [x] 6.3 Check the badge with the `web-ui-polish` screenshot sweep at 375 px and 1280 px, light and dark

## 7. Docs and integration check

- [x] 7.1 Write the how-to page for demo mode (enable, what is simulated, reset, screenshots without the badge) and the configuration reference entries; verify they follow `docs/development/docs-style.md`
- [x] 7.2 Start the container with only a model configured and `demo.enabled: true`; confirm every view shows data, a chat can turn on a light and cancel the print with confirmation, and `marshal demo reset` empties the seeded views. Checked in the built image with `local` auth: seeding after first-run setup, every view with data (memory empty without an embedding model), `marshal demo reset` empties the seeded views, a restart does not seed again. Not checked: the chat flows (light, print cancel with confirmation), because no model is reachable locally
