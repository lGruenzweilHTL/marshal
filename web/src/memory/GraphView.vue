<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { forceLayout } from './forceLayout'
import { chunkTypes, typeColors } from './memoryLabels'

const session = useApi()
const router = useRouter()
const graph = ref<Schemas['MemoryGraph'] | null>(null)
const error = ref<string | null>(null)

const drawing = computed(() => {
  if (!graph.value) return null
  const positions = forceLayout(
    graph.value.nodes.map((node) => node.id),
    graph.value.edges.map((edge) => [edge.from, edge.to]),
  )
  const points = [...positions.values()]
  const pad = 20
  const minX = Math.min(...points.map((point) => point.x)) - pad
  const minY = Math.min(...points.map((point) => point.y)) - pad
  const width = Math.max(...points.map((point) => point.x)) - minX + pad
  const height = Math.max(...points.map((point) => point.y)) - minY + pad
  return { positions, viewBox: `${minX} ${minY} ${Math.max(width, 1)} ${Math.max(height, 1)}` }
})

onMounted(async () => {
  const { data } = await session.api.GET('/api/v1/memory/graph')
  graph.value = data ?? null
  error.value = data ? null : 'The graph could not be loaded.'
})
</script>

<template>
  <section class="page">
    <h1>Memory graph</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="graph && !graph.nodes.length" class="muted">No memory yet.</p>
    <template v-if="graph && graph.nodes.length && drawing">
      <ul class="legend plain">
        <li v-for="type in chunkTypes" :key="type"><span class="swatch" :style="{ background: typeColors[type] }"></span>{{ type }}</li>
      </ul>
      <svg class="map" :viewBox="drawing.viewBox" role="img" aria-label="Memory chunks and their links">
        <line
          v-for="edge in graph.edges"
          :key="`${edge.from}/${edge.kind}/${edge.to}`"
          :data-edge="edge.kind"
          :class="['edge', edge.kind]"
          :x1="drawing.positions.get(edge.from)!.x"
          :y1="drawing.positions.get(edge.from)!.y"
          :x2="drawing.positions.get(edge.to)!.x"
          :y2="drawing.positions.get(edge.to)!.y"
        >
          <title>{{ edge.kind }}</title>
        </line>
        <g
          v-for="node in graph.nodes"
          :key="node.id"
          :data-node="node.id"
          class="node"
          tabindex="0"
          role="link"
          :transform="`translate(${drawing.positions.get(node.id)!.x} ${drawing.positions.get(node.id)!.y})`"
          @click="router.push(`/memory/chunks/${node.id}`)"
          @keydown.enter="router.push(`/memory/chunks/${node.id}`)"
        >
          <circle :r="2 + node.importance" :fill="typeColors[node.type]" :stroke-width="node.pinned ? 1.5 : 0" />
          <text :y="-(4 + node.importance)" text-anchor="middle">{{ node.title }}</text>
        </g>
      </svg>
    </template>
  </section>
</template>

<style scoped>
.map {
  display: block;
  width: 100%;
  max-height: 75vh;
  border: 1px solid var(--border);
}

.edge {
  stroke: var(--muted, #888);
  stroke-width: 0.6;
}

.edge.contradicts {
  stroke: var(--error);
  stroke-dasharray: 2 1;
}

.edge.supersedes {
  stroke-dasharray: 1 1;
}

.node {
  cursor: pointer;
}

.node circle {
  stroke: currentColor;
}

.node text {
  font-size: 4px;
  fill: currentColor;
}

.legend {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem 1rem;
  margin-bottom: 0.5rem;
}

.swatch {
  display: inline-block;
  width: 0.75rem;
  height: 0.75rem;
  margin-right: 0.25rem;
  border-radius: 50%;
  vertical-align: middle;
}
</style>
