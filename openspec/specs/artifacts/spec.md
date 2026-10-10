# artifacts Specification

## Purpose
An owner-scoped store for files Marshal produces or receives (models, code, images, documents), with stable ids, links and model tools to point at them.

## Requirements

### Requirement: Artifact store
Each artifact SHALL be stored as a file under the data directory (`/data/artifacts/<owner>/`) with one database row holding owner, id, name, kind, media type, size, SHA-256, source (`upload`, `executor`, `printer`, `mail`, `github`), source reference, `tainted` flag and creation time. All access SHALL go through owner-scoped queries; another owner's artifact SHALL behave as not found. Deleting an artifact SHALL remove its row and its file.

#### Scenario: Other owner's artifact
- **WHEN** owner A requests an artifact id that belongs to owner B
- **THEN** the response is 404

#### Scenario: Delete
- **WHEN** the user deletes an artifact
- **THEN** its row and its file are gone and its links return 404

### Requirement: Artifact kinds
The kind SHALL be one of `image` (PNG, JPEG, GIF, WebP), `svg`, `video` (MP4, WebM), `model3d` (STL, 3MF), `code` (known source file extensions), `diff` (`.diff`, `.patch`), `pdf`, `markdown`, `html` or `other`. The kind and media type SHALL be detected from the file extension and the file's leading bytes; a declared media type alone SHALL NOT decide the kind. A file whose content does not match its extension's kind SHALL be stored as `other`.

#### Scenario: Disguised HTML
- **WHEN** a file named `photo.png` contains HTML
- **THEN** it is stored as kind `other`

#### Scenario: STL model
- **WHEN** a file `bracket.stl` with STL content is uploaded
- **THEN** it is stored as kind `model3d` with media type `model/stl`

### Requirement: Quota
Each owner SHALL have a storage quota (default 5 GB, `artifacts.quotaMb`). A write that would exceed it SHALL fail with an error naming the quota and the current usage, and SHALL store nothing. Artifacts SHALL be kept until the user deletes them.

#### Scenario: Over quota
- **WHEN** an owner has used 4.9 GB of 5 GB and uploads a 200 MB file
- **THEN** the upload fails with a quota error and nothing is stored

### Requirement: Artifact ingest
Artifacts SHALL be created only through these paths: web upload (`POST /api/v1/artifacts`, multipart, at most `artifacts.maxUploadMb` per file, default 200); executor uploads with the session's callback token; keeping a printer snapshot; `mail.save_attachment`; diffs and logs stored by the `repos.pr` and `repos.run_log` tools. Artifacts from executors, mail and GitHub SHALL be marked tainted; uploads and printer snapshots SHALL NOT.

#### Scenario: Upload
- **WHEN** the user uploads `notes.md`
- **THEN** an artifact of kind `markdown`, source `upload`, not tainted, is created and appears on the live-update topic `artifacts`

#### Scenario: Oversized upload
- **WHEN** the user uploads a 300 MB file with the default limit
- **THEN** the response is 413 and nothing is stored

#### Scenario: GitHub log
- **WHEN** `repos_run_log` stores a job's log
- **THEN** an artifact of kind `code`, source `github`, tainted, is created and appears on the live-update topic `artifacts`

### Requirement: Artifact links
The content of an artifact SHALL be available at a URL on Marshal's origin that requires the owner's session or token. Marshal SHALL also issue signed links that need no sign-in, are bound to one artifact, and expire after `artifacts.signedLinkMinutes` (default 15). An expired, altered or foreign signature SHALL get 404.

#### Scenario: Signed link within lifetime
- **WHEN** a signed link issued 10 minutes ago is requested without a session
- **THEN** the content is returned

#### Scenario: Expired signed link
- **WHEN** a signed link issued 16 minutes ago is requested
- **THEN** the response is 404

### Requirement: Safe serving
Every content response SHALL carry `X-Content-Type-Options: nosniff` and the detected media type. Content of kind `html` and `svg` SHALL be served with `Content-Security-Policy: sandbox` that allows no scripts, forms, popups or same-origin access, and that allows framing only by Marshal's own origin. Content of kind `other` SHALL be served with `Content-Disposition: attachment`.

#### Scenario: Script in an HTML mockup
- **WHEN** an HTML artifact contains a `<script>` and is opened directly or in the viewer
- **THEN** the browser does not run the script

#### Scenario: Unknown file
- **WHEN** an artifact of kind `other` is opened
- **THEN** the browser downloads it instead of rendering it

