# Design

## Context

- `GraphView.vue` fetches `GET /api/v1/memory/graph` (all live chunks and edges between them), runs `forceLayout.ts` once (300 iterations, all pairs: O(n²) per step), stretches x and y independently onto a 1000×600 `viewBox`, and draws `<line>` edges and `<g>` nodes with every label shown.
- Node colours come from the `--mem-<type>` tokens, which already have light and dark values; the canvas uses `--surface`, `--line`, `--shadow-1`. Tests find nodes and edges by `data-node` and `data-edge`.
- `MapView.vue` shows the graph only on desktops (`useDesktop`); phones get a grouped list (spec: *Graph on a phone*).
- Graph nodes carry `projectId` but no project name; `GET /api/v1/memory/projects` returns names.
- The chunk editor already loads the chunk's edges for its Links section (`ChunkEditorView.vue`).
- API rule: only breaking changes need a new API version (STYLE_GUIDE). A new endpoint is additive in v1.

## Goals / Non-Goals

**Goals:**
- One reusable graph drawing used by the map and the chunk editor.
- Stays an SVG drawn by Vue, so tokens, dark mode, focus rings, accessible names and the `data-*` test hooks keep working.
- Deterministic layout for the same data, so Vitest assertions and the screenshot sweep are stable.
- Smooth with a few hundred chunks; usable up to low thousands.

**Non-Goals:**
- Canvas or WebGL rendering, or Web Workers for the layout.
- Layout persistence between visits.

## Decisions

