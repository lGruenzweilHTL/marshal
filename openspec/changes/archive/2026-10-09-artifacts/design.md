# Design

## Context

See proposal.md (Why). Existing pieces this change builds on:

- **Printer snapshots** (`Marshal.Integrations.Moonraker`): stored as `/data/snapshots/<printer>/<id>.jpg`, pruned to `SnapshotOptions.Retention`, served by `PrinterEndpoints` (cookie) and `/shared?signature=` (Data Protection time-limited signature, one day) through `ApiSnapshotLinks : ISnapshotLinks`. The signed-link pattern is reused.
- **Turn stream:** `AgentRunner` writes `TurnEvent`s (`DeltaEvent`, `StatusEvent`) to `request.Events`; `ConversationEndpoints` maps them to SSE items. Tools return `ToolOutput(Content, Reference?)` and cannot write turn events.
- **Executors:** `ExecutorCallbackEndpoints` accepts `POST executor/callbacks/{sessionId}` with the session's `executor_callback` token (own auth scheme, excluded from OpenAPI). `Marshal.ExecutorHost` is never referenced by Marshal.
- **Mail:** `IMailProvider` (IMAP via MailKit) reads messages; `mail.read_message` lists attachments by name only.
- **SPA CSP:** `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'`. Same-origin frames and media are allowed through `default-src`; `object-src 'none'` rules out `<object>` PDFs.
- **Taint:** `ToolDescriptor.UntrustedOutput` is static per tool.

## Goals / Non-Goals

**Goals:**
- One store, one set of links and one serving path for every artifact source, so `display-surfaces` and `github` only add sources and consumers.
- Untrusted files can never run script on Marshal's origin.

**Non-Goals:**
- Deduplication by hash across artifacts (the hash is stored for later use and integrity checks).
- Thumbnails; the list shows kind icons, the image viewer loads the original.

## Decisions

### 1. New project `Marshal.Artifacts`

Store, kind detection, quota and the `artifacts.*` tools live in `src/Marshal.Artifacts` (references `Marshal.Abstractions`, `Marshal.Data`, `Marshal.Core` for tool plumbing as the other feature projects do). The entity and migration live in `Marshal.Data` as for every other entity. Endpoints in `Marshal.Api/Artifacts`. Updates `packages.puml` and the CLAUDE.md repository map.

Alternative: put it into `Marshal.Core` — Core is the agent harness; a file store with its own config and quota is a subsystem of its own.

### 2. Interfaces

- `IArtifactStore` (Abstractions): `CreateAsync(ArtifactDraft, Stream, ct)`, `GetAsync(id)`, `OpenReadAsync(id)`, `ListAsync(ArtifactQuery)`, `DeleteAsync(id)`, `UsageAsync()`. All owner-scoped through the execution context's owner, like every other store. v1: `FileArtifactStore` (files + EF rows).
- `IArtifactLinks` (Abstractions): `Content(id)` and `Signed(id)`. v1: `ApiArtifactLinks` in `Marshal.Api` (same Data Protection pattern as `ApiSnapshotLinks`, purpose `Marshal.ArtifactLinks`, lifetime from config).
- `IArtifactKindDetector` (internal to `Marshal.Artifacts`): extension + magic bytes → kind and media type. v1: `ArtifactKindDetector`.
- `IMailProvider` gains `ReadAttachmentAsync(account, messageId, attachment, ct)` returning name, size and a stream (IMAP: fetch only that body part). Extends an existing interface rather than adding one.

`CreateAsync` writes to a temp file in the owner's folder while hashing and counting bytes, aborts as soon as the count would exceed the quota or the per-file limit, detects the kind from the first bytes, then moves the file to `<id>` and inserts the row in one step (row insert failure deletes the file). Concurrent writes of one owner are serialized by a per-owner lock so two uploads cannot both pass the quota check.

### 3. Kind detection

Extension picks the candidate kind; leading bytes must agree: PNG/JPEG/GIF/WebP/MP4/WebM/PDF signatures, `solid`/binary header size check for STL, ZIP with `3D/3dmodel.model` for 3MF. Text kinds (`code`, `diff`, `markdown`, `html`, `svg`) require valid UTF-8 without NUL bytes; `svg` additionally an `<svg` root. Mismatch → `other`. `code` extension list is a static table (cs, ts, js, py, scad, yaml, json, …) also used to pick the highlighter language.

