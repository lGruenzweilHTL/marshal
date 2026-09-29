## Context

Sources: brainstorm notes §13 (Moonraker), §14 (mail), §15 (FreshRSS), §16 (calendar), §17 (notes), §18 views 9 and 10. The HA client from `extensibility` provides calendar access; the memory change provides embeddings infrastructure reused by the notes index. EventBus-dependent features (semantic print events, babysitter, new-mail events) are deliberately left to `background-work`.

## Goals / Non-Goals

**Goals:**
- Five integrations behind small interfaces, each with tools, tiers and taint / redaction flags.
- All three mail account types working with IMAP.
- Printer page and accounts view.

**Non-Goals:**
- Graph / Gmail API providers, notes proposals, native calendars, event sources.

## Decisions

### D1. Moonraker
`MoonrakerClient` (REST, `X-Api-Key`) + `MoonrakerSocket` (JSON-RPC over `ClientWebSocket`, API key header on upgrade; oneshot token fallback). `PrinterStateCache` merges diffs into an immutable snapshot per printer and publishes `printer` topic updates to the live-event stream (UI only; no EventBus yet). Snapshots are resized to max 1024 px (configurable) as JPEG and stored under `/data/snapshots/<printer>/` with a retention count; served through an authenticated endpoint. The webcam stream proxy forwards MJPEG from the discovered `stream_url`.

### D2. Mail
`ImapMailProvider` over MailKit with a connection per account (pooled, short-lived for tools). `ICredentialSource` implementations: `PasswordCredentialSource`, `OAuth2CredentialSource` with presets `google` (scope `https://mail.google.com/`) and `microsoft` (scopes `https://outlook.office.com/IMAP.AccessAsUser.All offline_access`). OAuth client ids and client credentials are instance config (`oauth.google.clientId`, `oauth.google.clientCredentialSecret`, same for `microsoft`). The OAuth callback endpoint is versioned-API-independent (`/oauth/callback/{provider}`), state bound to the user session. Search translates structured fields to IMAP `SEARCH` criteria. Message bodies are converted to text (HTML → text) and wrapped as untrusted data; attachments are listed by name only.
*Alternative:* Graph / Gmail first — rejected by the build order (IMAP covers all accounts first).

### D3. FreshRSS
`FreshRssClient` with `ClientLogin` auth and token refresh; resilience pipeline from `Microsoft.Extensions.Http.Resilience`. `rss.fetch_full_text` resolves the article's canonical link via the API, then fetches with a size and time limit and extracts with SmartReader.

### D4. Calendar
`HomeAssistantCalendarProvider` uses the `calendar.get_events` service with response data for reads and `calendar.create_event` for creates; updates via HA's calendar event update API where supported (verify); capability flag `CanUpdate` controls tool visibility.

### D5. Notes
`GitNotesSource` uses the `git` CLI inside the container (clone / pull with a deploy key or token from `ISecretStore` via `GIT_ASKPASS`), avoiding a native libgit2 dependency. `NotesIndexer` chunks files by heading, embeds with the configured embedding model, and stores `NoteIndexEntry` rows with FTS5 and `sqlite-vec` tables separate from memory.

### D6. Interfaces introduced
| Interface | Purpose | v1 implementation |
|---|---|---|
| `IPrinterClient` | commands per printer | `MoonrakerPrinterClient` |
| `IPrinterStateCache` | live state | `MoonrakerStateCache` |
| `ISnapshotSource` | snapshot fetch + resize | `MoonrakerSnapshotSource` |
| `IImageResizer` | resize images | `SkiaImageResizer` |
| `IMailProvider` | mailbox operations + capability flags | `ImapMailProvider` |
| `ICredentialSource` | mail credentials | `PasswordCredentialSource`, `OAuth2CredentialSource` (google, microsoft presets) |
| `IMailAccountStore` | accounts | `EfMailAccountStore` |
| `IRssClient` | FreshRSS | `FreshRssClient` |
| `IFullTextExtractor` | readable text | `SmartReaderExtractor` |
| `ICalendarProvider` | calendars | `HomeAssistantCalendarProvider` |
| `INotesSource` | notes read | `GitNotesSource` |
| `INotesIndex` | hybrid note search | `SqliteNotesIndex` |

