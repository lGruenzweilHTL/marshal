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
