## Why

The core, extensions and memory make Marshal a capable conversationalist, but the everyday value is in the systems it connects: the 3D printer, mail accounts, the RSS reader, the calendar and notes. This change (sub-project 6 of 7) adds these integrations as tools and read paths, each behind a small interface, with the risk tiers and injection defenses from the design.

## What Changes

- **Printer (Moonraker):** config list of printers, REST commands with API key, websocket client with a state cache, webcam snapshot discovery, VLM inspection, tools with fixed tiers (safe / confirm; forbidden actions do not exist), macro allowlist, printer web page with UI controls and proxied webcam.
- **Mail:** `IMailProvider` + `ICredentialSource`; IMAP provider (MailKit) with password, OAuth2 Google and OAuth2 Microsoft credentials so all three account types work; capability flags; search / read / thread / folders / drafts tools; no send path anywhere; accounts view with OAuth connect flows.
- **RSS (FreshRSS):** Google Reader API client, read / search / mark-read tools, full text by article id, shipped `rss-digest` skill and `rss-scorer` template.
- **Calendar:** `ICalendarProvider` with a Home Assistant implementation (`calendar.*` entities); list = safe, create / update = confirm, no delete.
- **Notes:** `INotesSource` over a git repository of Markdown files (local or cloned remote), own hybrid index (`NoteIndexEntry`), read / list / search tools. Notes stay separate from memory.
- All content tools from mail, RSS and notes are `untrustedOutput` (taint) and `sensitive` (redacted in run records).

## Non-goals

- No mail sending, SMTP configuration, delete, trash, flag or label changes.
- No Microsoft Graph or Gmail API providers (build order steps 2 and 3; later changes behind the same interface).
- No new-mail events, Moonraker semantic events or print babysitter (need the EventBus and notifications; `background-work`).
- No notes proposals / write tools (open design topic; separate change once decided).
- No native calendar providers (CalDAV, Google, Graph) and no attendees or free / busy.
- No RSS subscribe / unsubscribe / delete.

## Capabilities

### New Capabilities
- `printer`: Moonraker configuration, commands, state cache, snapshots, inspection, tools and tiers, printer page and UI controls.
- `mail`: providers, credential strategies, capability flags, accounts, OAuth connect, tools, drafts, no-send enforcement.
- `rss`: FreshRSS client, tools, full text, digest skill and scorer template.
- `calendar`: calendar provider interface, HA implementation, tools and tiers.
- `notes`: notes source, index, read tools, separation from memory.

### Modified Capabilities
- None.

## Impact

- New projects: `Marshal.Integrations.Moonraker`, `Marshal.Integrations.Mail`, `Marshal.Integrations.FreshRss`, `Marshal.Integrations.Notes` (calendar lives in `Marshal.Integrations.HomeAssistant`), with test projects.
- New entities: `MailAccount`, `NoteIndexEntry` (in `erd.puml`).
- New dependencies: `MailKit`, `SmartReader`, `SkiaSharp`, `Microsoft.Extensions.Http.Resilience`, a git library or `git` CLI for notes (decided in design).
- Web UI: printer page, accounts view, dashboard printer widget.
- Test containers: GreenMail or Dovecot (IMAP), FreshRSS.
- Depends on `core-agent`, `auth-web-shell`, `extensibility`.
