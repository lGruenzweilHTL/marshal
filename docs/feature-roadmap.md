# Feature roadmap

Planning reference from an explore session on 2026-10-06. Nine features are in scope, built in the order below; four ideas are deferred.

This is not a spec. Each numbered item becomes its own OpenSpec change (`opsx:propose`), and no implementation starts without an approved change. Once a change exists in `openspec/changes/`, its proposal, design and delta specs supersede what this document says about it.

**Before starting:** finish and archive `spa-redesign`.

## Implementation order

Dependencies first, then size: cheap Home Assistant wins, then the artifact and display base that the media features reuse, then the infrastructure integrations that share one webhook ingest.

```
 1 location reminders ─┐
 2 HA todo            ├─ HA quick wins (reuse existing plumbing)
 3 HA energy ─────────┘
 4 artifacts + web viewer ──▶ 5 display surfaces ──▶ 6 Jellyfin + Seerr ──▶ 7 Music Assistant
                                  │                     (both reuse satellite → nearest player)
                                  └─▶ 5b 3D renderer
 8 Uptime Kuma (generic webhook ingest) ──▶ 9 Proxmox (events through same ingest)
 4 ─────────────────────────────────────────▶ 10 GitHub (CI logs / diffs as code artifacts)
```

| # | Proposed change | Feature | Depends on | Size |
|---|---|---|---|---|
| 1 | `location-reminders` | Reminders on zone entry | existing event bus + task triggers | S |
| 2 | `ha-lists-energy` | HA todo lists | HA MCP (verify existing tools) | S |
| 3 | `ha-lists-energy` | HA energy statistics | HA websocket client | S |
| 4 | `artifacts` | Artifact store + web viewer | — | M |
| 5 | `display-surfaces` | Show artifacts on the TV nearest the satellite | 4 | M |
| 5b | `artifact-3d-render` | Headless 3D renderer (turntable + still) | 4, 5 | M–L |
| 6 | `media-requests` | Seerr requests, Sonarr/Radarr read-only, Jellyfin playback | 5 | M |
| 7 | `music-assistant` | Music on the nearest speaker | 5, 6 | S–M |
| 8 | `webhook-ingest` | Uptime Kuma as first generic webhook event source | machine tokens | M |
| 9 | `proxmox` | Proxmox read-only + events | 8 | S–M |
| 10 | `github` | GitHub read-only (fine-grained PAT) | 4 | M |

Items 2 and 3 share the HA client work and can be one change or two.

## 1–3. Home Assistant quick wins

All three reuse plumbing Marshal already has. Nothing needs to change in Home Assistant itself.

### 1. Location reminders

The event-bus spec already matches watches like `source: ha, entity: person.lukas, to: home`, so "remind me when I'm at the hardware store" is a one-off task with an `event(filter)` trigger. HA automations are static, so ad-hoc voice reminders are the gap Marshal fills.

- New: a tool that resolves a spoken place to a zone id.
- New: creating a missing zone via HA websocket `zone/create`. This changes HA config, so it is **confirm** tier.
- Limit: HA zones are single circles, so a chain ("any Hornbach") needs one zone per store.

### 2. HA todo lists

HA's MCP Assist API probably exposes list intents already (`HassListAddItem`, `HassListCompleteItem`, plus a tool to read list items). Verify first. If present, this is risk rules and tests only; otherwise add a small REST-backed tool.

### 3. HA energy

The energy dashboard's data lives in HA long-term statistics. HA MCP `GetLiveContext` returns only current state, so Marshal cannot answer "how much did I use this week" today.

- New: a read tool over the HA websocket: `energy/get_prefs` (which sensors the dashboard uses) and `recorder/statistics_during_period`.
- Optional later: "run it when cheap" needs a dynamic price sensor and/or a solar forecast (`forecast_solar`). Low value with a fixed tariff and no PV.

## 4–5. Artifacts and display surfaces

Goal: "Marshal, show me the model you made" shows it on the screen nearest the asking satellite, or in the web UI for a web turn. This needs no browser or computer use.

### 4. Artifact store + web viewer

- Types: 3D models (STL / 3MF / STEP), images, video, code, design canvas; open to more.
- Stored under `/data`, owner-scoped like every user row, served through short-lived signed token URLs.
- One `IArtifactRenderer` per type, with output `media`, `web`, or both.
- SPA viewer: three.js for 3D, highlighted code and diffs, live canvas.
- Tools take artifact ids, never URLs, so the "no caller-chosen URL" rule holds. Showing is safe tier.
- Useful on its own for the web UI, without any TV.

### 5. Display surfaces

The TVs (Samsung Q6 and other smart TVs) are HA media players, not browsers. The core `samsungtv` integration mostly launches apps and sends keys; `dlna_dmr` is what plays image and video URLs. 3D models, code and canvases are not media, so they need a rendition or a browser.

