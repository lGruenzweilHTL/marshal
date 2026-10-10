# Tasks

## 1. Configuration and speakers

- [x] 1.1 Write failing tests for `MusicOptions` validation (speaker name pattern, `media_player` domain, error key `music.speakers.<name>`); implement and register in the configuration stage; verify with `dotnet test --project tests/Marshal.Core.Tests`
- [x] 1.2 Write failing tests for the satellite profile `speaker` key: parsed, unknown name → error naming `satellites.yaml` and `speaker`, hot reload. Add `ISpeakerDirectory` to Abstractions and implement `ConfiguredSpeakerDirectory`; verify with the Core tests
- [x] 1.3 Write failing `SatelliteContextContributor` tests: speaker line with this room's speaker and all names; no line without speakers; names only without a profile speaker. Implement; verify with the Core tests

## 2. Home Assistant player

- [x] 2.1 Build fixtures from the documented service schema (search response, get_queue response, `config_entries/get`). Write failing `HomeAssistantMusicPlayer` tests against the fake HA websocket server:
  - `call_service` domain, service, target and data for play (enqueue, radio_mode), transfer (`source_player`), join/unjoin, pause/resume/next/previous/stop, `volume_set` scaling;
  - `return_response` for search and queue; config entry id cached until reconnect;
  - HA error → `music.failed` naming the speaker; disconnected → `ha.unreachable`.

  Implement `IMusicPlayer`; verify with `dotnet test --project tests/Marshal.Integrations.HomeAssistant.Tests`

## 3. Tools

- [x] 3.1 Write failing `MusicToolSource` tests with fakes for every spec scenario:
  - default speaker, named speaker, `music.no_speaker` and `music.unknown_speaker` with names;
  - search limits and output shape; queue shape; transfer from the turn's speaker; group validation;
  - volume range, URL rejection (`https`, `file`, `http://track/x`) and `library://album/12` accepted.

  Implement the tools; verify with the HA integration tests
- [x] 3.2 Reserve the `music` namespace (`ReservedNamespaces`, registry test that an MCP server with id `music` is rejected). Write failing tests: no music tools without `music` or without HA; all safe; `music.search` and `music.queue` untrusted with taint source `music`; `denyTools: [music.*]` hides them. Implement the registration; verify with `dotnet test --project tests/Marshal.Api.Tests`

## 4. Documentation

- [x] 4.1 Add the `music` section to `deploy/config.example/marshal.yaml` (with the advice not to expose MA players to Assist) and `speaker` examples to `satellites.yaml`; verify `ExampleConfigTests` passes
- [x] 4.2 Update the CLAUDE.md repo map line for `Marshal.Integrations.HomeAssistant` (music); verify the line names music

## 5. Integration checks

- [ ] 5.1 Once Music Assistant is installed: record real `search`, `get_queue` and `config_entries/get` responses and replace the schema-based fixtures. Play, transfer and group on two real speakers; record the results in the change — open at archive (2026-10-10): Music Assistant is not installed yet; tracked in docs/feature-roadmap.md
- [x] 5.2 Run the full .NET test suite and `openspec validate music-assistant --strict`; both pass
