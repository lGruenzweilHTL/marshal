<script setup lang="ts">
import { Maximize, Minus, Plus } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, shallowRef, useId, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { AppButton, useMedia } from '../../ui'
import { labelledIds } from './graphLabels'
import { adjacency, type GraphEdge, type GraphNode } from './graphModel'
import { projectHulls } from './projectHulls'
import { useGraphLayout, type LayoutLink } from './useGraphLayout'
import { graphBounds, useGraphZoom } from './useGraphZoom'

/**
 * A memory graph drawn as SVG by Vue: d3-force lays it out, d3-zoom pans and zooms it. Nodes are coloured by type,
 * sized by importance and links; hovering or focusing one highlights its neighbourhood; labels follow the zoom.
 * Shared by the memory map (project anchors and backdrops) and the chunk editor's local graph (a fixed centre).
 */
const props = withDefaults(
  defineProps<{
    nodes: GraphNode[]
    edges: GraphEdge[]
    /** Accessible summary of the drawing. */
    label: string
    /** Pull each project's chunks together. */
    anchors?: boolean
    /** Draw a backdrop behind each project's chunks. */
    backdrops?: boolean
    /** Project names for the backdrops; without them the backdrops have no name. */
    projectNames?: ReadonlyMap<string, string> | null
    /** A chunk fixed at the centre and marked as such. */
    centreId?: string
    /** Show the zoom buttons. */
    controls?: boolean
    /** Room at the bottom kept free when fitting, for an overlay such as the legend. */
    bottomInset?: number
    /** Longer links for a small graph (see `GraphLayoutOptions.spread`). */
    spread?: number
  }>(),
  { anchors: false, backdrops: false, projectNames: null, centreId: undefined, controls: true, bottomInset: 0, spread: 1 },
)
const emit = defineEmits<{ open: [id: string] }>()
const { t } = useI18n()
// One soft edge per project backdrop (a filter per path, not per node).
const blurId = `graph-blur-${useId()}`

const reducedMotion = useMedia('(prefers-reduced-motion: reduce)')
const root = shallowRef<HTMLElement | null>(null)
const svg = shallowRef<SVGSVGElement | null>(null)
// jsdom and the first render have no layout: fall back to the map's usual proportions.
const size = shallowRef({ width: 1000, height: 600 })

const layout = useGraphLayout(() => ({ nodes: props.nodes, edges: props.edges }), { anchors: props.anchors, centreId: props.centreId, spread: props.spread }, reducedMotion)
const zoom = useGraphZoom(svg, size, reducedMotion)

const neighbours = computed(() => adjacency(props.nodes, props.edges))
const degree = computed(() => new Map([...neighbours.value].map(([id, set]) => [id, set.size])))

// Positions are mutated in place by d3; `frame` says when to read them again.
const drawn = computed(() => {
  void layout.frame.value
  return layout.nodes.value.map((entry) => ({ ...entry.node, x: entry.x, y: entry.y, r: entry.r }))
})
const position = computed(() => new Map(drawn.value.map((entry) => [entry.id, entry])))

const highlight = shallowRef<string | null>(null)
const lit = computed<ReadonlySet<string> | null>(() => {
  const id = highlight.value
  if (!id || !neighbours.value.has(id)) return null
  return new Set([id, ...neighbours.value.get(id)!])
})
const labelledNodes = computed(() => drawn.value.filter((entry) => labelled.value.has(entry.id)))
const labelled = computed(() => labelledIds(props.nodes, degree.value, zoom.transform.value.k, zoom.fitScale.value, lit.value ?? new Set()))

const directed = new Set<GraphEdge['kind']>(['dependsOn', 'supersedes'])
// Looked up once per language, not once per edge and frame.
const edgeTitles = computed(() => {
  const kinds: GraphEdge['kind'][] = ['relatesTo', 'dependsOn', 'supersedes', 'contradicts']
  return Object.fromEntries(kinds.map((kind) => [kind, t(`memory.editor.edge.${kind}`)])) as Record<GraphEdge['kind'], string>
})

/** Arrowhead size in graph units: it scales with the zoom like the nodes. */
const head = { length: 7, halfWidth: 3.5 }