```
 artifact (stl/3mf/step, image, video, code, canvas, ...)
    │
    ├─▶ media rendition (any TV via DLNA / HA play_media)
    │     3D     → turntable MP4 + still PNG
    │     code   → syntax-highlighted PNG pages
    │     canvas → PNG export
    │     image/video → as is
    │
    └─▶ interactive viewer (web UI, or a browser-capable display)
```

- **Baseline:** every artifact type has a media renderer, so every TV can show it. Not interactive; the remote can pause.
- **Upgrade:** an Android/Google TV stick with Fully Kiosk, or HA cast, gives a real browser display for the interactive viewer.
- **Web UI turn:** the turn knows its client, so the viewer opens there through the SSE event stream.

Config sketch:

```yaml
displays:
  living_tv:  { entity: media_player.living_room_dlna, kind: media }
  office_tab: { entity: media_player.fully_office, kind: web }
satellites:
  assist_satellite.living_room: { display: living_tv }
```

Verify when building: whether the Q6 appears via `dlna_dmr` and which image and video formats it accepts; whether it supports AirPlay 2 as another route.

### 5b. 3D renderer

Headless server-side rendering (a `trimesh`/`pyrender` or Blender CLI sidecar) produces the turntable MP4 and still PNG. Its own change because it carries the most risk. The same render lets a vision model check Marshal's own CAD output.

## 6–7. Media

### 6. Jellyfin + Seerr

Seerr downloads nothing; it is a request layer in front of Sonarr and Radarr.

```
 Seerr ──request──▶ Sonarr / Radarr ──search──▶ Prowlarr (indexers)
 (discover, ask,         │                         │
  approve, status)       └──grab──▶ download client ┘──▶ import, rename ──▶ Jellyfin library
```

- **Only Sonarr/Radarr do:** indexer search, release choice, quality upgrades, watching shows for new episodes, renaming and import, the queue.
- **Seerr adds:** one search for movies and shows (TMDB), quality profile and root folder set once, approvals, request history.
- **Marshal uses:** Seerr for requests; Sonarr/Radarr API read-only for queue, calendar ("next episode") and download progress. Delete and remove calls are forbidden.
- **Playback:** through HA (Jellyfin integration gives `media_player` entities; newer HA has a media search-and-play intent, verify it is exposed over MCP). Reuses the satellite → display mapping from item 5.
- **Risk:** the hard part is title ambiguity ("Dune" is 1984, 2021 or the 2000 series). A request is **confirm** tier and the confirmation names year and type, so disambiguation and confirmation are one step.
- A custom integration beats browser automation here: deterministic, cheap in tokens, usable from voice turns with a fast local model, and no taint.

### 7. Music Assistant

Plain HA only controls what each device integration exposes. Music Assistant adds one library across providers (Spotify, Tidal, YouTube Music, Jellyfin, local files, radio), search, queues, moving playback between rooms, synced groups, and playback to Cast, AirPlay, DLNA, Sonos, Snapcast and Squeezebox.

- Install: the MA server (HA add-on/app, or a container) plus the MA integration in HA.
- Marshal adds a `speaker` entry per satellite next to `display`.
- HA MCP exposes only intents and scripts exposed to Assist, so call `music_assistant.play_media` as a system call through HA REST (preferred, keeps risk rules in Marshal) or wrap it in an exposed HA script.

## 8–10. Infrastructure

### 8. Uptime Kuma (webhook ingest)

Marshal's first generic inbound webhook event source: an endpoint plus a scoped machine token per source. Uptime Kuma's "Webhook" notification type pushes monitor up/down events onto the event bus. The event bus has no generic webhook source today, so this change adds one that later sources reuse.

### 9. Proxmox

- Start read-only with a Proxmox API token that has only the `PVEAuditor` role. Limit access at the source first; Marshal's risk rules are the second layer.
- Later: VM create/start/stop with a separate token at **confirm** tier. Destroy and delete are forbidden.
- Implementation: a community Proxmox MCP server (review its code, it holds an infrastructure token) or a small built-in `IProxmoxClient` over the REST API.
- Events: Proxmox VE 8.1+ can send notifications to a webhook target, so backup and task failures reuse the ingest from item 8 instead of polling `/cluster/tasks`.
- Main value is background work (briefing, failed backups, full storage) and infrastructure knowledge for executor tasks, not voice questions.

### 10. GitHub

| | Fine-grained PAT | GitHub App |
|---|---|---|
| Scope | Chosen repos and permissions | Repos where the app is installed |
| Lifetime | Expires (set by you, up to 1 year) | Installation tokens last 1 h |
| Identity | Actions show as you | Actions show as `marshal[bot]` |
| Events | Polling | Webhooks (need reachability) or polling |

