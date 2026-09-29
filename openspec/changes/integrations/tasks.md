## 1. Setup and verification

- [x] 1.1 Create projects `Marshal.Integrations.{Moonraker,Mail,FreshRss,Notes}` and test projects; update `packages.puml`, `CLAUDE.md` repository map and package index
- [x] 1.2 Verify current Moonraker websocket / webcam APIs, Google and Microsoft OAuth redirect rules for LAN hostnames, FreshRSS Google Reader API, HA calendar services; record in `design.md`
- [x] 1.3 Record Moonraker websocket traffic (idle, print start, pause, complete, error, klippy shutdown) as fixtures

## 2. Printer

- [x] 2.1 Write state cache tests with a fake websocket server replaying fixtures: identify, subscribe, diff merge, resubscribe on `notify_klippy_ready`, reconnect with full re-read, offline result
- [x] 2.2 Implement `MoonrakerSocket` and `MoonrakerStateCache`
- [x] 2.3 Write command tests against a fake REST server: API key header, each safe and confirm command, macro allowlist validation
- [x] 2.4 Implement `MoonrakerPrinterClient` and printer tools with tiers and visibility conditions
- [x] 2.5 Write snapshot tests: webcam discovery, resize, storage and retention, same-origin URL
- [x] 2.6 Implement `MoonrakerSnapshotSource` and `SkiaImageResizer`
- [x] 2.7 Write `printer.inspect` tests with a fake vision client: vision model selection by tier, fallback, hidden without vision model
- [x] 2.8 Implement `printer.inspect`
- [ ] 2.9 Write API tests for printer UI endpoints: status, controls with tiers, confirm-tier controls require a confirmation token from the dialog, audit events, webcam proxy auth
- [ ] 2.10 Implement printer endpoints and the `printer` live-event topic
- [ ] 2.11 Write component tests and implement the printer page and dashboard printer widget

## 3. Mail

- [ ] 3.1 Write credential tests: password, OAuth2 token refresh with google / microsoft presets, refresh token as owner secret
- [ ] 3.2 Implement `PasswordCredentialSource` and `OAuth2CredentialSource`
- [ ] 3.3 Write IMAP provider tests against a GreenMail / Dovecot container: search translation, read message, thread rebuild, folders, drafts via `APPEND` to `\Drafts` with `\Draft` flag, missing drafts folder error
- [ ] 3.4 Implement `ImapMailProvider` with capability flags
- [ ] 3.5 Write mail tool tests: all-accounts search, alias resolution, taint source `mail`, redaction to message references, drafts not escalated in tainted runs, no send / delete / move tools exist
- [ ] 3.6 Implement mail tools
- [ ] 3.7 Write account API tests: create IMAP account, OAuth connect flow with state check, ownership
- [ ] 3.8 Implement accounts endpoints, OAuth callback and the accounts view
- [ ] 3.9 Add an architecture test asserting no SMTP types are referenced anywhere in the solution

## 4. RSS

- [ ] 4.1 Write FreshRSS client tests against the official container: login, feeds, categories, unread with continuation, search, mark read
- [ ] 4.2 Implement `FreshRssClient`
- [ ] 4.3 Write tool tests: taint on article content, `fetch_full_text` accepts only article ids and resolves the link via the API, size / time limits
- [ ] 4.4 Implement RSS tools and `SmartReaderExtractor`
- [ ] 4.5 Add `deploy/config/skills/rss-digest/SKILL.md` and `deploy/config/templates/rss-scorer.md`; test that both load and pass the secret scanner

## 5. Calendar

- [ ] 5.1 Write provider tests against a fake HA server: list calendars, get events in the owner's time zone, create, update capability detection
- [ ] 5.2 Implement `HomeAssistantCalendarProvider`
- [ ] 5.3 Write tool tests: tiers, update hidden without capability, no delete tool, list_events taints with source `calendar`
- [ ] 5.4 Implement calendar tools

## 6. Notes

- [ ] 6.1 Write source tests with a local test repository: list, read, path confinement, clone and pull of a remote (file-based remote in tests)
- [ ] 6.2 Implement `GitNotesSource`
- [ ] 6.3 Write index tests: heading chunking, per-file rebuild on change, hybrid search with fake embeddings, ownership
- [ ] 6.4 Implement `SqliteNotesIndex`, `NoteIndexEntry` migration; align `erd.puml`
- [ ] 6.5 Write tool tests: taint source `notes`, redaction, no automatic context injection
- [ ] 6.6 Implement notes tools