### Requirement: Artifact tools
The system SHALL provide these artifact tools:
- `artifacts.list(kind?, source?, query?)` (safe): at most 50 newest matches with id, name, kind, source, size and creation time. Its output is untrusted, because names can come from mail or executor output;
- `artifacts.show(id, page?)` (safe);
- `artifacts.inspect(id, question, page?)` (safe).

`artifacts.show` SHALL be visible in web turns and in turns from a satellite whose profile has a display, and hidden in all other turns. In a web turn it SHALL emit an `artifact` event on the turn stream with the artifact's id, name, kind and content URL. In a satellite turn it SHALL play the artifact's media rendition (page `page`, default 1) on the satellite's display and return the display name and the page count; the model SHALL NOT be able to choose another display. It SHALL fail with not found for an unknown or foreign id, with `artifacts.no_rendition` naming the kinds the display can show when the artifact has no rendition, and with `artifacts.invalid_arguments` for a page outside the rendition.

`artifacts.inspect` SHALL send page `page` (default 1) of the artifact's media rendition, as an image, with the question to a model with the `vision` capability of the run's tier, falling back to `standard` + `vision`, and return the model's answer and the page count. Its output SHALL be untrusted. It SHALL be hidden when neither tier has a vision model. It SHALL fail with not found for an unknown or foreign id, with `artifacts.no_rendition` for artifacts whose rendition is not an image (no rendition, or video), and with `artifacts.invalid_arguments` for a page outside the rendition.

#### Scenario: Show in a web turn
- **WHEN** a web chat turn calls `artifacts_show` for a 3D model
- **THEN** the turn stream carries an `artifact` event for it and the chat shows a card that opens the 3D viewer

#### Scenario: Voice turn
- **WHEN** a voice turn's tool list is built for a satellite whose profile has no display
- **THEN** `artifacts_show` is not in it and `artifacts_list` is

#### Scenario: Voice turn with display
- **WHEN** a turn from `assist_satellite.living_room` (profile `display: living_tv`) calls `artifacts_show` for an image
- **THEN** the image plays on `living_tv` and the result names `living_tv` and 1 page

#### Scenario: Code on the TV
- **WHEN** a satellite turn calls `artifacts_show` with `page: 2` for a 130-line code artifact
- **THEN** page 2 of 4 plays on the satellite's display

#### Scenario: 3D model on a TV
- **WHEN** a satellite turn calls `artifacts_show` for a `model3d` artifact
- **THEN** page 1 of 2, the shaded view, plays on the satellite's display

#### Scenario: Listing taints
- **WHEN** a run calls `artifacts_list`
- **THEN** the run is tainted with source `artifact`

#### Scenario: Inspect a model
- **WHEN** a run calls `artifacts_inspect` for a `model3d` artifact with `page: 2` and the question "Are the mounting holes on both sides?"
- **THEN** the four-view sheet and the question are sent to the vision model, the answer is returned, and the run is tainted with source `artifact`

#### Scenario: No vision model
- **WHEN** neither the run's tier nor `standard` has a model with `vision`
- **THEN** `artifacts_inspect` is not in the tool list

#### Scenario: Video cannot be inspected
- **WHEN** `artifacts_inspect` is called for an MP4 artifact
- **THEN** the tool fails with `artifacts.no_rendition` and no model is called

### Requirement: Artifact previews
The system SHALL serve a preview image of an artifact at `GET /api/v1/artifacts/{id}/preview`, with the same sign-in rules as its content. For `model3d` it SHALL be the shaded view of rendition page 1. For `image` it SHALL be the image (the first frame for GIF and WebP). Either way it SHALL be scaled to fit 480 × 270 pixels and served as PNG. For every other kind, and for a model or image that cannot be read, the response SHALL be 404. A preview SHALL carry an `ETag` derived from the artifact's SHA-256, and a request with a matching `If-None-Match` SHALL get 304.

#### Scenario: Model preview
- **WHEN** the owner requests the preview of an STL artifact
- **THEN** the response is a PNG of at most 480 × 270 pixels showing the model

#### Scenario: Code has no preview
- **WHEN** the preview of a `code` artifact is requested
- **THEN** the response is 404

#### Scenario: Foreign artifact
- **WHEN** owner A requests the preview of owner B's model
- **THEN** the response is 404

#### Scenario: Unchanged preview
- **WHEN** the preview is requested again with the `ETag` from the first response
- **THEN** the response is 304 without a body
