# media-requests Specification

## Purpose
Media requests and download status through Seerr, Sonarr and Radarr: find a title, request it after confirmation, and see what is coming and downloading.

## Requirements

### Requirement: Media configuration
Media SHALL be configured as `media: { seerr: { url, apiKeySecret }, sonarr?: { url, apiKeySecret }, radarr?: { url, apiKeySecret }, jellyfinUrl? }`. Every `url` and `jellyfinUrl` SHALL be an absolute `http` or `https` URL. Every `apiKeySecret` SHALL be the name of a secret holding that service's API key, read from the secret store per request. Without a `media` section, no `media_*` tool SHALL exist. Without `sonarr` or `radarr`, the tools SHALL answer without that service's data and say which service is not configured.

#### Scenario: Not configured
- **WHEN** `marshal.yaml` has no `media` section
- **THEN** no `media_*` tool is in any tool list

#### Scenario: Seerr only
- **WHEN** `media` configures only `seerr` and `media_upcoming` is called
- **THEN** the result is empty and says that Sonarr and Radarr are not configured

#### Scenario: Missing secret value
- **WHEN** the secret named by `media.seerr.apiKeySecret` has no value
- **THEN** the call fails with `media.unavailable` naming the secret

### Requirement: Limited write access
The Sonarr and Radarr clients SHALL only send `GET` requests. The Seerr client SHALL only send `GET` requests and `POST` requests that create a request. No code path SHALL delete, approve or decline requests, remove or change series or movies, start searches, or change any settings.

#### Scenario: No delete path
- **WHEN** the model asks to remove a movie from the library
- **THEN** no media tool can do it and no request other than the allowed ones is sent

### Requirement: Media tools
The system SHALL provide these tools, each failing with `media.unavailable` when a needed service cannot be reached or rejects the API key:
- `media.search(query, type?)` (safe): up to 10 movies and shows from Seerr's search, each with TMDB id, type, title, year, a shortened overview, the number of seasons for shows, and the availability (`unknown`, `pending`, `processing`, `partially_available`, `available`);
- `media.request(type, tmdbId, title, year, seasons?)` (confirm): creates a Seerr request for the movie or show. For a show without `seasons`, it SHALL request all seasons. It SHALL fail with `media.already_available` or `media.already_requested` when Seerr reports the title as available or requested, and with not found for an unknown TMDB id;
- `media.requests(status?)` (safe): up to 20 newest requests with title, type, year, request status (`pending`, `approved`, `declined`, `failed`, `completed`), availability and requested time, filtered by status;
- `media.upcoming(days?)` (safe): episodes and movie releases from the Sonarr and Radarr calendars for the next `days` days (default 7, at most 30), with title, episode number and title for episodes, air or release date, and whether the file is already downloaded;
- `media.downloads()` (safe): the Sonarr and Radarr download queues with title, episode for shows, percent done, estimated time left and state.
The output of every media tool SHALL be untrusted, because titles and overviews come from third parties.

#### Scenario: Ambiguous title
- **WHEN** `media_search` is called with `Dune`
- **THEN** the result lists the 1984 and 2021 movies and the 2000 show as separate entries with type, year and TMDB id

#### Scenario: Request a movie
- **WHEN** the user confirms `media_request` with `type: movie`, the TMDB id of Dune (2021), `title: Dune` and `year: 2021`
- **THEN** Seerr receives one request for that movie and the result says it was requested

#### Scenario: Show defaults to all seasons
- **WHEN** `media_request` is confirmed for a show with 3 seasons and no `seasons`
- **THEN** Seerr receives a request for all seasons

#### Scenario: Already available
- **WHEN** a confirmed `media_request` names a movie Seerr reports as available
- **THEN** the tool fails with `media.already_available` and no request is sent

#### Scenario: Next episode
- **WHEN** Sonarr's calendar has an episode of a show airing in 3 days and `media_upcoming` is called
- **THEN** the result lists that episode with show title, episode number and air date

#### Scenario: Search taints
- **WHEN** a run calls `media_search`
- **THEN** the run is tainted with source `media`

### Requirement: Request confirmation
The confirmation of `media.request` SHALL name the title, year and type from the call's arguments, and the seasons for a show ("all seasons" or the listed numbers). The frozen action SHALL contain type, TMDB id, title, year and seasons. When the confirmed call runs, the title and year Seerr reports for the TMDB id SHALL match the frozen title (ignoring case and surrounding spaces) and year; otherwise the call SHALL fail with `media.title_mismatch` naming Seerr's title and year, and nothing SHALL be requested. So a confirmed request can only create a request for the title the user heard.

#### Scenario: Confirmation names the title
- **WHEN** `media_request` is called with `type: movie`, the TMDB id of Dune (2021), `title: Dune` and `year: 2021`
- **THEN** the confirmation asks to request "Dune (2021), movie"

#### Scenario: Wrong id for the confirmed title
- **WHEN** the user confirms a request for "Dune (2021)" whose TMDB id belongs to Dune (1984)
- **THEN** the tool fails with `media.title_mismatch` naming "Dune (1984)" and Seerr receives no request

#### Scenario: Unknown TMDB id
- **WHEN** a confirmed `media_request` names a TMDB id Seerr does not know
- **THEN** the tool fails with not found and Seerr receives no request

### Requirement: Jellyfin links
When `media.jellyfinUrl` is set and Seerr reports a Jellyfin item id for a title, tool results and `seerr` events SHALL include a link to that item's page in the Jellyfin web client under `jellyfinUrl`. Links SHALL be built only from `jellyfinUrl` and the item id.

#### Scenario: Link for an available movie
- **WHEN** `jellyfinUrl` is `https://jellyfin.lan/` and Seerr reports Jellyfin item `abc123` for a found movie
- **THEN** the search result links `https://jellyfin.lan/web/#/details?id=abc123`

#### Scenario: No Jellyfin URL
- **WHEN** `jellyfinUrl` is not set
- **THEN** no result contains a Jellyfin link
