<script setup lang="ts">
import { defineAsyncComponent, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { LoadBoundary, useDesktop } from '../ui'

// d3 and the drawing load with the graph, never in the entry chunk.
const GraphCanvas = defineAsyncComponent(() => import('./graph/GraphCanvas.vue'))

/**
 * The chunk editor's local graph: the chunk fixed at the centre, its direct neighbours and the links among them.
 * No backdrops or filters; on phones the zoom buttons give way to pinch and drag.
 */
const props = defineProps<{ chunkId: string }>()
const api = useApi()
const router = useRouter()
const desktop = useDesktop()
const { t } = useI18n()

const graph = useLoad(() => api.call((client) => client.GET('/api/v1/memory/chunks/{id}/graph', { params: { path: { id: props.chunkId } } })), {
  // Only the chunk itself (every neighbour deleted): nothing worth drawing.
  isEmpty: (data) => data.nodes.length <= 1,
})
watch(
  () => props.chunkId,
  () => graph.retry(),
)
</script>

<template>
  <div data-test="local-graph">
    <LoadBoundary :load="graph" :error-title="t('states.couldNotLoad')">
      <template #empty />
      <template #default="{ data }">
        <GraphCanvas
          :key="chunkId"
          class="local"
          :nodes="data.nodes"
          :edges="data.edges"
          :label="t('memory.graph.localLabel')"
          :centre-id="chunkId"
          :spread="2"
          :controls="desktop"
          @open="(id) => router.push(`/memory/chunks/${id}`)"
        />
      </template>
    </LoadBoundary>
  </div>
</template>

<style scoped>
.local {
  height: 280px;
  min-height: 0;
}
</style>