### D7. Model-facing tools
| Tool | Tier | Flags | Hidden when |
|---|---|---|---|
| `printer.status`, `printer.list_files`, `printer.job_queue`, `printer.history` | safe | — | no printer configured |
| `printer.snapshot`, `printer.inspect(question)` | safe | — | no printer or no webcam; `inspect` also when no vision model exists |
| `printer.pause`, `printer.emergency_stop` | safe | — | no printer |
| `printer.start_print`, `printer.resume`, `printer.cancel`, `printer.set_temperature`, `printer.home`, `printer.move`, `printer.edit_queue` | confirm | — | no printer; `denyConfirm` |
| `printer.run_macro(name)` | confirm (allowlist validated first) | — | no printer; empty allowlist; `denyConfirm` |
| `mail.search`, `mail.read_message`, `mail.read_thread` | safe | `untrustedOutput`, `sensitive` | no mail account |
| `mail.list_folders` | safe | — | no mail account |
| `mail.list_labels` | safe | — | no account with `Labels` |
| `mail.create_draft`, `mail.update_draft` | safe | not escalated by taint (user reviews drafts) | no mail account |
| `rss.list_feeds`, `rss.list_categories` | safe | — | FreshRSS not configured |
| `rss.get_unread`, `rss.get_article`, `rss.search`, `rss.fetch_full_text(articleId)` | safe | `untrustedOutput` | FreshRSS not configured |
| `rss.mark_read(ids)` | safe | — | FreshRSS not configured |
| `calendar.list_calendars` | safe | — | no calendar entity |
| `calendar.list_events` | safe | `untrustedOutput` | no calendar entity |
| `calendar.create_event` | confirm | — | no calendar; `denyConfirm` |
| `calendar.update_event` | confirm | — | provider cannot update; `denyConfirm` |
| `notes.list`, `notes.read`, `notes.search` | safe | `untrustedOutput`, `sensitive` | notes not configured |

All tools are additionally subject to template allowlists and satellite `denyTools`.

## Risks / Trade-offs

- [Google OAuth needs a redirect URI on a registered domain, not a bare LAN IP] → document: use the reverse-proxy hostname (a real domain that only resolves on the LAN is fine); verify current Google rules.
- [Google "testing" mode refresh tokens expire after 7 days] → setup docs: publish the Cloud project to production.
- [Work tenants may block IMAP or consent] → deferred Exchange questions stay open; Graph provider later.
- [Moonraker websocket drops] → reconnect with backoff, full state re-read; tools return `printer.offline` meanwhile.
- [Full-text fetch reaches the internet] → only article links from FreshRSS, size / time limits.

## Migration Plan

EF migration adds `MailAccount` and `NoteIndexEntry` plus notes FTS / vec tables. New config sections are optional; unconfigured integrations hide their tools.

## Open Questions

- Exchange account details (deferred §14).
- HA calendar event update API availability per integration.

## Implementation Notes

### Verification (task 1.2, checked 2026-09-29)

