<script setup lang="ts">
import { Network } from 'lucide-vue-next'
import { computed, defineAsyncComponent } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { LoadBoundary } from '../ui'
import MapLegend from './MapLegend.vue'
import { chunkTypes } from './memoryLabels'

type Type = Schemas['ChunkType']

// d3 and the drawing load with the graph, never in the entry chunk.
const GraphCanvas = defineAsyncComponent(() => import('./graph/GraphCanvas.vue'))

/**
 * Memory chunks as an interactive graph of their links: each project's chunks grouped on a named backdrop, chunk types
 * filtered from the legend (`?hide=event,fact`), and a node opens its chunk.
 */
const api = useApi()
const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const graph = useLoad(() => api.call((client) => client.GET('/api/v1/memory/graph')), { isEmpty: (data) => !data.nodes.length })
// Names only label the backdrops; without them the graph still draws.
const projects = useLoad(() => api.call((client) => client.GET('/api/v1/memory/projects')))
const projectNames = computed(() => new Map((projects.data.value ?? []).map((project) => [project.id, project.name])))

const hidden = computed<ReadonlySet<Type>>(() => {
  const value = route.query.hide
  const names = typeof value === 'string' ? value.split(',') : []
  const set = new Set(chunkTypes.filter((type) => names.includes(type)))
  // At least one type stays shown, whatever the link says.
  return set.size === chunkTypes.length ? new Set() : set
})

function toggle(type: Type) {
  const next = new Set(hidden.value)
  if (next.has(type)) next.delete(type)
  else if (next.size < chunkTypes.length - 1) next.add(type)
  const query = { ...route.query }
  if (next.size) query.hide = chunkTypes.filter((entry) => next.has(entry)).join(',')
  else delete query.hide
  void router.replace({ query })
}

const shown = computed(() => {
  const data = graph.data.value
  if (!data) return { nodes: [], edges: [] }
  const nodes = data.nodes.filter((node) => !hidden.value.has(node.type))
  const ids = new Set(nodes.map((node) => node.id))
  return { nodes, edges: data.edges.filter((edge) => ids.has(edge.from) && ids.has(edge.to)) }
})

const open = (id: string) => router.push(`/memory/chunks/${id}`)
</script>

<template>
  <LoadBoundary :load="graph" size="page" :empty-title="t('memory.map.empty')" :empty-icon="Network">
    <GraphCanvas
      :nodes="shown.nodes"
      :edges="shown.edges"
      :label="t('memory.map.graphLabel')"
      anchors
      backdrops
      :project-names="projectNames"
      :bottom-inset="64"
      @open="open"
    >
      <MapLegend class="legend" :hidden="hidden" @toggle="toggle" />
    </GraphCanvas>
  </LoadBoundary>
</template>

<style scoped>
.legend {
  position: absolute;
  left: var(--space-4);
  bottom: var(--space-4);
}
</style>
