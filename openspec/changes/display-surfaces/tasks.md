# Tasks

## 1. Configuration

- [ ] 1.1 Write failing tests for `displays` options validation (name pattern, `media_player.` entity, absolute http(s) base URL, base URL required with surfaces, `notifications.publicUrl` fallback, `linkMinutes` range); add `DisplayOptions` and register its validation in the configuration stage; verify with `dotnet test --project tests/Marshal.Core.Tests`
- [ ] 1.2 Write failing tests for `display` in `satellites.yaml` (loaded into `SatelliteProfile.Display`, unknown display name is an error naming file and key, previous profiles kept on a broken reload); implement in `YamlSatelliteContextProvider`; verify with the satellite tests
- [ ] 1.3 Add `IDisplayDirectory` and `ConfiguredDisplayDirectory` (display by name, display for a satellite) with tests; verify with `dotnet test --project tests/Marshal.Core.Tests`
- [ ] 1.4 Add the `displays` section to `deploy/config.example/marshal.yaml` and `display` to `satellites.yaml` with comments on reachability (HTTP, IP address, port); verify `ExampleConfigTests` passes

## 2. Renditions

- [ ] 2.1 Write failing tests for `IArtifactRenditions.Describe` per kind (JPEG/PNG as is, GIF/WebP → PNG, MP4 as is, WebM/model3d/svg/pdf/html/other → `artifacts.no_rendition`, text kinds → page count of 60-line pages incl. wrapping)
- [ ] 2.2 Embed JetBrains Mono TTF in `Marshal.Artifacts`, add SkiaSharp references, implement `SkiaArtifactRenditions` (image conversion, text pages with header and diff colors, 2 MB text cap); verify tests from 2.1 plus page rendering tests (1920×1080 PNG, page 2 of a 130-line file starts at line 61)
- [ ] 2.3 Update `docs/diagrams/packages.puml` (SkiaSharp from `Marshal.Artifacts`) and re-render the SVG; update the CLAUDE.md package index row for SkiaSharp; verify the SVG renders

## 3. Display links and endpoint

- [ ] 3.1 Write failing API tests for the display endpoint: valid link 200 with media type and `nosniff`, range request 206, `HEAD` 200, expired (FakeTimeProvider) 404, altered page or id 404, browser signed-link signature rejected, deleted artifact 404, no sign-in needed
- [ ] 3.2 Implement `IDisplayLinks` (`ApiDisplayLinks`, protector `Marshal.DisplayLinks`, base64url path signature, base URL from `displays.mediaBaseUrl` or `notifications.publicUrl`) and the anonymous `GET`/`HEAD /api/v1/artifacts/{id}/display/{page}/{signature}.{ext}` endpoint on the versioned group; verify tests from 3.1
- [ ] 3.3 Regenerate `web/openapi/v1.json` and `web/src/api/schema.d.ts` if the endpoint appears in the document; verify `npm run build` in `web/`

## 4. Playing and the tool

- [ ] 4.1 Write failing tests for `HomeAssistantDisplayPlayer` against the fake HA REST server (`media_player.play_media` body with entity, content id and type; HA error → `display.failed` naming the display; HA down → `ha.unavailable`); implement and register it in the HA integration
- [ ] 4.2 Write failing tests for `artifacts.show` visibility (web: visible; satellite with display + HA: visible; satellite without display, background run, no HA: hidden)
- [ ] 4.3 Write failing tests for `artifacts.show` in satellite turns (plays page 1 by default, `page: 2` of 3, page out of range → `artifacts.invalid_arguments`, `model3d` → `artifacts.no_rendition` and no HA call, unknown id → not found, result names display and page count); implement in `ArtifactToolSource` with `IArtifactRenditions`, `IDisplayLinks`, `IDisplayDirectory`, `IDisplayPlayer`; verify web-turn tests still pass
- [ ] 4.4 Write an end-to-end API test: a voice turn from a satellite with a display, a fake chat client calling `artifacts_show`, the fake HA server receiving `play_media`, and the display link from that call returning the rendition; verify with `dotnet test --project tests/Marshal.Api.Tests`

## 5. Integration checks

- [ ] 5.1 Run the full .NET test suite and `openspec validate display-surfaces --strict`; both pass
