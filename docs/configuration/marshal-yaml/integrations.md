---
icon: fa-solid fa-plug
description: "Keys of the integration sections of marshal.yaml: Home Assistant, CalDAV calendars, printers, FreshRSS, notes, web search, mail OAuth clients, Proxmox, media, music, GitHub and executors."
---

# Integration sections

Keys of the integration sections of `marshal.yaml`: Home Assistant, CalDAV calendars, printers, FreshRSS, notes, web search, mail OAuth clients, Proxmox, media, music, GitHub and executors. Every section is optional. An integration's tools stay hidden until its section is set. Changes need a restart.

Secret keys (`...Secret`) hold secret names, never values. See [secrets reference](../secrets-reference.md).

## Example

```yaml
ha:
  url: http://homeassistant.local:8123
  tokenSecret: ha_token

printers:
  - name: voron
    url: http://voron.local:7125
    apiKeySecret: moonraker_voron
    macros: [PARK, LOAD_FILAMENT]

github:
  tokenSecret: github_pat
  repos:
    marshal: lukas/marshal
```

## `ha` (Home Assistant)

| Key | Type | Default | Description |
|---|---|---|---|
| `ha.url` | URL | unset | Base URL of Home Assistant, including a path base behind a proxy. Unset: Home Assistant is off. |
| `ha.tokenSecret` | secret name | unset | Long-lived access token of an admin user (the exposure list needs admin). The same secret can authenticate the HA MCP server in `mcp/ha.yaml`. |

Validation: `ha.url must be an absolute http(s) URL.`, `ha.tokenSecret is required when ha.url is set.` The token must resolve at startup.

## `calendar.caldav` (CalDAV calendars)

CalDAV accounts of the instance owner, next to the Home Assistant calendars. Calendars are discovered from the URL (principal, then calendar home set) and addressed as `caldav:<account>/<calendar>`. An account that fails discovery or login raises a health warning and does not affect other calendars. Setup per server: [use your calendars](../../guides/integrations/calendar.md).

| Key | Type | Default | Description |
|---|---|---|---|
| `calendar.caldav.accounts` | map | empty | Accounts by id. The id matches `[a-z][a-z0-9_]*`. Empty: no CalDAV calendars. |
| `calendar.caldav.accounts.<id>.url` | URL | required | DAV URL. `https`, or `http` for a host on the local network (private address, `localhost`, a single-label name or `.lan`, `.local`, `.home.arpa`, `.internal`, `.localdomain`). |
| `calendar.caldav.accounts.<id>.username` | string | required | Login name. |
| `calendar.caldav.accounts.<id>.passwordSecret` | secret name | required | Secret holding the password or app password. Read per request, so a changed value needs no restart. |
| `calendar.caldav.accounts.<id>.calendars` | list of strings | all | Calendar names (last path segment of the calendar URL) to use. |
| `calendar.caldav.accounts.<id>.pollMinutes` | integer | `5` | Minutes between change polls. 1 to 60. |

Validation: `calendar.caldav.accounts.<id>: the account id must match [a-z][a-z0-9_]*.`, `calendar.caldav.accounts.<id>.url must be an absolute https URL (http only for a host on the local network).`, `calendar.caldav.accounts.<id>.username is required.`, `calendar.caldav.accounts.<id>.passwordSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).`, `calendar.caldav.accounts.<id>.calendars must not contain empty names.`, `calendar.caldav.accounts.<id>.pollMinutes must be 1-60.`

## `printers` and `printerSnapshots` (Moonraker)

`printers` is a list at the top level of the file.

| Key | Type | Default | Description |
|---|---|---|---|
| `printers[].name` | string | none | Name used in tools and the UI. Letters, digits, `-` or `_`. Unique, case-insensitive. |
| `printers[].url` | URL | none | Moonraker base URL, for example `http://voron.lan:7125`. |
| `printers[].webUrl` | URL | unset | Mainsail or Fluidd, linked for actions Marshal does not offer. |
| `printers[].apiKeySecret` | secret name | unset | Only needed when Moonraker requires an API key. |
| `printers[].powerEntity` | string | unset | Home Assistant `switch.*` entity that powers the printer. The `print_done_away` [suggestion](suggestions.md) offers to switch it off. |
| `printers[].macros` | list of strings | `[]` | Allowlist for `printer.run_macro`. Upper-cased. No arguments are ever passed. Raw G-code does not exist as a tool. |
| `printers[].babysitter.enabled` | boolean | `false` | Snapshot checks during prints. Pauses after two failed checks. Needs a webcam and a vision model. |
| `printers[].babysitter.interval` | duration | `00:01:00` | Time between checks. |
| `printerSnapshots.maxSize` | integer (px) | `1024` | Longest side after resizing. |
| `printerSnapshots.quality` | integer | `85` | JPEG quality. |
| `printerSnapshots.retention` | integer | `20` | Snapshots kept per printer. |

