# Request and track media with Seerr, Sonarr and Radarr

Connect Marshal to Seerr so you can ask for a movie or show by voice or chat, and optionally to Sonarr and Radarr to ask what airs soon and what is downloading.

Marshal can only read from Sonarr and Radarr. In Seerr it can only read and create requests.

## Before you start

- A running Seerr instance that Marshal's container can reach over `http` or `https`.
- Optional: Sonarr and Radarr, and a Jellyfin web client, for upcoming releases, download status and links.
- The API key of each service: Seerr under **Settings > General**, Sonarr and Radarr under **Settings > General > Security**.
- Shell access to the secret store, as described in [secrets](../secrets.md).

> [!WARNING]
> Seerr's API key belongs to its admin user, and Sonarr and Radarr have no read-only keys. Marshal sends only the allowed requests (see [limits](#limits)), but the keys themselves are powerful. Keep these services on your LAN and keep the keys in secrets.

## Steps

::: steps
1. **Store the API keys as secrets**

   Create one secret per service, for example with Docker secrets or `MARSHAL_SECRET_<NAME>` environment variables. Names are lowercase letters, digits and underscores, starting with a letter.

   ```text
   seerr_api_key
   sonarr_api_key
   radarr_api_key
   ```

   Marshal reads each secret on every request, so you can replace a key without a restart.

2. **Add the `media` section**

   Add this to `/config/marshal.yaml`. Only `seerr` is required. Leave out `sonarr`, `radarr` or `jellyfinUrl` if you do not use them.

   ```yaml
   media:
     seerr:  { url: https://seerr.lan/,  apiKeySecret: seerr_api_key }
     sonarr: { url: https://sonarr.lan/, apiKeySecret: sonarr_api_key }
     radarr: { url: https://radarr.lan/, apiKeySecret: radarr_api_key }
     jellyfinUrl: https://jellyfin.lan/
   ```

   Every `url` and `jellyfinUrl` must be an absolute `http` or `https` URL. Every `apiKeySecret` must be a secret name. Startup fails with one of these messages if a rule is broken:

   - `media.seerr.url is required when a media section is configured.`
   - `media.seerr needs an absolute http(s) url and an apiKeySecret naming a secret.` (the same text applies to `sonarr` and `radarr`)
   - `media.jellyfinUrl must be an absolute http(s) URL.`

   Without a `media` section, Marshal registers no `media_*` tool.

3. **Restart Marshal**

   ```sh
   docker compose restart marshal
   ```

