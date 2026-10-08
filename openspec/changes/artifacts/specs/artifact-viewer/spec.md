## Purpose

The web UI for artifacts: a list with upload, filters and deletion, and a viewer for each artifact kind.

## ADDED Requirements

### Requirement: Artifact list
The web UI SHALL provide an Artifacts destination listing the owner's artifacts newest first with name, kind, source, size, creation time and a marker for tainted artifacts, filterable by kind and source and searchable by name. It SHALL allow uploading files, deleting an artifact after a confirmation dialog, and SHALL show the quota usage. New and deleted artifacts SHALL appear without reload through the live-update topic `artifacts`.

#### Scenario: New executor artifact appears
- **WHEN** the list is open and an executor uploads an artifact
- **THEN** the artifact appears at the top of the list without reload

#### Scenario: Delete needs confirmation
- **WHEN** the user clicks delete on an artifact
- **THEN** a confirmation dialog appears, and only after confirming is the artifact deleted

### Requirement: Artifact viewers
Opening an artifact SHALL show a viewer for its kind:
- `image`, `video`: the browser's native elements;
- `svg`: rendered as an image (never inline in the page's DOM);
- `code`: syntax-highlighted text with line numbers;
- `diff`: the existing diff view;
- `model3d`: an interactive 3D view with orbit, zoom and reset (STL and 3MF), loaded only when a 3D artifact is opened;
- `pdf`: opened in the browser's PDF viewer in a new tab;
- `markdown`: rendered with the chat's Markdown rules (HTML disabled, same-origin images only);
- `html`: a sandboxed frame without scripts;
- `other`: name, size and a download button.
Every viewer SHALL offer download, and SHALL show the source and the tainted marker.

#### Scenario: 3D model
- **WHEN** the user opens an STL artifact
- **THEN** the model is shown and can be rotated and zoomed

#### Scenario: 3D code loads on demand
- **WHEN** the user opens only image artifacts
- **THEN** the 3D viewer's code is not downloaded

#### Scenario: Markdown with an external image
- **WHEN** a Markdown artifact contains `![x](https://evil.example/x.png)`
- **THEN** no request to `evil.example` is made and the URL is shown as a link