### 4. Serving

`GET /api/v1/artifacts/{id}/content` (cookie or token) and `GET /api/v1/artifacts/{id}/content/signed?signature=` (anonymous). Both set `nosniff`, the detected media type, `Cache-Control: private`, and range support (video seeking). `html`/`svg`: `Content-Security-Policy: sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'self'`. `other`: `Content-Disposition: attachment`. Everything else `inline` with the artifact name.

The viewer frames HTML with `<iframe sandbox src=…/content>`; the empty `sandbox` attribute and the CSP header both block scripts, so opening the URL directly is safe as well. SVG is shown through `<img>`, where scripts never run.

### 5. Turn stream: tool output carries artifacts

`ToolOutput` gains `IReadOnlyList<ArtifactRef>? Artifacts` (id, name, kind, url). After a tool call, `AgentRunner` writes one `ArtifactEvent` per ref to `request.Events`; `ConversationEndpoints` maps it to the SSE `artifact` item. The refs are stored with the assistant message (message metadata JSON) so reopened sessions render the cards. If the message store has no metadata field, the implementation adds a nullable JSON column (one migration together with the `artifacts` table).

Alternative: give tools a turn-event writer in `ToolInvocation` — widens every tool's surface for one tool's need.

### 6. Tools

| Tool | Tier | Flags | Hidden when |
|---|---|---|---|
| `artifacts.list(kind?, source?, query?)` | safe | `UntrustedOutput`, taint kind `artifact` | never |
| `artifacts.show(id)` | safe | — | not a web turn (`SessionSource != Web`) |
| `mail.save_attachment(message, attachment)` | confirm (`DefaultTier = Confirm`) | `Persists`, `Sensitive` | owner has no mail account |

`list` is always untrusted because `UntrustedOutput` is static and names from mail or executor output are third-party text; a run that only lists artifacts becomes tainted, which only escalates `persists` tools to confirm. `artifacts` joins `ReservedNamespaces`.

### 7. Executor upload

New callback route `POST executor/callbacks/{sessionId}/artifacts` (same auth scheme and checks as the existing callback, excluded from OpenAPI), body = file stream, name in a header, size limited by `artifacts.maxUploadMb`. The host walks `.marshal/artifacts/` (no symlinks, regular files only, path must stay inside the folder), uploads each file, collects skips, then posts completion with the skip list in its notes. Upload refusals (quota, size) come back as 4xx with a reason the host records as a skip.

### 8. Web UI

Route `/artifacts` (list) and `/artifacts/:id` (viewer), destination in the side navigation and the "more" tab. Viewers are per-kind components loaded with dynamic `import()`, so `three` (with `STLLoader`, `3MFLoader`, `OrbitControls` from `three/examples/jsm`) and `highlight.js` (core + registered languages for the code table) land in separate chunks. The `productionBundle` architecture test gains a check that the main chunk does not contain three.js. The diff viewer reuses `inbox/DiffView.vue` (moved to `ui/` if needed by two features). Markdown reuses `chat/markdown.ts`. Upload uses `useSubmit` with progress; live updates use the `artifacts` topic.

New packages: `three` (MIT) and `highlight.js` (BSD-3-Clause), both added to the CLAUDE.md package index.

## Risks / Trade-offs

- [Polyglot files (valid PNG that is also HTML)] → served as `image/png` with `nosniff`, so the browser never treats it as HTML; HTML and SVG only ever render sandboxed.
- [PDF from mail with JavaScript] → opened in the browser's own PDF viewer in a new tab; browser PDF viewers run PDF scripts isolated from the page origin. Accepted.
- [Large 3MF/STL files freeze the phone] → the viewer shows size first and loads models over 50 MB only after a tap.
- [Per-owner lock is in-process] → one Marshal instance per data directory (existing deployment model).
- [`artifacts.list` taints often] → listing is rare in voice; the consequence is confirmation for memory and task writes in that run.
- [Executor writes many files] → each upload counts against the quota; the completion notes list what was skipped.

## Migration Plan

One migration adds `artifacts` (and the message metadata column if needed); `erd.puml`/`.svg` and `packages.puml`/`.svg` updated in the same change. The `/data/artifacts` folder is created on first write. Rollback: restore the `/data` backup; the folder can be deleted.
