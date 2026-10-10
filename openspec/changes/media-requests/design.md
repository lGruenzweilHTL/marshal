# Design

## Context

See proposal.md (Why). External facts. Seerr is the merge of Overseerr and Jellyseerr, so check its fields during implementation against the running version's `/api-docs`:

- **Seerr API** (`/api/v1`, header `X-Api-Key`; the key belongs to the admin, so requests made with it are auto-approved):
  - `GET /search?query=<encoded>&page=1`: results of `mediaType` `movie`, `tv` or `person`, with `id` (TMDB), `title`/`name`, `releaseDate`/`firstAirDate`, `overview` and `mediaInfo` (`status`, `jellyfinMediaId`, `requests`). The query must be percent-encoded (`%20`, not `+`).
  - `GET /movie/{tmdbId}`, `GET /tv/{tmdbId}`: details. Shows include `seasons` (`seasonNumber`, season 0 = specials) and `mediaInfo`.
  - `POST /request` with `{ mediaType, mediaId, seasons: number[] | "all" }`.
  - `GET /request?take=20&skip=0&sort=added&filter=<all|approved|available|pending|processing|unavailable|failed>`.
  - `MediaStatus`: 1 unknown, 2 pending, 3 processing, 4 partially available, 5 available, 6 blocklisted, 7 deleted. `MediaRequestStatus`: 1 pending, 2 approved, 3 declined, 4 failed, 5 completed.
- **Seerr webhook agent:** the JSON payload is a user-editable template. Values are `{{…}}` variables. The special keys `"{{media}}"` and `"{{request}}"` become `media` / `request` objects, or `null` when the notification has none. Media variables: `media_type`, `media_tmdbid`, `media_tvdbid`, `media_status`, `media_status4k`, `media_jellyfinMediaId`. Request variables: `request_id`, `requestedBy_*`. The "Authorization Header" field is sent as `Authorization`.
- **Sonarr / Radarr API v3** (header `X-Api-Key`). API keys are full-access; neither app has read-only keys.
  - `GET /api/v3/calendar?start=&end=` (Sonarr with `includeSeries=true`). Episodes have `seasonNumber`, `episodeNumber`, `title`, `airDateUtc`, `hasFile`. Radarr movies have `inCinemas`, `digitalRelease`, `physicalRelease`, `hasFile`.
  - `GET /api/v3/queue?pageSize=50` (with `includeSeries`/`includeEpisode` or `includeMovie`). Records have `size`, `sizeleft`, `timeleft`, `estimatedCompletionTime`, `status`, `trackedDownloadState`.
- **Jellyfin web:** item page `<base>/web/#/details?id=<itemId>`.
- The webhook ingest (`IWebhookNormalizer`, `WebhookIngest`; normalizers in `Marshal.Tasks/Webhooks`) handles scopes, size limit, rate limit and debounce. A source is a normalizer plus a scope constant, as for `proxmox`.

## Goals / Non-Goals

**Goals:**
- Disambiguation and confirmation in one step. The model searches and picks a TMDB id. The confirmation names title and year, and execution checks that pair against Seerr.
- Webhook events without free text, so they are trusted.

**Non-Goals:**
- Caching. Every tool call reads the services (homelab latency).
- Per-user Seerr identities (`userId` on requests). One user per instance; the request appears as the API key's admin user.

## Decisions

### 1. Project and interfaces

New project `Marshal.Integrations.Media` (references `Marshal.Abstractions` only), registered by `Marshal.Api`. Its tool source offers no tools while `media` is unset. Namespace `media` (capability, not provider), reserved by this change.

| Interface | v1 implementation | Purpose |
|---|---|---|
| `ISeerrClient` | `SeerrRestClient` (named `HttpClient`, resilience handler) | `SearchAsync`, `GetTitleAsync(type, tmdbId)`, `CreateRequestAsync`, `GetRequestsAsync`; typed records, `Result<T>` with `media.unavailable` |
| `IArrClient` | `ArrRestClient` | `IsConfigured(kind)`, `GetCalendarAsync(kind, start, end)`, `GetQueueAsync(kind)`; one class for Sonarr and Radarr, because the v3 APIs share auth, paging and the queue shape. The kind decides the include parameters and the record mapping |
| `IJellyfinLinks` | `JellyfinLinks` | item id → link, or null without `jellyfinUrl`; shared by the tools and `SeerrNormalizer` |

`IJellyfinLinks` lives in `Marshal.Abstractions` (the normalizer in `Marshal.Tasks` must not reference the integration project). `MediaToolSource : IToolSource` depends only on these interfaces, so tool tests use hand-written fakes. The REST clients are tested against in-process fake servers (`Microsoft.AspNetCore.TestHost`) with recorded-shape JSON. The tests also assert the HTTP methods sent, which covers the spec's limited write access.

Alternatives:
- **The Seerr MCP servers from the community:** rejected, as for Proxmox. A third-party server would hold an admin key, its tool surface drifts, and tests would need it.
- **Talking to Radarr/Sonarr directly for requests:** rejected. Seerr owns quality profiles, root folders and request history, and the user already uses it.

### 2. Tools

| Tool | Tier | Untrusted output | Hidden when |
|---|---|---|---|
| `media.search(query, type?)` | safe | yes | `media` not configured |
| `media.request(type, tmdbId, title, year, seasons?)` | confirm | yes | same; and by `denyConfirm` |
| `media.requests(status?)` | safe | yes | same |
| `media.upcoming(days?)` | safe | yes | same |
| `media.downloads()` | safe | yes | same |