Validation: `printers[].name must be letters, digits, '-' or '_'.`, `printers[].name must be unique.`, `printers[].url must be an absolute http(s) URL.`, `printers[].webUrl must be an absolute http(s) URL.`, `printers[].powerEntity must be a switch.* entity, not '<entity>'.`, `printers[].babysitter.interval must be at least 10 seconds.` (when enabled), `printerSnapshots.maxSize must be at least 64.`, `printerSnapshots.quality must be between 1 and 100.`, `printerSnapshots.retention must be at least 1.`

## `freshrss`

The API password is the one set in FreshRSS under Profile > API management, not the login password.

| Key | Type | Default | Description |
|---|---|---|---|
| `freshrss.url` | URL | unset | FreshRSS base URL (API at `api/greader.php` below it). Unset: RSS tools are hidden. |
| `freshrss.userName` | string | empty | FreshRSS user. Required when `url` is set. |
| `freshrss.apiPasswordSecret` | secret name | unset | Secret holding the API password. |
| `freshrss.searchWindow` | integer | `500` | Recent articles (read and unread) `rss.search` looks through. 1 to 5000. |
| `freshrss.pageSize` | integer | `100` | Articles per API request. 1 to 1000. |
| `freshrss.fullText.maxBytes` | integer | `2097152` | Largest page read, in bytes. At least 1024. |
| `freshrss.fullText.timeout` | duration | `00:00:15` | Longest page fetch. Positive. |
| `freshrss.fullText.maxCharacters` | integer | `20000` | Longest text returned to the model. At least 100. |

Validation: `freshrss.url must be an absolute http(s) URL.`, `freshrss.userName is required.`, `freshrss.searchWindow must be 1-5000 and pageSize 1-1000.`, `freshrss.fullText needs maxBytes >= 1024, maxCharacters >= 100 and a positive timeout.`

## `notes`

Markdown files in a git repository. Without `remote`, put a repository into `/data/notes`. With `remote`, Marshal clones it there and mirrors it read-only.

| Key | Type | Default | Description |
|---|---|---|---|
| `notes.remote` | string | unset | Git URL (https, ssh or file). |
| `notes.branch` | string | remote default branch | Branch to follow. |
| `notes.credentialSecret` | secret name | unset | Secret holding an https token or an SSH deploy key (private key). |
| `notes.pullInterval` | duration | `00:15:00` | How often the remote is pulled and the index updated. At least one minute. |
| `notes.maxReadCharacters` | integer | `50000` | Longest note text returned by `notes.read`. At least 1000. |

Validation: `notes.pullInterval must be at least one minute.`, `notes.maxReadCharacters must be at least 1000.`

## `web` (SearXNG web search)

SearXNG must serve JSON (`search.formats: [html, json]` in its `settings.yml`). Keep it on the LAN or a VPN.

| Key | Type | Default | Description |
|---|---|---|---|
| `web.searxng.url` | URL | unset | SearXNG base URL. Unset: web tools are hidden. |
| `web.searxng.authHeaderSecret` | secret name | unset | Secret holding an `Authorization` header value, for a SearXNG behind a reverse proxy. |
| `web.searxng.engines` | list of strings | `[google, bing, duckduckgo, wikipedia]` | SearXNG engine names sent with every request. The query text reaches each of them. |
| `web.searxng.maxResults` | integer | `8` | Most results one search returns, and the default `limit` of `web.search`. 1 to 20. |

Validation: `web.searxng.url must be an absolute http(s) URL.`, `web.searxng.authHeaderSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).`, `web.searxng.maxResults must be 1-20.`, `web.searxng.engines must not contain empty names.`

## `oauth` (mail account connections)

OAuth clients that let users connect Gmail or Microsoft mail accounts in the web UI. Without a client, the connect button is disabled. Register the redirect URI `<public URL>/oauth/callback/google` (or `/microsoft`). Google needs the `https://mail.google.com/` scope; Microsoft needs `IMAP.AccessAsUser.All` and `offline_access`. Marshal never sends mail.

| Key | Type | Default | Description |
|---|---|---|---|
| `oauth.google.clientId` | string | empty | Client id. |
| `oauth.google.clientCredentialSecret` | secret name | unset | Secret holding the client secret. |
| `oauth.microsoft.clientId` | string | empty | Client id. |
| `oauth.microsoft.clientCredentialSecret` | secret name | unset | Secret holding the client secret. |

No validator. A client counts as configured only when the id and the resolved secret are present.

## `proxmox`

Read-only Proxmox VE access for the `infra.*` tools. Create an API token with privilege separation and the `PVEAuditor` role on `/`. Marshal sends GET requests only.