/**
 * A slightly bent curve from circle edge to circle edge, so edges in both directions stay apart, plus an arrowhead
 * along the curve's end tangent for directed kinds. The head is its own small path, not an SVG marker: markers are
 * re-instanced on every edge update and halve the frame rate of a large graph while it moves.
 */
function curve(link: LayoutLink): { d: string; head: string | null } {
  const from = position.value.get(link.source.id)!
  const to = position.value.get(link.target.id)!
  const dx = to.x - from.x
  const dy = to.y - from.y
  const length = Math.hypot(dx, dy) || 1
  const bend = 0.12 * length
  const cx = (from.x + to.x) / 2 - (dy / length) * bend
  const cy = (from.y + to.y) / 2 + (dx / length) * bend
  const toward = (point: { x: number; y: number }, distance: number) => {
    const ux = cx - point.x
    const uy = cy - point.y
    const unit = Math.hypot(ux, uy) || 1
    return { x: point.x + (ux / unit) * distance, y: point.y + (uy / unit) * distance }
  }
  const start = toward(from, from.r)
  if (!directed.has(link.edge.kind)) {
    const end = toward(to, to.r)
    return { d: `M${start.x} ${start.y}Q${cx} ${cy} ${end.x} ${end.y}`, head: null }
  }
  const tip = toward(to, to.r + 1)
  const base = toward(to, to.r + 1 + head.length)
  const ux = (tip.x - base.x) / head.length
  const uy = (tip.y - base.y) / head.length
  const left = { x: base.x - uy * head.halfWidth, y: base.y + ux * head.halfWidth }
  const right = { x: base.x + uy * head.halfWidth, y: base.y - ux * head.halfWidth }
  return {
    d: `M${start.x} ${start.y}Q${cx} ${cy} ${base.x} ${base.y}`,
    head: `M${tip.x} ${tip.y}L${left.x} ${left.y}L${right.x} ${right.y}Z`,
  }
}

const edgesDrawn = computed(() =>
  layout.links.value.map((link) => {
    const set = lit.value
    const inLight = !!set && set.has(link.source.id) && set.has(link.target.id)
    return { key: `${link.edge.from}/${link.edge.kind}/${link.edge.to}`, kind: link.edge.kind, ...curve(link), lit: inLight, dim: !!set && !inLight }
  }),
)

// Backdrops follow the nodes, but only every few frames while the layout moves.
const hullFrame = computed(() => (layout.moving.value ? Math.floor(layout.frame.value / 4) : layout.frame.value))
const hulls = computed(() => {
  void hullFrame.value
  if (!props.backdrops) return []
  return projectHulls(layout.nodes.value.map((entry) => ({ x: entry.x, y: entry.y, r: entry.r, projectId: entry.node.projectId ?? null })))
})

const transform = computed(() => zoom.transform.value)
const counterScale = computed(() => `scale(${1 / transform.value.k})`)

/** Average glyph width of the 12 px label font, for estimating a label's width without measuring it. */
const glyphWidth = 6.5

/**
 * Labels that would run past the left or right edge of the view (or under the zoom buttons) grow inwards instead of
 * centring on their node, so the drawing's border does not cut them off.
 */
function labelAnchor(x: number, title: string): 'start' | 'middle' | 'end' {
  const screen = x * transform.value.k + transform.value.x
  const half = (title.length * glyphWidth) / 2
  const right = size.value.width - (props.controls ? 52 : 8)
  return screen - half < 8 ? 'start' : screen + half > right ? 'end' : 'middle'
}

function fit(animate: boolean) {
  // The zoom buttons sit at the top right; keep the graph clear of them.
  const side = props.controls ? 48 : 8
  zoom.fitTo(graphBounds(layout.nodes.value), animate, { top: 8, right: side, bottom: Math.max(8, props.bottomInset), left: 8 })
}
watch(
  () => [props.nodes, props.edges],
  () => fit(false),
)

let observer: ResizeObserver | null = null
onMounted(() => {
  const element = root.value!
  const measure = () => {
    if (element.clientWidth && element.clientHeight) size.value = { width: element.clientWidth, height: element.clientHeight }
  }
  measure()
  fit(false)
  if (typeof ResizeObserver === 'function') {
    observer = new ResizeObserver(() => {
      const before = size.value
      measure()
      if (size.value !== before) fit(false)
    })
    observer.observe(element)
  }
})
onBeforeUnmount(() => observer?.disconnect())

