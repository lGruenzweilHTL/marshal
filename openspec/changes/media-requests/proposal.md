# Proposal

## Why

The homelab runs Seerr in front of Sonarr and Radarr, with Jellyfin as the library played in the browser. "Request Dune", "is the new season of Severance downloading?" or "when is the next episode?" currently need the Seerr or Sonarr web UI. Seerr already does search, approval and request tracking, and Marshal has a webhook ingest and push notifications, so a small read-mostly client plus one webhook source covers the whole flow. Item 6 of `docs/feature-roadmap.md`.

## What Changes

- New media client for Seerr (search, title details, requests), Sonarr (calendar, queue) and Radarr (calendar, queue), configured as `media: { seerr, sonarr?, radarr?, jellyfinUrl? }` with API key secret names. Sonarr and Radarr are only read. Seerr gets reads plus one write, creating a request.
- New tools in the capability namespace `media`:
  - safe: `media.search(query, type?)`, `media.requests(status?)`, `media.upcoming(days?)`, `media.downloads()`;
  - confirm: `media.request(type, tmdbId, title, year, seasons?)`. The confirmation names title, year, type and seasons, so picking the right "Dune" and confirming happen in one step. Before requesting, the tool checks that the TMDB id really is that title and year. Shows default to all seasons.
  - Titles and overviews come from TMDB through Seerr, so the output of these tools is untrusted.
- Results link to the title's Jellyfin web page when `jellyfinUrl` is set and Seerr knows the Jellyfin item.
- New webhook source `seerr` on `POST /api/v1/hooks/seerr` with scope `hooks:seerr`. Seerr's webhook agent sends a documented body template. The source publishes trusted events `media.approved`, `media.available`, `media.declined` and `media.failed` with media type, TMDB id and Jellyfin link, and no free text.
- Example watch `deploy/config.example/tasks/media-available.yaml` (arguments of `tasks.watch`): an event-triggered task that pushes "<title> is available" with the Jellyfin link.

## Capabilities

### New Capabilities

- `media-requests`: media configuration, the Seerr/Sonarr/Radarr client and the `media.*` tools.

### Modified Capabilities

- `webhook-ingest`: new `seerr` source (body format, event mapping, trust).
- `tool-registry`: `media` joins the reserved namespaces.

## Non-goals

- Playback on TVs or speakers. Jellyfin is watched in the browser. A Jellyfin client and display playback are a later change.
- Deleting, approving or declining requests, removing series or movies, changing quality profiles, triggering searches, and every other Sonarr/Radarr write.
- 4K requests and choosing a quality profile or root folder (Seerr's defaults apply).
- Issues (Seerr's problem reports) and Seerr user management.
- A Jellyfin API client. Availability and the Jellyfin item id come from Seerr.
- Several Seerr, Sonarr or Radarr instances.

## Impact

- New project `src/Marshal.Integrations.Media` with `tests/Marshal.Integrations.Media.Tests`, referenced by `Marshal.Api`.
- `src/Marshal.Tasks/Webhooks`: `SeerrNormalizer`. `ApiScopes` gains `hooks:seerr`.
- `deploy/config.example/marshal.yaml`: `media` section and the Seerr webhook setup. New example task `tasks/media-available.yaml`.
- CLAUDE.md repo map and `docs/diagrams/packages.puml`.
