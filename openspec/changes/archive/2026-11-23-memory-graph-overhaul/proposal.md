# Proposal

## Why

The memory map's graph is hard to read and feels static. It draws every label at once, stretches the layout to the canvas so clusters look squashed, draws all edges the same way, and offers no zoom, panning, dragging or hover highlight. Its hand-written layout compares every pair of nodes and gets slow at a few hundred chunks. Graph views in note tools such as Obsidian show that a link graph can be useful and good to look at; we want that quality inside Marshal's own design system.

## What Changes

- The memory graph becomes interactive:
  - Zoom, pan and a fit-to-view control.
  - Dragging a node moves it, and the layout reacts while it moves.
  - Hovering or focusing a node highlights it and its direct neighbours and dims the rest.
- Labels follow the zoom level. At fit zoom only pinned chunks and the chunks with the most links are labelled; more labels fade in as the user zooms in. The hovered or focused node and its neighbours are always labelled.
- Nodes and edges are restyled:
  - Node size reflects importance and number of links, and pinned chunks get a halo.
  - Edges are thin curves, with direction shown only for `dependsOn` and `supersedes`; `contradicts` stays visually distinct.
  - The layout settles with a short animation, which is skipped when the user prefers reduced motion.
- The legend's chunk types become filters that hide or show chunks of that type.
- Chunks of the same project are pulled together. Each project's cluster gets a neutral backdrop with the project's name. Hue keeps meaning chunk type only.
- New endpoint `GET /api/v1/memory/chunks/{id}/graph`: a chunk, its direct neighbours, and the edges between them. This is an addition to API v1; no existing contract changes.
- The chunk editor shows this 1-hop local graph above its links list, at every width including phones.
- The hand-written layout is replaced by `d3-force` and `d3-zoom` (new web dependencies). The graph stays an SVG drawn by Vue.

## Non-goals

- No canvas or WebGL rendering, and no support for graphs much larger than one person's memory (low thousands of chunks).
- No user-tunable force settings (Obsidian's sliders), saved layouts or pinned node positions.
- No depth-2 local graph and no graph on the memory map at phone width (the list stays).
- No colour per project; no new colour tokens.
- No change to the scatter view, the graph endpoint `GET /api/v1/memory/graph`, or memory data.
- No editing (creating or removing edges) from the graph.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `memory-view`: adds requirements for the graph's interaction, labels, filters and project grouping, the chunk neighbourhood endpoint, and the local graph on the chunk editor. Existing requirements are unchanged.

## Impact

- **Web**: `web/src/memory/` (`GraphView.vue`, `MapLegend.vue`, `ChunkEditorView.vue`, `forceLayout.ts` is removed, new shared graph drawing pieces), i18n messages, preview data (`web/src/preview/scenarios/normal.ts` and `index.ts`), Vitest tests, and the screenshot sweep.
- **API**: new endpoint in `Marshal.Api/Memory/MemoryEndpoints.cs`; `IMemoryMaps` gains a neighbourhood method, implemented in `Marshal.Memory`; the OpenAPI document `web/openapi/v1.json` and `web/src/api/schema.d.ts` are regenerated.
- **Dependencies**: `d3-force` and `d3-zoom` (v3, ISC) in `web/package.json`; the CLAUDE.md package index lists them.
- **Docs**: the Map row in `docs/guides/memory.md` and `memory.de.md`; the API v1 reference picks up the new endpoint from the OpenAPI document.
