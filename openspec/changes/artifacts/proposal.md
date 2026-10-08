# Proposal

## Why

Marshal produces and receives things worth looking at (3D models and code from the coding executor, printer snapshots, mail attachments, files the user uploads), but there is no place to keep them, no way for the model to point at them, and no viewer. The roadmap's display features (showing a model on the TV nearest the satellite, CI logs as code) all need one owner-scoped artifact store with stable ids and links first. This change builds that store and the web viewer; it is useful on its own in the web UI.

## What Changes

- **Artifact store:** files under `/data/artifacts/<owner>/`, one database row per artifact with owner, name, kind, media type, size, SHA-256, source (`upload`, `executor`, `printer`, `mail`), source reference, a `tainted` flag and creation time. Kinds: `image`, `svg`, `video`, `model3d` (STL, 3MF), `code`, `diff`, `pdf`, `markdown`, `html`, `other`. Kind is detected from extension and content, never from a declared media type alone.
- **Quota:** per owner, default 5 GB (`artifacts.quotaMb`); a write that would exceed it fails. Artifacts are deleted only by the user (web UI / API).
- **Ingest paths:**
  - Web upload (`POST /api/v1/artifacts`, size limit `artifacts.maxUploadMb`, default 200).
  - Executor output: the executor host uploads the files a session wrote under `.marshal/artifacts/` in its worktree through the session's callback token. Marked tainted (executor output can carry third-party text).
  - Printer snapshots: a "Keep" action on the printer page copies a snapshot into the store.
  - Mail attachments: new tool `mail.save_attachment(message, attachment)`, confirm tier, `persists`. Marked tainted.
- **Links:** content URLs on Marshal's origin (cookie-authenticated) and signed links that work without signing in and expire after 15 minutes (`artifacts.signedLinkMinutes`), for display surfaces and push images.
- **Serving rules:** `X-Content-Type-Options: nosniff` on every response; `html` and `svg` content served with a CSP `sandbox` policy (no scripts, opaque origin); `other` served as a download.
- **Model tools:** `artifacts.list(kind?, source?, query?)` (safe, output untrusted because names can come from mail or executor output) and `artifacts.show(id)` (safe, web turns only: opens the artifact in the chat). `show` is hidden in voice and background turns until display surfaces exist.
- **Web viewer:** an Artifacts destination with list (filters, upload, delete, quota usage) and a viewer per kind: images and video (native), SVG (as image), code with syntax highlighting, diffs (existing diff view), 3D (three.js, orbit controls, STL and 3MF), PDF (browser viewer in a new tab), Markdown (the chat renderer), HTML mockups (sandboxed iframe, no scripts).
- **Chat:** an `artifact` event on the turn stream; the chat shows image artifacts inline and other kinds as a card that opens the viewer.
- **Live updates:** a new `artifacts` topic on the live-update stream (created, deleted).

## Capabilities

### New Capabilities

- `artifacts`: store, kinds, quota, ingest paths, links, serving rules, model tools.
- `artifact-viewer`: the web UI destination, list and per-kind viewers.

### Modified Capabilities

- `conversation-api`: the turn stream gains an `artifact` event.
- `web-chat`: artifact images inline and artifact cards.
- `executors`: sessions upload artifacts with their callback token.
- `mail`: new `mail.save_attachment` tool.
- `printer`: the printer page can keep a snapshot as an artifact.

## Non-goals

- STEP and other CAD formats (deferred to the 3D renderer change).
- Media renditions (turntable video, code as PNG pages) and showing artifacts on TVs; `display-surfaces` builds on this store.
- A model tool to read artifact contents, delete artifacts or create artifacts from text.
- Editing artifacts in the browser (design canvas editing).
- Snapshots kept automatically; the babysitter's snapshot store and retention stay as they are.
- Artifacts shared between owners.
- Scripts in HTML mockups.

## Impact

- `src/Marshal.Abstractions`: `IArtifactStore`, `IArtifactLinks`, artifact records.
- `src/Marshal.Data`: `Artifact` entity, owner-scoped query filter, migration; `docs/diagrams/erd.puml` / `.svg`.
- New project `src/Marshal.Artifacts` (store, kind detection, quota, tools) with `tests/Marshal.Artifacts.Tests`; `docs/diagrams/packages.puml` / `.svg`; CLAUDE.md repository map.
- `src/Marshal.Api`: `Artifacts` endpoints (list, upload, content, signed content, delete, keep snapshot), turn-stream `artifact` event, live topic, executor callback upload.
- `src/Marshal.ExecutorHost`: upload of `.marshal/artifacts/` at session end.
- `src/Marshal.Integrations.Mail`: `mail.save_attachment`; IMAP provider fetches one attachment part.
- `web/`: Artifacts destination and viewers, chat cards; new packages `three` and `highlight.js` (CLAUDE.md package index).
- `deploy/config.example/marshal.yaml`: `artifacts` section.
