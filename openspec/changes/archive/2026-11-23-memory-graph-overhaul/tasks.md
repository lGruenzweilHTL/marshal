# Tasks

## 1. Dependencies

- [x] 1.1 Diff the `d3-force` and `d3-zoom` 3.0.1 dist files against 3.0.0. Choose the version (3.0.1 if the diff is a clean maintenance release, otherwise 3.0.0) and note the result in this change's design.md. Verify: the note is in design.md.
- [x] 1.2 Add `d3-force` and `d3-zoom` with exact pins, and `@types/d3-force` and `@types/d3-zoom` as dev dependencies, in `web/package.json`. Verify: `npm ci` and `npm run typecheck` pass.
- [x] 1.3 Add `d3-force` and `d3-zoom` to the web package index in CLAUDE.md. Verify: the table lists both with their purpose.

## 2. Neighbourhood endpoint

- [x] 2.1 Write failing `Marshal.Memory.Tests` cases for `IMemoryMaps.GetNeighbourhoodAsync`:
  - neighbours in both directions
  - edges between neighbours
  - a soft-deleted neighbour left out
  - `null` for a missing, deleted or other owner's chunk

  Verify: `dotnet test --project tests/Marshal.Memory.Tests` fails on exactly these cases.
- [x] 2.2 Implement `GetNeighbourhoodAsync` in `MemoryMaps` with owner-filtered queries. Verify: the tests from 2.1 pass.
- [x] 2.3 Write failing `Marshal.Api.Tests` cases for `GET /api/v1/memory/chunks/{id}/graph`: 200 with the `MemoryGraph` shape, and 404 `ProblemDetails` for an unknown or other owner's chunk. Verify: the tests fail.
- [x] 2.4 Map the endpoint on the v1 memory group, with a name and a description. Verify: the tests from 2.3 pass.
- [x] 2.5 Regenerate the OpenAPI document and the client types with `npm run gen:api`. Verify: `web/openapi/v1.json` and `web/src/api/schema.d.ts` contain the new path, and `npm run build` passes.

## 3. Pure graph logic

- [x] 3.1 Write failing Vitest cases for `graphLabels.ts`:
  - fit zoom labels pinned chunks plus at most the top 10 by degree (80-node fixture, 2 pinned: at most 12 labels)
  - the ×1.5 tier adds chunks with importance ≥ 3 or degree ≥ 3
  - ×2.5 labels all chunks
  - the highlight set is always labelled
  - ties are deterministic

  Verify: the tests fail.
- [x] 3.2 Implement `graphLabels.ts`. Verify: the tests from 3.1 pass.
- [x] 3.3 Write failing Vitest cases for `projectHulls.ts`: one path per project, none for chunks without a project, the name position at the top of the bounding box, and no output for an empty project. Verify: the tests fail.
- [x] 3.4 Implement `projectHulls.ts` as a union of padded circles, without `d3-polygon`. Verify: the tests from 3.3 pass.
- [x] 3.5 Write failing Vitest cases for `useGraphLayout.ts`, using stopped simulations ticked headless:
  - deterministic output for the same input
  - linked nodes closer than unlinked ones
  - nodes of one project closer to each other than to other projects' nodes
  - a fixed centre node stays at the origin
  - edges to unknown nodes ignored

  Verify: the tests fail.
- [x] 3.6 Implement `useGraphLayout.ts`: forces, headless warm-up, settle frames, drag reheat, and reduced motion. Then delete `forceLayout.ts` and its test. Verify: the tests from 3.5 pass and nothing imports `forceLayout`.
- [x] 3.7 Write failing Vitest cases for the fit computation in `useGraphZoom.ts`: bounds of the shown nodes plus label room, aspect ratio kept, and scale clamped to [0.25, 4]. Verify: the tests fail.
- [x] 3.8 Implement `useGraphZoom.ts`: the `d3-zoom` binding plus `fitTo` and `zoomBy`. Verify: the tests from 3.7 pass.

## 4. Graph canvas

- [x] 4.1 Write failing component tests for `GraphCanvas.vue`:
  - `data-node` and `data-edge` per item
  - arrow markers only on `dependsOn` and `supersedes`
  - a distinct class on `contradicts`
  - a pinned halo
  - hover and focus set `dim` on non-neighbours and label the neighbours
  - Enter and click open a chunk, but a drag over 4 px does not
  - zoom in, zoom out and fit buttons call the zoom functions

  Verify: the tests fail.
- [x] 4.2 Implement `GraphCanvas.vue`:
  - curved edges and markers
  - node sizing, halo and highlight
  - labels counter-scaled and faded
  - edge-aware label anchoring
  - controls and i18n texts (English and German)

  Verify: the tests from 4.1 pass, and `npm run typecheck` passes.

## 5. Memory map

- [x] 5.1 Write failing tests for `GraphView.vue`:
  - project backdrops with names from `/api/v1/memory/projects`
  - backdrops without names when the projects load fails
  - legend chips toggle types via `?hide=`
  - hidden types' nodes and edges not drawn
  - the last shown type disabled
  - the empty state unchanged

  Verify: the tests fail in `MapViews.test.ts`.
- [x] 5.2 Rebuild `GraphView.vue` on `GraphCanvas`, turn the `MapLegend` chips into toggle buttons with `aria-pressed`, and add project anchors and backdrops. Verify: the tests from 5.1 and the existing map tests pass.
- [x] 5.3 Extend the preview data in `web/src/preview/scenarios/normal.ts` with two projects, directed and `contradicts` edges, pinned chunks, and enough chunks to exercise the label tiers. Add a synthetic 500-chunk graph for a performance check. Verify: `scenarios.test.ts` passes, and the 500-chunk graph settles without visible stutter in `npm run dev:preview`.

## 6. Local graph

- [x] 6.1 Write failing tests for `LocalGraph.vue` and its use in `ChunkEditorView.vue`:
  - a chunk with three links shows four nodes, the centre marked
  - clicking a neighbour opens it
  - no graph for a chunk without links
  - a failed load shows an inline error and leaves the Links list usable

  Verify: the tests fail.
- [x] 6.2 Implement `LocalGraph.vue` on `GraphCanvas`: fixed centre, no backdrops or filters, zoom buttons hidden at phone width. Place it above the Links section. Add preview data for `/api/v1/memory/chunks/{id}/graph`. Verify: the tests from 6.1 pass.

## 7. Docs

- [x] 7.1 Update the Map row in `docs/guides/memory.md` and `docs/guides/memory.de.md` to describe zoom, fit, hover highlight, type filters and project backdrops, and mention the local graph in the chunk editor. Verify: `npx clay-doc@5 check` reports no problems.
- [x] 7.2 Refresh `docs/images/memory.png` if it shows the map. Verify: the image shows the new graph.

## 8. Integration checks

- [x] 8.1 Run the screenshot sweep (`npm run screenshots`). Check that the map and the editor render correctly in light and dark at desktop width, and that the editor's local graph fits at phone width without horizontal scroll. Verify: the screenshots show this.
- [x] 8.2 Check zoom, pan, drag, hover and fit by hand in a browser via preview mode, including `prefers-reduced-motion` (no animation). Verify: each behaviour matches the `memory-view` delta spec scenarios.
- [x] 8.3 Run the full checks: `dotnet test` for `Marshal.Memory.Tests` and `Marshal.Api.Tests`, plus `npm test` and `npm run build` in `web/`. Verify: all pass.

## Workflow follow-up

- Sync the delta specs into `openspec/specs/memory-view/spec.md` and archive the change once it is implemented and reviewed.