**Moonraker** ([external API docs](https://moonraker.readthedocs.io/en/latest/external_api/introduction/)):
- Websocket at `/websocket`, JSON-RPC 2.0 (`{"jsonrpc":"2.0","method","params","id"}`, errors `{"error":{"code","message"}}`).
  HTTP responses are wrapped as `{"result": ...}`; HTTP auth is the `X-Api-Key` header.
- `server.connection.identify` takes `client_name`, `version`, `type` (`web|mobile|desktop|display|bot|agent|other`),
  `url` and optional `api_key` / `access_token`; it authenticates the websocket and may be called once per
  connection. Marshal identifies as type `agent` with the API key. The `X-Api-Key` header on the upgrade request is
  sent as well; the oneshot token (`/access/oneshot_token`) is only a fallback for proxies that strip headers.
- `printer.objects.subscribe` / `printer.objects.query` take `objects: {name: null | [attrs]}` and return
  `{eventtime, status}`. `notify_status_update` has `params: [statusDiff, eventtime]`.
  `notify_klippy_ready`, `notify_klippy_shutdown`, `notify_klippy_disconnected` have no params.
  `server.info` reports `klippy_state` (`disconnected|startup|ready|error|shutdown`).
- Commands: `POST /printer/print/start?filename=`, `/printer/print/pause`, `/printer/print/resume`,
  `/printer/print/cancel`, `/printer/emergency_stop`, all answering `"ok"`. Heating, homing, moving and macros go through
  `POST /printer/gcode/script` with a script **built by Marshal** from typed, validated arguments
  (`SET_HEATER_TEMPERATURE HEATER=… TARGET=…`, `G28 X Y`, `G91`/`G1`/`G90`, `<ALLOWLISTED_MACRO>`); the model never
  supplies G-code text. Restart and firmware restart endpoints exist but have no code path in Marshal.
- `GET /server/files/list?root=gcodes` (items `path`, `modified`, `size`, `permissions`); job queue
  `GET /server/job_queue/status`, `POST /server/job_queue/job` (`filenames`, `reset`), `DELETE /server/job_queue/job`
  (`job_ids` / `all`), `POST /server/job_queue/{pause,start,jump}`; history `GET /server/history/list`
  (`limit`, `start`, `order`, `since`, `before`; jobs with `job_id`, `filename`, `status`, `start_time`, `end_time`,
  `print_duration`, `total_duration`, `filament_used`, `exists`).
- `GET /server/webcams/list` returns `webcams[]` with `name`, `enabled`, `service`, `snapshot_url`, `stream_url`,
  `flip_horizontal`, `flip_vertical`, `rotation`, `uid`; URLs may be relative to Moonraker's host and are resolved
  against the printer URL.

**Google OAuth** ([web server flow](https://developers.google.com/identity/protocols/oauth2/web-server)):
authorization `https://accounts.google.com/o/oauth2/v2/auth` with `access_type=offline` and `prompt=consent` (to
always receive a refresh token), token endpoint `https://oauth2.googleapis.com/token`, IMAP scope
`https://mail.google.com/`. Redirect URIs must be HTTPS (localhost excepted), must not be raw IP addresses and the host
must end in a public-suffix TLD. So the redirect must use the reverse proxy's real domain name (a domain that only
resolves on the LAN works); a bare LAN IP does not. The 7-day refresh-token lifetime of apps in "Testing" status
is not on this page; the setup docs still tell users to publish the Cloud project to production.

**Microsoft OAuth** ([redirect URI rules](https://learn.microsoft.com/en-us/entra/identity-platform/reply-url),
[IMAP OAuth](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth)):
redirect URIs must be HTTPS except `localhost` / `127.0.0.1`, are case-sensitive, max 256 characters, and must not
carry query parameters when personal accounts are allowed. Hence the callback is the fixed path
`/oauth/callback/microsoft` and state travels in the `state` parameter. Delegated IMAP scope
`https://outlook.office.com/IMAP.AccessAsUser.All` plus `offline_access`; works for Microsoft 365 and Outlook.com
accounts through the `common` endpoints `https://login.microsoftonline.com/common/oauth2/v2.0/{authorize,token}`.
SASL XOAUTH2 (`user=…^Aauth=Bearer …^A^A`) is MailKit's `SaslMechanismOAuth2`.

**FreshRSS** ([Google Reader API](https://freshrss.github.io/FreshRSS/en/developers/06_GoogleReader_API.html),
`p/api/greader.php`): `POST /api/greader.php/accounts/ClientLogin` (`Email`, `Passwd`) answers `Auth=<token>`; requests
send `Authorization: GoogleLogin auth=<token>`; edits need `T=<token>` from `GET /reader/api/0/token`.
`subscription/list`, `tag/list`, `stream/contents/<streamId>` (`n`, `c` continuation, `xt` exclude target, `ot` older
than, `r=o` ascending), `stream/items/contents` (`i=` ids, decimal or `tag:google.com,2005:reader/item/<hex>`),
`edit-tag` (`a=user/-/state/com.google/read`, `i=`). **The API has no search route**, so `rss.search(query)` filters
the most recent items (`freshrss.searchWindow`, default 500, read and unread) client-side on title and summary. This
satisfies the RSS spec (the tool exists and uses the API) without an extra channel.

**Home Assistant calendar** ([REST API](https://developers.home-assistant.io/docs/api/rest/),
[get_events](https://www.home-assistant.io/actions/calendar.get_events/),
[create_event](https://www.home-assistant.io/actions/calendar.create_event/), `components/calendar`):
`GET /api/calendars` lists `{entity_id, name}`; `GET /api/calendars/<entity>?start=&end=` returns events with
`summary`, `start`/`end` (`{dateTime}` or `{date}`), `description`, `location` and `uid` / `recurrence_id`. The
`calendar.get_events` action omits `uid`, so reads use the REST endpoint (the uid is needed for updates).
Creation uses `calendar.create_event` (`summary`, `description`, `start_date_time`, `end_date_time`, `start_date`,
`end_date`, `location`). Updates use the websocket command `calendar/event/update` (`entity_id`, `uid`, optional
`recurrence_id` / `recurrence_range`, `event`); there is no update action. Capability detection: the entity's
`supported_features` bit `UPDATE_EVENT = 4` (`CREATE_EVENT = 1`, `DELETE_EVENT = 2` is never used).

### Moonraker fixtures (task 1.3)

There is no printer in the development environment, so the websocket fixtures in
`tests/Marshal.Integrations.Moonraker.Tests/Fixtures/` are transcribed from the documented message formats above
(identify result, subscribe result with the initial state, `notify_status_update` diffs for print start, pause,
complete and error, `notify_klippy_shutdown`, `notify_klippy_ready`) rather than captured from a live Moonraker.
They should be replaced with real captures when a printer is available.

### Printer (tasks 2.1–2.4)

- `MoonrakerStateCache` subscribes only when `server.info` reports `klippy_state: ready`; otherwise it publishes the
  Klippy state alone and waits for `notify_klippy_ready`. `notify_klippy_shutdown` / `notify_klippy_disconnected` set
  the Klippy state in the cached status. ETA is `print_duration / progress − print_duration` while printing (progress
  from `display_status`, falling back to `virtual_sdcard`).
- Commands are a closed set of typed records (`PrinterCommand`); `IPrinterClient.SendAsync` validates them and builds
  the only G-code Marshal ever sends. Limits: hotend target 0–350 °C, bed 0–150 °C (Klipper's `max_temp` still
  applies), moves non-zero and at most 300 mm, axes X/Y/Z only (no extrusion), file paths relative without `..`,
  macro names `[A-Za-z0-9_]+` and in the printer's allowlist (case-insensitive, sent upper case, no arguments).
- "Fails validation before any confirmation is asked" needed a hook the guard did not have: `ITool.Validate(arguments)`
  (default: success) runs in `ToolCallGuard` after the visibility check and before risk rules; a failure denies the
  call with the tool's error. Printer tools resolve the printer and parse their arguments into a `PrinterCommand`
  there, so an unknown macro, a bad heater or an ambiguous printer is rejected without a pending action.
- Snapshots (tasks 2.5–2.6): the first enabled webcam wins. The webcam image is fetched without the Moonraker API key
  (webcams are usually served by the web server next to Moonraker, possibly on another host) and capped at 20 MB.
  Stored files are `<paths.data>/snapshots/<printer>/<yyyyMMddTHHmmssfff>-<random>.jpg`; ids are checked against that
  pattern before any file access. Webcam rotation and flip settings are not applied yet. The same-origin link comes
  from `ISnapshotLinks`, implemented by the host (it knows the path base and API version).
- `printer.snapshot` and `printer.inspect` (tasks 2.7–2.8) live in `PrinterCameraTools`. Webcam presence for their
  visibility is cached per printer for five minutes; an unreachable printer keeps them visible (the call then reports
  the error). `inspect` routes `ModelRequest(runTier, Vision: true)` where the run tier is the template's tier, else
  `fast` for voice and `standard` otherwise, then falls back to `standard` + vision; the call is recorded as a model
  step of the run. `printer.snapshot` answers with a Markdown image of the same-origin link, which the web chat renders.
- Printer page API (tasks 2.9–2.10) under `/api/v1/printers` (signed-in users only, not API tokens). A confirm-tier
  control returns `confirmation_required` with a single-use token (32 random bytes, two minutes, bound to user and
  printer) that freezes the validated command in memory; the dialog posts it to `controls/confirm`. A restart drops
  open confirmations, which only means asking again. Executed controls are audited as `printer.control` with the
  command description, whether it was confirmed and the outcome. The `printer` live topic carries
  `printer.state` events (`{printer, state}`, state null when offline) to every user, since printers belong to the
  instance. The webcam stream and snapshots are served from Marshal's origin; the webcam HTTP client has no timeout
  and no retries, the command client retries only safe methods.
- Printer page (task 2.11): `/printers/:name?` with live status, a temperature chart built from `printer.state`
  events since the page opened, the proxied webcam stream plus on-demand snapshots, controls, files, queue and
  history. Confirm-tier controls open an in-page dialog; only its Confirm button posts the token. An optional
  `printers[].webUrl` (Mainsail / Fluidd) is linked for the actions Marshal does not offer. The dashboard gains a
  printer widget.

### Mail (tasks 3.1–3.4)

- Credentials: `PasswordCredentialSource` reads the account's owner secret; `OAuth2CredentialSource` (one per preset)
  refreshes access tokens with the refresh token in the owner secret, keeps them until five minutes before expiry
  and writes a rotated refresh token back (Microsoft rotates them). `invalid_grant` becomes
  `mail.reauthorize_required`. The connect flow uses PKCE (S256) in addition to the client secret.
- `ImapMailProvider` opens one connection per call and folders read-only for reading (reading never sets `\Seen`).
  `since` / `before` compare the Date header (`SENTSINCE` / `SENTBEFORE`): IMAP's internal date is the time a
  message reached the mailbox, which differs for imported mail. Search fetches at most 500 of the newest UIDs and
  sorts by date. Threads are rebuilt from Message-ID, In-Reply-To and References across the message's folder, the
  inbox and the `\Sent` folder. `update_draft` appends the new version and then expunges only the superseded draft,
  after checking it is in the drafts folder and carries `\Draft`; there is still no tool or provider method that
  deletes or moves mail.
- Container tests: Dovecot 2.4 with a test config (German `Entwürfe` marked `\Drafts`, `Sent` marked `\Sent`) and
  GreenMail 2.1 (no SPECIAL-USE) for the configured-drafts-folder fallback and the missing-folder error. Neither
  offers XOAUTH2, so a tiny in-process IMAP responder checks the `AUTHENTICATE XOAUTH2` exchange.
- Mail tools (tasks 3.5–3.6): tool-facing message ids are `alias:providerId`, so reads and replies always go to the
  account the message came from; a reply drafted in another account is rejected. `mail.search` without `account`
  queries every account, merges by date and reports accounts that failed next to the results. The references
  recorded for sensitive tools are the comma-separated tool-facing ids. JSON output keeps non-ASCII characters.
- Accounts (tasks 3.7–3.8): `/api/v1/mail/accounts` lists, adds (IMAP with password) and removes accounts; the
  password or refresh token is stored as the owner secret `mail_<alias>_password` / `mail_<alias>_refresh` (`-` in
  the alias becomes `_`) after the account row exists, so a taken alias never overwrites another account's secret.
  Removing an account deletes its secret. The connect flow is `POST /api/v1/mail/oauth/{provider}/start`, which
  returns the provider's authorization URL, and the page route `GET /oauth/callback/{provider}`, which redirects back
  to `/mail/accounts?connected=<alias>` or `?error=<code>`. The `state` is random, single use, valid for ten minutes
  and bound to the signed-in user and the provider; it maps to the PKCE verifier on the server. The redirect URI is
  built from the request's scheme, host and path base, so behind a reverse proxy `http.trustForwardedHeaders` must
  be on and the proxy's public URL registered with the provider. OAuth accounts use `imap.gmail.com` or
  `outlook.office365.com` on port 993 with the address as login name (both from the providers' IMAP OAuth
  documentation). Pending flows live in memory: a restart between start and callback means connecting again.

### RSS (tasks 4.1–4.4)

- `FreshRssClient` logs in with `ClientLogin` (user name and API password), keeps the auth token and renews it once
  when a request comes back 401/403; edit tokens (`T`) are fetched per `edit-tag`. Tool-facing article ids are the
  decimal form of the Google Reader item id (`tag:google.com,2005:reader/item/<hex>`), which FreshRSS also accepts.
  Unread lists use `xt=user/-/state/com.google/read`, `ot` for `since` (crawl time, seconds) and follow
  `continuation` in pages of `freshrss.pageSize` until the limit. Single articles come from
  `POST stream/items/contents`; an id of another user returns no item and becomes `rss.article_not_found`.
  Summaries are converted to plain text (AngleSharp, scripts and styles dropped). FreshRSS always has the
  `Uncategorized` folder, so it is listed as a category.
- Container tests run the official `freshrss/freshrss:1.27.1` image, installed through `FRESHRSS_INSTALL`, with one
  user per test created by `cli/create-user.php --no-default-feeds`. The container serves the two test feeds itself
  (`http://localhost/feeds/*.xml` from `p/`), and subscribing through the API fetches them at once, so no test
  reaches the internet.
- Tools (tasks 4.3–4.4): `rss.fetch_full_text` declares `additionalProperties: false` and validates, before the
  guard's tiers, that its only argument is an article id (digits). The link comes from `GET` of that article in
  FreshRSS; the extractor then accepts only http(s), only HTML, at most `freshrss.fullText.maxBytes` (2 MB) within
  `timeout` (15 s), follows at most 5 redirects and cuts the text at `maxCharacters` (20 000). The link itself is
  still chosen by a feed author, so it can point into the local network; the tool only reads, its output is
  untrusted, and no credentials are sent. `mark_read` and the list tools are not untrusted.
- Digest (task 4.5): the skill and template live in `deploy/config.example/` (the repository has no
  `deploy/config/`; the example directory is what gets copied to `/config`). "Since the last digest" needs no
  state: the digest marks every article it processed as read, so the unread list is exactly what is new.
