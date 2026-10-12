<script setup lang="ts">
import { Network } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { LoadBoundary } from '../ui'
import MapLegend from './MapLegend.vue'
import { forceLayout } from './forceLayout'

/** Memory chunks as a force-laid-out graph of their links; a node opens its chunk. */
const api = useApi()
const router = useRouter()
const { t } = useI18n()

const graph = useLoad(() => api.call((client) => client.GET('/api/v1/memory/graph')), { isEmpty: (data) => !data.nodes.length })
// The layout is stretched onto a fixed canvas, so a few far-apart clusters do not shrink every node to a dot.
const width = 1000
const height = 600

/** Labels of nodes near the left or right edge grow inwards, so they are not cut off by the drawing's border. */
function labelAnchor(x: number): 'start' | 'middle' | 'end' {
  return x < width * 0.2 ? 'start' : x > width * 0.8 ? 'end' : 'middle'
}
const pad = 60
const drawing = computed(() => {
  const data = graph.data.value
  if (!data?.nodes.length) return null
  const layout = forceLayout(
    data.nodes.map((node) => node.id),
    data.edges.map((edge) => [edge.from, edge.to]),
  )
  const points = [...layout.values()]
  const minX = Math.min(...points.map((point) => point.x))
  const minY = Math.min(...points.map((point) => point.y))
  const spanX = Math.max(...points.map((point) => point.x)) - minX || 1
  const spanY = Math.max(...points.map((point) => point.y)) - minY || 1
  const positions = new Map(
    [...layout].map(([id, point]) => [id, { x: pad + ((point.x - minX) / spanX) * (width - 2 * pad), y: pad + ((point.y - minY) / spanY) * (height - 2 * pad) }]),
  )
  return { positions, viewBox: `0 0 ${width} ${height}` }
})
const open = (id: string) => router.push(`/memory/chunks/${id}`)
</script>

<template>
  <LoadBoundary :load="graph" size="page" :empty-title="t('memory.map.empty')" :empty-icon="Network">
    <div v-if="graph.data.value && drawing" class="canvas">
      <svg class="map" :viewBox="drawing.viewBox" role="img" :aria-label="t('memory.map.graphLabel')">
        <line
          v-for="edge in graph.data.value.edges"
          :key="`${edge.from}/${edge.kind}/${edge.to}`"
          :data-edge="edge.kind"
          class="edge"
          :class="edge.kind"
          :x1="drawing.positions.get(edge.from)!.x"
          :y1="drawing.positions.get(edge.from)!.y"
          :x2="drawing.positions.get(edge.to)!.x"
          :y2="drawing.positions.get(edge.to)!.y"
        >
          <title>{{ t(`memory.editor.edge.${edge.kind}`) }}</title>
        </line>
        <g
          v-for="node in graph.data.value.nodes"
          :key="node.id"
          :data-node="node.id"
          class="node"
          :class="[node.type, { pinned: node.pinned }]"
          tabindex="0"
          role="link"
          :aria-label="node.title"
          :transform="`translate(${drawing.positions.get(node.id)!.x} ${drawing.positions.get(node.id)!.y})`"
          @click="open(node.id)"
          @keydown.enter="open(node.id)"
        >
          <circle :r="5 + node.importance * 1.5" />
          <text :y="-(10 + node.importance * 1.5)" :text-anchor="labelAnchor(drawing.positions.get(node.id)!.x)">{{ node.title }}</text>
        </g>
      </svg>
      <MapLegend class="legend" />
    </div>
  </LoadBoundary>
</template>

<style scoped>
.canvas {
  position: relative;
  border-radius: var(--radius-lg);
  background-color: var(--surface);
  background-image: radial-gradient(var(--line) 1px, transparent 1px);
  background-size: 22px 22px;
  box-shadow: var(--shadow-1);
  overflow: hidden;
}
.map {
  display: block;
  width: 100%;
  height: 70vh;
  min-height: 420px;
}
.edge {
  stroke: var(--line-strong);
  stroke-width: 1.5;
}
.edge.contradicts {
  stroke: var(--danger);
  stroke-dasharray: 6 4;
}
.edge.supersedes {
  stroke-dasharray: 3 3;
}
.node {
  cursor: pointer;
}
.node circle {
  fill: var(--mem);
  stroke: var(--surface);
  stroke-width: 2;
}
.node.pinned circle {
  stroke: var(--ink);
}
.node:focus-visible {
  outline: none;
}
.node:focus-visible circle {
  stroke: var(--accent);
  stroke-width: 3;
}
.node text {
  fill: var(--ink-2);
  font: 500 12px var(--font-sans);
  paint-order: stroke;
  stroke: var(--surface);
  stroke-width: 4px;
}
.legend {
  position: absolute;
  left: var(--space-4);
  bottom: var(--space-4);
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