// A press becomes a drag once the pointer moves more than 4 px; a drag never opens the chunk.
const dragThreshold = 4
let press: { id: string; x: number; y: number; dragged: boolean } | null = null
let swallowClick = false

function toGraph(event: PointerEvent) {
  const rect = svg.value!.getBoundingClientRect()
  const sx = rect.width ? (event.clientX - rect.left) * (size.value.width / rect.width) : event.clientX
  const sy = rect.height ? (event.clientY - rect.top) * (size.value.height / rect.height) : event.clientY
  return { x: (sx - transform.value.x) / transform.value.k, y: (sy - transform.value.y) / transform.value.k }
}

function pointerDown(event: PointerEvent, id: string) {
  if (event.button !== 0) return
  press = { id, x: event.clientX, y: event.clientY, dragged: false }
  const target = event.currentTarget as Element
  if (event.pointerId !== undefined && typeof target.setPointerCapture === 'function') target.setPointerCapture(event.pointerId)
}

function pointerMove(event: PointerEvent) {
  if (!press) return
  if (!press.dragged) {
    if (Math.hypot(event.clientX - press.x, event.clientY - press.y) <= dragThreshold) return
    press.dragged = true
    layout.dragStart(press.id)
  }
  const point = toGraph(event)
  layout.dragMove(press.id, point.x, point.y)
}

function pointerUp() {
  if (press?.dragged) {
    layout.dragEnd(press.id)
    swallowClick = true
  }
  press = null
}

function click(id: string) {
  if (swallowClick) {
    swallowClick = false
    return
  }
  emit('open', id)
}

function enter(id: string) {
  highlight.value = id
}
function leave(id: string) {
  if (highlight.value === id && !press?.dragged) highlight.value = null
}
</script>

<template>
  <div ref="root" class="graph">
    <svg ref="svg" class="drawing" :viewBox="`0 0 ${size.width} ${size.height}`" role="img" :aria-label="label">
      <defs v-if="backdrops">
        <filter :id="blurId" x="-10%" y="-10%" width="120%" height="120%">
          <feGaussianBlur stdDeviation="6" />
        </filter>
      </defs>
      <g data-viewport :transform="`translate(${transform.x} ${transform.y}) scale(${transform.k})`">
        <g v-for="hull in hulls" :key="hull.projectId" class="backdrop" :data-backdrop="hull.projectId">
          <path :d="hull.path" :filter="`url(#${blurId})`" />
          <text v-if="projectNames?.get(hull.projectId)" class="project-name" :transform="`translate(${hull.label.x} ${hull.label.y}) ${counterScale}`" text-anchor="middle" dy="-6">
            {{ projectNames.get(hull.projectId) }}
          </text>
        </g>
        <g v-for="edge in edgesDrawn" :key="edge.key" :data-edge="edge.kind" class="edge" :class="[edge.kind, { lit: edge.lit, dim: edge.dim }]">
          <title>{{ edgeTitles[edge.kind] }}</title>
          <path class="line" :d="edge.d" />
          <path v-if="edge.head" class="head" data-arrow :d="edge.head" />
        </g>
        <g
          v-for="entry in drawn"
          :key="entry.id"
          :data-node="entry.id"
          :data-centre="entry.id === centreId ? 'true' : undefined"
          class="node"
          :class="[entry.type, { pinned: entry.pinned, centre: entry.id === centreId, dim: !!lit && !lit.has(entry.id) }]"
          tabindex="0"
          role="link"
          :aria-label="entry.id === centreId ? t('memory.graph.centre', { title: entry.title }) : entry.title"
          :transform="`translate(${entry.x} ${entry.y})`"
          @pointerenter="enter(entry.id)"
          @pointerleave="leave(entry.id)"
          @focus="enter(entry.id)"
          @blur="leave(entry.id)"
          @pointerdown="pointerDown($event, entry.id)"
          @pointermove="pointerMove"
          @pointerup="pointerUp"
          @pointercancel="pointerUp"
          @click="click(entry.id)"
          @keydown.enter="emit('open', entry.id)"
        >
          <circle v-if="entry.pinned" class="halo" :r="entry.r + 4" />
          <circle class="dot" :r="entry.r" />
        </g>
        <!-- Labels sit on their own layer above every node, so no later node covers them. -->
        <g class="labels" aria-hidden="true">
          <text
            v-for="entry in labelledNodes"
            :key="entry.id"
            class="label"
            :class="{ dim: !!lit && !lit.has(entry.id) }"
            :data-label="entry.id"
            :transform="`translate(${entry.x} ${entry.y - entry.r - 5}) ${counterScale}`"
            :text-anchor="labelAnchor(entry.x, entry.title)"
          >
            {{ entry.title }}
          </text>
        </g>
      </g>
    </svg>
    <div v-if="controls" class="controls" data-controls role="group" :aria-label="t('memory.graph.controls')">
      <AppButton size="sm" variant="ghost" :icon="Plus" icon-only :label="t('memory.graph.zoomIn')" data-zoom="in" @click="zoom.zoomBy(1.4)" />
      <AppButton size="sm" variant="ghost" :icon="Minus" icon-only :label="t('memory.graph.zoomOut')" data-zoom="out" @click="zoom.zoomBy(1 / 1.4)" />
      <AppButton size="sm" variant="ghost" :icon="Maximize" icon-only :label="t('memory.graph.fit')" data-zoom="fit" @click="fit(true)" />
    </div>
    <slot />
  </div>
