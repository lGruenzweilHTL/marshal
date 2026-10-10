# Tasks

## 1. Project and clients

- [ ] 1.1 Create `src/Marshal.Integrations.Media` and `tests/Marshal.Integrations.Media.Tests`, add both to `Marshal.sln`, reference the project from `Marshal.Api`; verify `dotnet build` succeeds
- [ ] 1.2 Write failing tests for `MediaOptions` validation (absolute http(s) URLs, base paths kept, valid secret names, optional `sonarr` / `radarr` / `jellyfinUrl`); implement and register in the configuration stage; verify with the Media tests
- [ ] 1.3 Record JSON fixtures from the running Seerr version (search with movie, show and person results, movie and show details with seasons and `mediaInfo`, request list, request creation). Write failing `SeerrRestClient` tests against an in-process fake server: `X-Api-Key` header, percent-encoded query, status mapping, 401 and connection errors → `media.unavailable`, missing secret value names the secret, only `GET` and `POST /request` sent. Implement `ISeerrClient`; verify the tests pass
- [ ] 1.4 Record Sonarr and Radarr fixtures (calendar, queue). Write failing `ArrRestClient` tests for both kinds: include parameters, date range, queue percent and time left, only `GET` sent. Implement `IArrClient`; verify the tests pass
- [ ] 1.5 Write failing tests for `JellyfinLinks` (link format, base path, null without `jellyfinUrl`); implement it with `IJellyfinLinks` in `Marshal.Abstractions`; verify the tests pass

## 2. Tools

- [ ] 2.1 Write failing tests with fake clients for `media.search` (movies and shows only, at most 10, overview cut to 200 characters, seasons count, availability, Jellyfin link) and `media.requests` (status filter, at most 20); implement them in `MediaToolSource`; verify the tests pass
- [ ] 2.2 Write failing tests for `media.request`, covering the spec scenarios:
  - summary "Dune (2021), movie" and "…, show, all seasons";
  - all seasons by default, explicit season list, unknown season → `media.invalid_arguments`;
  - title or year mismatch → `media.title_mismatch` with no POST; unknown id → not found;
  - `media.already_available` / `media.already_requested` with no POST.

  Implement it; verify the tests pass
- [ ] 2.3 Write failing tests for `media.upcoming` (default 7 days, max 30, merged and sorted, at most 50, "not configured" note per missing service) and `media.downloads` (merged queues); implement them; verify the tests pass
- [ ] 2.4 Reserve the `media` namespace (`ReservedNamespaces`, with a registry test that an MCP server with id `media` is rejected). Write failing tests: no media tools without a `media` section; descriptors have the spec's tiers and are all untrusted output with taint source `media`. Implement the registration; verify with `dotnet test --project tests/Marshal.Api.Tests`

## 3. Webhook source

- [ ] 3.1 Add JSON fixtures for the documented payload template (available movie, approved and auto-approved show, declined, failed, pending, test notification, `media: null`, extra `subject` / `message` / `image` keys, missing `tmdbId`, invalid JSON). Write failing `SeerrNormalizer` tests from the spec scenarios (entity `<type>:<tmdbId>`, Jellyfin link in the payload, trusted). Implement the normalizer; verify with `dotnet test --project tests/Marshal.Tasks.Tests`
- [ ] 3.2 Add `ApiScopes.HooksSeerr` and register the normalizer. Write failing API tests: 202 + event for a `hooks:seerr` token, 403 for a `hooks:proxmox` token, admin role gets the scope, CLI token creation. Verify with `dotnet test --project tests/Marshal.Api.Tests`

## 4. Documentation and example task

- [ ] 4.1 Add the `media` section and the Seerr webhook setup (URL, Authorization header, payload template, notification types, token scope) to `deploy/config.example/marshal.yaml`, with a note that Sonarr/Radarr keys are full-access; verify `ExampleConfigTests` passes
- [ ] 4.2 Add `deploy/config.example/tasks/media-available.yaml` (event trigger `source: seerr, type: media.available`, `fast` tier, push delivery); write a failing test that it parses as valid `tasks.create` arguments with an event trigger, then make it pass
- [ ] 4.3 Update the CLAUDE.md repo map and `docs/diagrams/packages.puml` (new project, reference from `Marshal.Api`), re-render the SVG; verify the SVG renders

## 5. Integration checks

- [ ] 5.1 Run the full .NET test suite and `openspec validate media-requests --strict`; both pass
