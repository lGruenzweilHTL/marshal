# Proposal

## Why

"Marshal, show me the model you made" works in the web UI, but fails on the TV: `model3d` artifacts have no media rendition, so `artifacts.show` answers `artifacts.no_rendition`. The model also cannot look at a model, an image or a code page it produced. `printer.inspect` already sends pictures to a vision model, but only webcam snapshots. Item 5b of `docs/feature-roadmap.md`. The explore session chose rendered stills in C# over a rendering sidecar, Blender or headless Chromium: no new dependency and no extra container.

## What Changes

- New media rendition for `model3d` (STL, ASCII and binary, and 3MF), drawn by a software renderer in Marshal:
  - page 1: one shaded view from the front-right above, with the model's size in millimetres in the header;
  - page 2: a sheet of four views (front, right, top, perspective).
  - Both are 1920×1080 PNG on the dark page background used for code, colored with the 3MF material colors when present.
- `artifacts.show` plays these pages on a satellite's display, like code pages.
- New preview image per artifact at `GET /api/v1/artifacts/{id}/preview`: a small PNG of the model view for `model3d` and a downscaled copy for images.
- The web artifact list shows the preview as a thumbnail. The three.js viewer stays the interactive view.
- New tool `artifacts.inspect(id, question, page?)` (safe): sends a rendition page of an image, 3D model or text artifact with the question to a vision model and returns the answer. Uses the same tier routing as `printer.inspect`. Hidden when no vision model is configured.
- Rendered model pages are cached by file hash, because a model can take longer to draw than a text page.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `display-surfaces`: media renditions gain `model3d`.
- `artifacts`: `artifacts.inspect` joins the artifact tools, the 3D scenario of `artifacts.show` changes, and a new requirement covers artifact previews.
- `artifact-viewer`: the artifact list shows preview thumbnails.

## Non-goals

- Turntable video (MP4). It needs a video encoder in the image; stills cover "show me" and inspection.
- STEP and other CAD formats (they need OpenCascade), OBJ, glTF and textures.
- Making the rendering match the three.js web viewer pixel for pixel.
- Executor self-checks. Executor tasks can render their own CAD output in the sandbox (e.g. `openscad -o out.png`).
- Inspecting PDF, SVG, HTML and video artifacts.

## Impact

- `src/Marshal.Artifacts`: STL and 3MF readers, mesh renderer, rendition cache, `artifacts.inspect` in the artifact tools.
- `src/Marshal.Api/Artifacts`: preview endpoint (OpenAPI v1, additive).
- `web/src/artifacts`: thumbnails in the list (regenerated API client).
- No new packages: SkiaSharp and `System.IO.Compression` are already available.