Start with a read-only fine-grained PAT; move to a GitHub App once Marshal writes and needs its own identity.

Access control in layers:

1. Token scope at GitHub: read-only Contents, Pull requests, Actions, Issues on chosen repos.
2. Repo allowlist in config; tools take a repo id, not a free-form owner/name.
3. Risk rules: reads safe; issue and PR comments **confirm** (outward send); merge, delete, settings forbidden.
4. Taint: issue and PR text from other people is untrusted.

The official `github/github-mcp-server` supports `--read-only` and `--toolsets`, so a first version may be config only. Keep the executor's git token separate from Marshal's. CI logs and diffs show as code artifacts (item 4).

## Deferred

| Idea | Why deferred | Prepare now |
|---|---|---|
| Phone channel | Push (critical alerts) already reaches you; calls need a SIP number, real-time voice and caller-ID trust handling | Keep notification channels pluggable so `call` can be added as a last escalation step |
| Speaker ID | Only pays off with multi-user; Marshal never receives audio today | Keep "who is speaking" behind one interface (satellite → owner today) |
| Browser / computer use | Navigation is a caller-chosen URL; every run is tainted; prompt-injection risk next to home control | Most "check own work" needs are covered by rendering in the executor sandbox |
| Self-modification | Needs a git host integration and strict path protection | See notes below |

### Phone channel notes

- Marshal cannot call from your own mobile number (caller-ID spoofing is blocked or illegal); give it its own SIP number saved as a contact so DND can let it through.
- Only system-defined critical sources may use it, like `critical` notifications.
- Inbound callers are untrusted: caller ID can be faked, so no confirm-tier actions without a second factor.
- Calling other people: skip. Outward send in your name, and the EU AI Act requires AI disclosure.
- Check whether HA's VoIP integration can place outbound calls before building SIP code.
- Cheaper alternative for "reach me anywhere": a Signal, Matrix or Telegram text channel.

### Speaker ID notes

- Speaker embeddings (ECAPA-TDNN / x-vector; SpeechBrain, WeSpeaker, pyannote), not spectrogram templates. Enroll 5–10 phrases per person, compare by cosine similarity with a threshold.
- Needs an audio step outside Marshal, e.g. a Wyoming STT proxy doing STT and the embedding together, then a way to pass the label to Marshal.
- Short commands (about 1 s) are weak; adults vs children works, similar adults are unreliable.
- Personalization only, never authorization.

### Browser / computer use notes

- If built: browser-only (not a full VM), as a separate sandboxed service like `ExecutorHost`, with an origin allowlist, a dedicated low-privilege browser profile, background runs only, and a Claude model.
- Rule of thumb: the browser is a fallback for rare tasks, never for something done weekly.

### Self-modification notes

Marshal proposes, something outside Marshal applies. Never writes its own config or code directly.

- **Data** (preferences, memory, tasks): already Marshal-editable through tools.
- **Config:** `config.propose` opens a PR on the config repo; CI runs config validation; you merge on the git host (separate login = real boundary); the host pulls into `/config:ro`; hot reload applies it; `git revert` rolls back.
- **Protected paths:** `risk.yaml`, auth and model settings, secret names, `mcp/` (stdio servers run code in the container), anything loosening `denyTools`/`denyConfirm`, CI workflows. Self-edits may only tighten.
- **Skills:** persistent instructions, so a skill written in a tainted run could store an injection. Hide `config.propose` for skills and templates in tainted runs.
- **Code:** Marshal writes an OpenSpec proposal, you approve it, the executor implements it and opens a PR, CI tests, you merge, CI builds a tagged image, deploy. Marshal never merges, holds the Docker socket or deploys itself.
- **Deployment risk:** EF migrations are forward-only, so back up `/data` before every upgrade; keep PRs small to avoid review fatigue.

## Rules that apply to every item

- Small interfaces for every integration, even with one implementation.
- Limit access at the source first (token scope, roles), then risk rules in Marshal.
- Unmatched MCP tools default to **confirm**; destructive operations are forbidden, not confirm.
- Third-party content (issue text, page text, media metadata) is untrusted and taints the run.
- Tools take ids of known items, never caller-chosen URLs.
- Secrets only by name in `/config`, values from `ISecretStore`.
- Tests use fakes, never real services or a real LLM.

## Open questions

- [ ] Does HA MCP expose todo list intents and a list-read tool on the current HA version?
- [ ] Does the Samsung Q6 appear via `dlna_dmr`, and which formats does it play? AirPlay 2?
- [ ] Is HA's media search-and-play intent exposed over MCP for Jellyfin?
- [ ] Which music source feeds Music Assistant?
- [ ] PV or dynamic tariff available for energy optimization?
- [ ] Where do artifacts come from: executor output only, or uploads and generated images too?
- [ ] Is `/config` a git repo, and where is it hosted (for future self-modification)?