### 1. `d3-force` + `d3-zoom` for physics and gestures, Vue for drawing
- d3 computes positions and the zoom transform only; Vue renders from plain arrays. We never use `d3-selection` to change the DOM, except for what `d3-zoom` needs to attach its listeners to the `<svg>`.
- `d3-force`'s many-body force uses a Barnes–Hut quadtree (O(n log n)), which removes the O(n²) slowdown.
- `d3-force` v3 seeds its own LCG (`randomSource`, verified in the 3.0.1 dist) and places nodes on a fixed phyllotaxis spiral, so the same input gives the same layout.
- Size: `d3-force.min.js` is 8.2 kB (about 3 kB gzipped), `d3-zoom.min.js` 9.9 kB, plus `d3-zoom`'s dependencies (`d3-selection`, `d3-transition`, `d3-drag`, `d3-interpolate`, `d3-dispatch`). Both load only in the memory chunk, never in the entry chunk. Types come from `@types/d3-force` and `@types/d3-zoom` (dev).
- Version: both 3.0.1 releases were published on 2026-11-03, after five years on 3.0.0. Pin exact versions, compare the 3.0.1 dist against 3.0.0 before adding, and fall back to 3.0.0 if the diff is not a clean maintenance release.
- **Version check (task 1.1, done 2026-11-22): use 3.0.1.**
  - The UMD builds of `d3-force` and `d3-zoom` 3.0.1 match 3.0.0 apart from the copyright year and the Rollup 4 wrapper. In `d3-zoom` one redundant `type` key in the `ZoomEvent` options object is gone; `ZoomEvent` takes `type` as its own argument, so nothing changes.
  - The packages no longer ship `src/`; `main` and `exports` point to a new `dist/*.mjs` ESM build. That build holds the same code as the UMD build.
  - Tooling only: mocha → Vitest, Rollup 2 → 4. No install scripts. No network, `eval`, `Function` or `process.env` use in the shipped code.
  - The transitive packages were republished the same morning (`d3-drag` and `d3-selection` 3.0.1; `d3-transition`, `d3-interpolate`, `d3-dispatch`, `d3-timer`, `d3-quadtree` and `d3-ease` 3.0.2; `d3-color` 3.1.1). Their diffs are small fixes too: `Math.abs` in `interpolateZoom`'s duration, a rounded `setTimeout` delay in `d3-timer`, typed arrays in `quadtree.addAll`, and renamed bundle locals.
  - `d3-selection` 3.0.1 and `d3-transition` 3.0.2 are also direct dependencies (exact pins, same versions as in `d3-zoom`'s tree): `useGraphZoom.ts` imports `select` and the transition methods to attach the zoom and animate fit and zoom steps.
- **Alternatives:** keep the hand-written layout (no dependency, but we would rebuild Barnes–Hut, dragging and zoom ourselves); sigma.js + graphology or `force-graph` (canvas/WebGL; styling would no longer be CSS, the theme would have to be read at runtime, and accessibility and DOM tests get harder, for graph sizes we don't have).

### 2. Component split
```
memory/graph/
  useGraphLayout.ts   d3 simulation: forces, warm-up, settle, drag reheat
  useGraphZoom.ts     d3-zoom on the <svg>: transform ref, fitTo(bounds), zoomBy(k)
  graphLabels.ts      pure: which node ids are labelled at zoom k
  projectHulls.ts     pure: backdrop path per project from node positions and radii
  GraphCanvas.vue     the SVG: hulls, edges, nodes, labels, highlight, controls slot
GraphView.vue         map: fetch graph + projects, filters, legend, project grouping
LocalGraph.vue        editor: fetch neighbourhood, centre node fixed, no hulls
```
The pure modules carry most unit tests; `GraphCanvas` tests use stopped simulations.

### 3. Forces
| Force | Setting (starting values, tuned with the screenshot sweep) |
|---|---|
| `forceManyBody` | strength −120, `distanceMax` 300 |
| `forceLink` | distance 40 (`dependsOn` 30), strength from d3's degree-based default |
| `forceCollide` | node radius + 4 |
| `forceX` / `forceY` | strength 0.08 to the node's project anchor; anchors on a circle of radius ∝ √(project count) around the origin, ordered by project id; chunks without a project anchor at the origin |
| `forceCenter` | at the origin |

The local graph uses the same forces without project anchors and fixes the centre chunk at the origin (`fx`/`fy`).

### 4. Warm-up, settle, drag
- Run the simulation headless (`stop()`, `tick()`) until alpha is about 0.05, render that state, then let the remaining ticks play through `requestAnimationFrame` for about 600 ms.
- Under `prefers-reduced-motion`, tick to the end and render once.
- Dragging sets `fx`/`fy`, reheats to alpha 0.3, and releases on drop.
- A click is a click only if the pointer moved less than 4 px, so a drag does not open the chunk.
- Positions live in a `shallowRef` array and a frame counter triggers the render, to avoid deep reactivity on every node at every tick.

### 5. Zoom and fit
- `d3-zoom` with `scaleExtent` [0.25, 4], wheel and background drag.
- On-screen buttons: zoom in, zoom out, fit.
- Fit computes the bounds of the shown nodes plus label room and keeps the aspect ratio (the current x/y stretch goes).
- The first render is fitted.

### 6. Labels (`graphLabels.ts`)
- At fit zoom k₀: label the pinned chunks plus the top 10 by degree (ties by importance, then id).
- From k₀·1.5: add chunks with importance ≥ 3 or degree ≥ 3.
- From k₀·2.5: label all chunks.
- Labels fade in with a CSS opacity transition, so no label pops in.
- The highlight set (hovered or focused node and its neighbours) is always labelled.
- Label text has a fixed screen size: it is counter-scaled by 1/k.
- Labels near the left or right edge of the view grow inwards, as today.

### 7. Highlight
- One `highlight` ref (node id or null), set on `pointerenter`, `focus`, `pointerleave` and `blur`.
- Neighbours come from an adjacency map built once per data load.
- Non-highlighted nodes and edges get a `dim` class (opacity about 0.15, transitioned with `--transition`).
- Highlighted edges switch to `--ink-2`; `contradicts` keeps `--danger`.

### 8. Appearance
- **Node radius:** `4 + 1.2·importance + 1.5·√degree`.
- **Pinned:** a halo, a second circle at r + 4 in `--mem` at 0.25 opacity, instead of the hard ink stroke.
- **Edges:** quadratic curves with a small fixed bend, so parallel edges in both directions stay apart. 1 px in `--line-strong` at 0.6 opacity.
- **Arrowheads:** SVG `<marker>`s for `dependsOn` and `supersedes` only, with `context-stroke` fill. Where `context-stroke` is unsupported, a solid `--line-strong` fill is used.
- `contradicts` keeps the dashed `--danger` stroke; `supersedes` keeps its short dash.
- Nodes keep `role="link"`, `tabindex="0"` and an `aria-label`. The `<svg>` keeps `role="img"` with a summary label.

### 9. Project backdrop (`projectHulls.ts`)
- Each project's backdrop is the union of circles of radius r + 14 around its nodes. These are drawn as one `<path>` per project with `--surface-3` fill at reduced opacity, plus a light blur filter for a soft edge.
- The project name is drawn in `--ink-3` text at the top of the backdrop's bounding box.
- This needs no `d3-polygon`. Union of circles also suits concave clusters, where a convex hull would swallow nodes from other projects.
- Project names come from `GET /api/v1/memory/projects`. If that load fails, the backdrops are drawn without names rather than failing the graph.
- **Alternatives:** a padded convex hull (needs `d3-polygon`, swallows nodes in concave clusters); colour per project (conflicts with the type colours, needs new tokens).

### 10. Type filters
- Legend chips become toggle buttons (`aria-pressed`). Hidden types are removed from the nodes passed to the simulation and to fit. Their edges go too.
- The last shown type's chip is disabled.
- The filter state lives in the route query (`?hide=event,fact`), like `?view=scatter`, so a link or reload keeps it.

### 11. Neighbourhood endpoint
- `GET /api/v1/memory/chunks/{id}/graph` returns `MemoryGraph`, the same shape as `/memory/graph`, so the web drawing code and types are shared.
- `IMemoryMaps.GetNeighbourhoodAsync(ChunkId, CancellationToken)` returns `MemoryGraph?`: `null` when the chunk is missing, deleted or another owner's. This matches `IMemoryBrowser.GetHistoryAsync`.
- It loads the chunk's live edges in both directions, then the live neighbour chunks, then the edges among that set. All queries are owner-filtered EF queries.
- The endpoint maps `null` to the existing `ChunkNotFound()` 404 `ProblemDetails`.
- `Marshal.Demo` does not implement `IMemoryMaps`, so no simulation change is needed.

### 12. Local graph in the editor
- Fixed height (about 280 px), full content width, no backdrops, no type filters.
- The same highlight and labels as the map. Zoom controls are hidden on phones; pinch and drag still work.
- It is shown only when the neighbourhood has more than one node.
- A failed load shows the existing inline error pattern and leaves the Links list usable.

### 13. Changes found while building (2026-11-22)
These came out of the screenshot sweep and the browser checks; they refine Decisions 3–9.
- **Forces:** many-body −160 (`distanceMax` 320), link distance 52 (`dependsOn` 40), collide r + 8. The starting values packed clusters too tightly for their labels. Project anchors sit on an ellipse 1.3 × wider than tall, starting on the left, so two projects lie side by side in the wide drawing.
- **Local graph:** link lengths × 2 (`spread`). A handful of nodes has room, and the labels no longer collide.
- **Fit:** label room 60 × 40, plus insets kept free for the zoom buttons (right) and the legend (bottom, map only).
- **Backdrop padding:** r + 24, not r + 14. At 14 the circles barely overlap and draw as separate blobs. The blur stays; it costs little.
- **Arrowheads:** a small filled `<path>` per directed edge, along the curve's end tangent, instead of SVG `<marker>`s. Markers are re-instanced on every edge update, and they halved the frame rate of the 500-chunk graph while it moved. `context-stroke` is no longer needed: CSS colours the head with its edge.
- **Labels:** only shown labels are rendered, on their own layer above every node, so no later node covers them. Each fades in with a CSS animation as it appears, and there is no animation under reduced motion. Hover dims a label with its node.
- **Adaptive settle:** if two settle frames in a row take more than 50 ms, the layout jumps to its final state instead of stuttering towards it. Drags keep animating.
- **Backdrops** ignore the pointer, so hover and drag reach the nodes under them.

## Risks / Trade-offs

- [Vue re-rendering hundreds of nodes per frame stutters during the settle or a drag] → most ticks run off screen, positions are in a `shallowRef`, labels and hull paths update only every few frames during motion; measure with 500 synthetic chunks in preview mode.
- [`d3-zoom` relies on pointer and wheel events that jsdom only partly supports] → unit-test the pure label, hull and fit logic, test zoom via the exposed `fitTo` and `zoomBy` functions, and cover gestures in the Playwright screenshot sweep.
- [3.0.1 is a fresh release of long-stable packages] → exact pins, dist diff against 3.0.0 before adding (Decision 1).
- [Project anchors fight strong cross-project links, stretching edges across the canvas] → keep the anchor strength weak (0.08); backdrops are drawn from actual positions, so a chunk pulled away is simply outside its backdrop.
- [Blur filters on large paths are costly on low-end GPUs] → one filter per project path, not per node; drop the blur if the sweep shows jank.
- [Label counter-scaling at high zoom overlaps dense clusters] → collide radius includes some label room at high zoom; accept some overlap at full zoom, as Obsidian does.

## Migration Plan

Web and API ship together in one release. There are no data changes. Rollback is reverting the release: the new endpoint is unused by anything else.