- `type` is `movie` or `show`. The tool maps `show` to Seerr's `tv`; the model-facing word is the common one.
- Person results from Seerr's search are dropped. Search results carry no seasons, so `media.search` reads the details of each show among the results (at most 10, in parallel). A failed detail call only drops the season count.
- Seerr's request list has no titles, so `media.requests` reads the details of each listed request (at most 20, in parallel).
- Overviews are cut to 200 characters. They help the model tell "Dune" apart, but are third-party text, which is why every media tool is untrusted. Taint source `media`.
- `seasons` is a list of season numbers. Omitted means `"all"` (user decision). Numbers a show does not have fail with `media.invalid_arguments`.
- **Confirmation text without a lookup.** `ITool.Summarize` is synchronous and sees only the arguments, so the model passes `title` and `year` from `media.search`. The summary is built from them: "Dune (2021), movie" or "Severance (2022), show, all seasons". The frozen action holds `type`, `tmdbId`, `title`, `year`, `seasons`.
- **Checked at execution.** The confirmed call reads `GET /movie|tv/{id}` first. Unknown id: not found. Title or year differs from the frozen values: `media.title_mismatch`. Available or requested: `media.already_*`. Only then `POST /request`. The user can only get the title they heard, even if the model paired it with a wrong id.
- Alternative: an async pre-confirmation hook on `ITool`, so the guard could ask with Seerr's title. Rejected: it changes the core tool contract for one tool, and the mismatch check gives the same guarantee.
- `media.upcoming` merges both calendars sorted by date and returns at most 50 entries. `media.downloads` merges both queues.
- The tools stay visible while a service is unreachable and fail with `media.unavailable` on call, like other integrations.

### 3. Webhook source

`SeerrNormalizer : IWebhookNormalizer` (source `seerr`, trusted) in `Marshal.Tasks/Webhooks`. `ApiScopes.HooksSeerr` joins `ApiScopes.All`, so admins get it automatically. Only `notification_type`, `media.{media_type,tmdbId,jellyfinMediaId}` and `request.request_id` are read; template values arrive as strings and are parsed. Recommended setup (documented in the example config):

```
Seerr → Settings → Notifications → Webhook
  Webhook URL:          https://marshal.lan/api/v1/hooks/seerr
  Authorization Header: Bearer <marshal tokens create --name seerr --scope hooks:seerr>
  JSON Payload:
    {"notification_type":"{{notification_type}}",
     "{{media}}":{"media_type":"{{media_type}}","tmdbId":"{{media_tmdbid}}",
                  "status":"{{media_status}}","jellyfinMediaId":"{{media_jellyfinMediaId}}"},
     "{{request}}":{"request_id":"{{request_id}}"}}
  Types: Request Approved, Request Automatically Approved, Request Available,
         Request Declined, Request Failed
```

Debounce (source + type + entity) collapses Seerr's duplicate notifications for one title within 60 s. The normalizer needs `IJellyfinLinks`, so webhook sources are registered with an explicit source name instead of being constructed once to read it. `Marshal.Tasks` registers an empty `IJellyfinLinks` fallback with `TryAdd`, so it works without the media integration.

### 4. "Available" notification is a task

No built-in notifier. Following "generic mechanisms over special cases", the example `tasks/media-available.yaml` holds the arguments of `tasks.watch` (event triggers are created by `tasks.watch`, not `tasks.create`): filter `source: seerr, type: media.available`, a goal ("Tell me in one push which title became available, with the Jellyfin link") and a push delivery. The run uses the default model tier: `tasks.watch` has no tier setting. The event is trusted, so the run starts untainted. To name the title, the run calls `media.requests`, and that taints it. A push needs no confirmation, so this does not matter. The owner can change wording, channel or quiet hours like any task.

Alternative: a built-in notifier that formats the push itself (no model call). Rejected for v1: it duplicates the task system and fixes the wording. Revisit if the model cost matters.

### 5. Configuration

```yaml
media:
  seerr:  { url: https://seerr.lan/,  apiKeySecret: seerr_api_key }
  sonarr: { url: https://sonarr.lan/, apiKeySecret: sonarr_api_key }   # optional
  radarr: { url: https://radarr.lan/, apiKeySecret: radarr_api_key }   # optional
  jellyfinUrl: https://jellyfin.lan/                                    # optional, for links
```

Validation: absolute http(s) URLs (a base path such as `https://lan/seerr/` is kept), valid secret names. Keys are read per request, so replacing a secret needs no restart. A missing value fails with `media.unavailable` naming the secret.

## Risks / Trade-offs

- **Sonarr/Radarr keys are full-access.** Marshal's clients send only `GET`, enforced by tests. The example config tells the user to put the services on the LAN only and to treat the keys as secrets.
- **Seerr's API key is an admin key.** Only `GET` plus `POST /request` exist in code. Approve, decline and delete are not implemented (spec: limited write access).
- **Seerr fields drift after the merge.** The JSON fixtures are recorded from the running version during implementation. Unknown fields are ignored and missing optional ones become null.
- **The model picks the wrong TMDB id.** The id is checked against the confirmed title and year before requesting (`media.title_mismatch`). A model that invents a title and year matching a wrong id is caught when the user hears the confirmation.
- **The user confirms and then hears "already available".** Availability is checked after confirmation, not before. Acceptable: `media.search` shows availability, so the model rarely offers such a request.
- **Webhook `media_status` arrives as a status name string in some versions.** The normalizer does not need it for the event type (the notification type decides). It is ignored when unparsable.
