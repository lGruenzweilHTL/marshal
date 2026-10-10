# Tasks

## 1. Mesh readers

- [ ] 1.1 Add small STL fixtures (binary cube, ASCII cube, binary file starting with `solid`, wrong size, NaN vertex, empty). Write failing `StlMeshReader` tests for bounding box, triangle count and the unreadable cases. Implement `IMeshReader` for STL; verify with `dotnet test --project tests/Marshal.Artifacts.Tests`
- [ ] 1.2 Add 3MF fixtures: core mesh in millimetres, `unit="inch"`, base material colors, color group, components with transforms, a `p:path` component, a Bambu Studio and a PrusaSlicer export. Write failing `ThreeMfMeshReader` tests for unit conversion, transforms, colors and `p:path`. Implement the reader; verify the tests pass
- [ ] 1.3 Write failing limit tests: more than 5 million triangles (synthetic header), zip with too many entries, too large uncompressed size, DTD in the model XML, component cycle, nesting deeper than 16. Enforce the limits while reading; verify the tests pass

## 2. Renderer and renditions

- [ ] 2.1 Write failing `SoftwareMeshRenderer` tests on small meshes: the pixel at the cube center has the model color, not the background; the nearer of two overlapping triangles wins; orthographic front, right and top views show the expected silhouette extents; a 3MF color appears shaded. Implement the rasterizer (z-buffer, flat shading, edge darkening, 2× supersampling); verify the tests pass
- [ ] 2.2 Write failing `SkiaArtifactRenditions` tests for the `display-surfaces` scenarios: STL describes as 2 PNG pages; header text with size in mm; page 2 has four tiles; unreadable model → `artifacts.no_rendition`. Implement the `model3d` rendition; verify the tests pass
- [ ] 2.3 Write failing `FileRenditionCache` tests: hit by hash and variant, eviction by size cap with oldest access first, owner check before the cache. Implement and wire it for model pages; verify the tests pass
- [ ] 2.4 Update the `artifacts.show` test for `model3d` on a satellite display (page 1 of 2 plays) and make it pass; verify with the Artifacts tests

## 3. Previews

- [ ] 3.1 Write failing `SkiaArtifactPreviews` tests: model preview fits 480 × 270, image preview (GIF first frame), code → not found, unreadable → not found. Implement `IArtifactPreviews`; verify the tests pass
- [ ] 3.2 Write failing API tests for `GET /api/v1/artifacts/{id}/preview`: 200 PNG for the owner, 404 for code, foreign and unknown ids, `ETag` and 304, unauthenticated → 401. Implement the endpoint in OpenAPI v1; verify with `dotnet test --project tests/Marshal.Api.Tests`
- [ ] 3.3 Regenerate the web API client (`npm run gen:api`). Write failing `ArtifactList` tests: thumbnail `img` with lazy loading for `model3d` and `image`, kind icon for code, icon after an `error` event. Implement the thumbnails; verify with `npm test` and `npm run build`

## 4. Inspect tool

- [ ] 4.1 Write failing `artifacts.inspect` tests with a fake router and fake `IChatClient`:
  - hidden without a vision model, `standard` fallback;
  - image content and question sent, page downscaled to a 1568-pixel long side;
  - model page 2 sent for `page: 2`; video → `artifacts.no_rendition` with no model call; page out of range;
  - untrusted output with taint source `artifact`; call recorded and costed.

  Implement the tool in `ArtifactTools`; verify with the Artifacts tests
- [ ] 4.2 Update `deploy/config.example/marshal.yaml` comments (displays can show 3D models; `artifacts.inspect` needs a vision model). Verify `ExampleConfigTests` passes

## 5. Integration checks

- [ ] 5.1 Render the fixtures to PNG files in the scratchpad and check them visually: main view, four-view sheet, preview, a slicer 3MF
- [ ] 5.2 Run the full .NET test suite, `npm test`, `npm run build` and `openspec validate artifact-3d-render --strict`; all pass