4. **Optional: get a push when a request is ready**

   Seerr can notify Marshal when a requested title becomes available. Marshal turns these notifications into events that a watch task can react on.

   1. Create a token that may post Seerr webhooks:

      ```sh
      docker exec marshal marshal tokens create --name seerr --scope hooks:seerr
      ```

      Marshal prints the token once. Copy it now.

   2. In Seerr, open **Settings > Notifications > Webhook**, enable the agent and set:

      | Field | Value |
      |---|---|
      | Webhook URL | `https://<marshal>/api/v1/hooks/seerr` |
      | Authorization Header | `Bearer <token from step 1>` |
      | Notification Types | Request Approved, Request Automatically Approved, Request Available, Request Declined, Request Failed |
      | JSON Payload | see below |

      ```json
      {"notification_type":"{{notification_type}}","{{media}}":{"media_type":"{{media_type}}","tmdbId":"{{media_tmdbid}}","status":"{{media_status}}","jellyfinMediaId":"{{media_jellyfinMediaId}}"},"{{request}}":{"request_id":"{{request_id}}"}}
      ```

   3. Create a watch for `source: seerr`, `type: media.available`. The file `deploy/config.example/tasks/media-available.yaml` is a ready example; ask Marshal to create a watch with those settings. See [event triggers](../tasks/event-triggers.md).

   For the events and how Marshal limits and authenticates webhooks, see [Uptime Kuma and Proxmox webhooks](uptime-kuma.md#how-webhook-ingest-works).
:::

## Tools

| Tool | Risk tier | What it does |
|---|---|---|
| `media_search` | safe | Up to 10 movies and shows from Seerr with TMDB id, type, title, year, a shortened overview, season count for shows, and availability. |
| `media_request` | confirm | Creates a Seerr request for a movie or show. |
| `media_requests` | safe | Up to 20 newest requests, optionally filtered by status. |
| `media_upcoming` | safe | Episodes and movie releases from the Sonarr and Radarr calendars for the next days. |
| `media_downloads` | safe | The Sonarr and Radarr download queues. |

Details:

- `media_search(query, type?)`: `type` is `movie` or `show`. Availability is one of `unknown`, `pending`, `processing`, `partially_available`, `available`.
- `media_request(type, tmdbId, title, year, seasons?)`: for a show without `seasons`, Marshal requests all seasons.
- `media_requests(status?)`: `status` is `pending`, `approved`, `declined`, `failed` or `completed`.
- `media_upcoming(days?)`: `days` defaults to 7 and is at most 30. Each entry shows whether the file is already downloaded.
- `media_downloads()`: shows title, episode for shows, percent done, estimated time left and state.

Without `sonarr` or `radarr`, `media_upcoming` and `media_downloads` answer without that service's data and say which service is not configured. All five tools fail with `media.unavailable` when a service cannot be reached or rejects its API key.

### How a request is confirmed

`media_request` is in the confirm tier, so Marshal freezes the call and asks you first. The question names the title, year and type, and for a show "all seasons" or the season numbers, for example "request Dune (2021), movie". After you confirm, Marshal checks that Seerr's title and year for that TMDB id match what you heard. If they differ, the call fails with `media.title_mismatch` and nothing is requested. Marshal also refuses with `media.already_available` or `media.already_requested` when Seerr says the title is available or requested. See [approvals](../tasks/approvals.md) and [risk tiers](../../reference/risk-tiers.md).

### Jellyfin links

When `jellyfinUrl` is set and Seerr reports a Jellyfin item id for a title, results and `seerr` events include a link to the item's page in the Jellyfin web client. With `jellyfinUrl: https://jellyfin.lan/` and item `abc123`, the link is `https://jellyfin.lan/web/#/details?id=abc123`. Marshal builds links only from `jellyfinUrl` and the item id, and never calls Jellyfin.

## Limits

- Sonarr and Radarr clients send `GET` requests only.
- The Seerr client sends `GET` requests and the `POST` that creates a request. Marshal cannot delete, approve or decline requests, change or remove series or movies, start searches or change settings.
- Titles and overviews come from third parties. The output of every media tool is untrusted: a run that calls one is tainted with source `media`. See [untrusted content](../../concepts/untrusted-content.md).
- `media_search` lists at most 10 results, `media_requests` at most 20, `media_upcoming` looks at most 30 days ahead.
- Seerr webhook events carry only ids and the Jellyfin link, never free text.

## Check that it works

1. Open the web UI and ask "Search for Dune". The answer lists the 1984 and 2021 movies and the 2000 show as separate entries with year and TMDB id.
2. Ask "Request Dune from 2021". Marshal asks for confirmation and names "Dune (2021), movie". Confirm, then check Seerr for the new request.
3. Ask "What downloads right now?" and "What airs this week?" if you configured Sonarr or Radarr.
4. If you set up the webhook, use Seerr's **Test** button. Marshal answers 202 and publishes no event. Real notifications produce `media.available`, `media.approved`, `media.declined` and `media.failed` events.

If a tool fails with `media.unavailable`, the message names the service or secret. Check the URL, the API key and that the secret resolves. See [troubleshooting](../troubleshooting.md).

## Related

- [`media` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Tool reference](../../reference/tools.md) and [event reference](../../reference/events.md)
- [Secrets](../secrets.md)
- [Machine tokens](../authentication/machine-tokens.md)
- [Music](music.md)