| Key | Type | Default | Description |
|---|---|---|---|
| `proxmox.url` | URL | unset | API URL of one cluster node, for example `https://pve.lan:8006/`. Unset: tools are hidden. Must be https. |
| `proxmox.tokenId` | string | empty | `user@realm!name`. |
| `proxmox.tokenSecret` | secret name | unset | Secret holding the token value (a UUID). Read per request. |
| `proxmox.certificateFingerprint` | string | unset | SHA-256 fingerprint of the node's self-signed certificate: 64 hex digits, colons allowed. Pins the certificate. |

Validation (when `url` is set): `proxmox.url must be an absolute https URL.`, `proxmox.tokenId must look like user@realm!name.`, `proxmox.tokenSecret must name a secret.`; and `proxmox.certificateFingerprint must be a SHA-256 fingerprint (64 hex digits, colons allowed).`

## `media`

Seerr for search and requests (requests need confirmation), optional read-only Sonarr and Radarr calendars and queues, and Jellyfin links. Tools are hidden until `media.seerr.url` is set. Sonarr and Radarr keys are full-access keys; Marshal sends GET requests only.

| Key | Type | Default | Description |
|---|---|---|---|
| `media.seerr.url` | URL | unset | Seerr base URL. |
| `media.seerr.apiKeySecret` | secret name | unset | Seerr API key (admin). Read per request. |
| `media.sonarr.url`, `media.sonarr.apiKeySecret` | URL, secret name | unset | Optional. |
| `media.radarr.url`, `media.radarr.apiKeySecret` | URL, secret name | unset | Optional. |
| `media.jellyfinUrl` | URL | unset | Jellyfin web client base URL, for links. |

Validation: `media.seerr.url is required when a media section is configured.`, `media.seerr needs an absolute http(s) url and an apiKeySecret naming a secret.` (same for `sonarr`, `radarr`), `media.jellyfinUrl must be an absolute http(s) URL.`

## `music` (Music Assistant)

Needs `ha.url` and the Music Assistant integration in Home Assistant.

| Key | Type | Default | Description |
|---|---|---|---|
| `music.speakers.<name>` | string | none | Speaker name mapped to a Music Assistant `media_player` entity id. The `music_*` tools take these names only. A satellite plays on the speaker named in its `satellites.yaml` profile. |

Do not expose the Music Assistant players to Assist: HA's own media intents then offer a second way to control the same speaker.

Validation: `music.speakers.<name> must be named with lowercase letters, digits and underscores, starting with a letter.`, `music.speakers.<name> must be a media_player entity id.`

## `github`

Read-only access for the `repos.*` tools through a fine-grained personal access token limited to the listed repositories (read-only Metadata, Contents, Pull requests, Issues, Actions). The model sees only the ids. Tool output is third-party text and taints the run.

| Key | Type | Default | Description |
|---|---|---|---|
| `github.tokenSecret` | secret name | unset | Secret holding the token. Read per request: replace the value without a restart. |
| `github.repos.<id>` | string | none | `owner/name`. The id is what the model uses. |

Validation: `github.repos ids must be lowercase letters, digits and dashes, starting with a letter.`, `github.repos values must be owner/name.`, `github.tokenSecret must name a secret when repos are configured.` Without repos there are no tools.

## `executors`

Coding sessions on the separate executor host. `tasks.run_executor` is hidden until `executors.claudeCode.url` is set.

| Key | Type | Default | Description |
|---|---|---|---|
| `executors.callbackBaseUrl` | URL | unset | Marshal as seen from the executor host. Callbacks go to `api/v1/executor/callbacks/<session>` below it. |
| `executors.claudeCode.url` | URL | unset | Base URL of the executor host. |
| `executors.claudeCode.tokenSecret` | secret name | unset | Secret holding the executor host's token. |
| `executors.claudeCode.repositories[].name` | string | empty | Directory below the host's `repositoriesRoot`. |
| `executors.claudeCode.repositories[].description` | string | unset | What the repository is, for the model. |

Validation: `executors.callbackBaseUrl must be an absolute URL.`, and when `claudeCode.url` is set: `executors.claudeCode needs an absolute url, a tokenSecret name, at least one repository and executors.callbackBaseUrl.`

## Related

::: cards
- [Home Assistant](../../guides/integrations/home-assistant.md)
- [Printers](../../guides/integrations/printers-moonraker.md)
- [RSS](../../guides/integrations/rss-freshrss.md)
- [Notes](../../guides/integrations/notes.md)
- [Web search](../../guides/integrations/web-search.md)
- [Mail](../../guides/integrations/mail.md)
- [Proxmox](../../guides/integrations/proxmox.md)
- [Media](../../guides/integrations/media.md)
- [Music](../../guides/integrations/music.md)
- [GitHub](../../guides/integrations/github.md)
- [Executor host](../../installation/executor-host.md)
:::