</template>

<style scoped>
.graph {
  position: relative;
  height: 70vh;
  min-height: 420px;
  border-radius: var(--radius-lg);
  background-color: var(--surface);
  background-image: radial-gradient(var(--line) 1px, transparent 1px);
  background-size: 22px 22px;
  box-shadow: var(--shadow-1);
  overflow: hidden;
}
.drawing {
  display: block;
  width: 100%;
  height: 100%;
  cursor: grab;
  touch-action: none;
  user-select: none;
}
.drawing:active {
  cursor: grabbing;
}
.backdrop {
  pointer-events: none;
}
.backdrop path {
  fill: var(--surface-3);
  opacity: 0.7;
}
.project-name {
  fill: var(--ink-3);
  font: 600 12px var(--font-sans);
  pointer-events: none;
}
.edge {
  transition: opacity var(--transition);
}
.edge .line {
  fill: none;
  stroke: var(--line-strong);
  stroke-width: 1px;
  stroke-opacity: 0.6;
  vector-effect: non-scaling-stroke;
}
.edge .head {
  fill: var(--line-strong);
  fill-opacity: 0.8;
}
.edge.lit .line {
  stroke: var(--ink-2);
  stroke-opacity: 1;
}
.edge.lit .head {
  fill: var(--ink-2);
  fill-opacity: 1;
}
.edge.contradicts .line {
  stroke: var(--danger);
  stroke-opacity: 0.9;
  stroke-dasharray: 6 4;
}
.edge.supersedes .line {
  stroke-dasharray: 3 3;
}
.edge.dim,
.label.dim,
.node.dim {
  opacity: 0.15;
}
.node {
  cursor: pointer;
  transition: opacity var(--transition);
}
.node .dot {
  fill: var(--mem);
  stroke: var(--surface);
  stroke-width: 1.5px;
  vector-effect: non-scaling-stroke;
}
.node .halo {
  fill: var(--mem);
  opacity: 0.25;
}
.node.centre .dot {
  stroke: var(--ink);
  stroke-width: 2px;
}
.node:focus-visible {
  outline: none;
}
.node:focus-visible .dot {
  stroke: var(--accent);
  stroke-width: 3px;
}
.label {
  fill: var(--ink-2);
  font: 500 12px var(--font-sans);
  paint-order: stroke;
  stroke: var(--surface);
  stroke-width: 4px;
  stroke-linejoin: round;
  pointer-events: none;
  /* Only shown labels exist; each fades in as it appears, so none pops in. */
  animation: label-in 160ms ease-out;
}
@keyframes label-in {
  from {
    opacity: 0;
  }
}
@media (prefers-reduced-motion: reduce) {
  .label {
    animation: none;
  }
}
.controls {
  position: absolute;
  top: var(--space-3);
  right: var(--space-3);
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 2px;
  border-radius: var(--radius-sm);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.fact {
  --mem: var(--mem-fact);
}
.preference {
  --mem: var(--mem-preference);
}
.event {
  --mem: var(--mem-event);
}
.procedure {
  --mem: var(--mem-procedure);
}
.person {
  --mem: var(--mem-person);
}
.device {
  --mem: var(--mem-device);
}
</style>
