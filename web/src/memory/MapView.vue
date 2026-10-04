<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { Banner, ListCard, ListRow, LoadBoundary, Page, PageHeader, Section, Segmented, useDesktop } from '../ui'
import ChunkType from './ChunkType.vue'
import GraphView from './GraphView.vue'
import MemoryTabs from './MemoryTabs.vue'
import ScatterView from './ScatterView.vue'
import { chunkTypes } from './memoryLabels'

type View = 'graph' | 'scatter'

/**
 * The memory map: the link graph or the embedding scatter on desktops (`?view=scatter`); phones get the chunks
 * grouped by type, since neither drawing is usable at that size.
 */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const { t } = useI18n()

const view = computed<View>({
  get: () => (route.query.view === 'scatter' ? 'scatter' : 'graph'),
  set: (value) => void router.replace({ query: value === 'scatter' ? { view: 'scatter' } : {} }),
})
const views = computed(() => [
  { value: 'graph' as const, label: t('memory.map.graph') },
  { value: 'scatter' as const, label: t('memory.map.scatter') },
])

// Phones list the graph's nodes; the load starts only there.
const nodes = desktop.value ? null : useLoad(() => api.call((client) => client.GET('/api/v1/memory/graph')), { isEmpty: (data) => !data.nodes.length })
const groups = computed(() => chunkTypes.map((type) => ({ type, nodes: (nodes?.data.value?.nodes ?? []).filter((node) => node.type === type) })).filter((group) => group.nodes.length))
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('memory.title')">
      <template v-if="desktop" #actions>
        <Segmented v-model="view" :options="views" :label="t('memory.map.view')" />
      </template>
    </PageHeader>
    <MemoryTabs />
    <template v-if="desktop">
      <GraphView v-if="view === 'graph'" />
      <ScatterView v-else />
    </template>
    <template v-else-if="nodes">
      <Banner tone="info" :title="t('memory.map.phoneTitle')" :body="t('memory.map.phoneBody')" />
      <LoadBoundary :load="nodes" :empty-title="t('memory.map.empty')">
        <Section v-for="group in groups" :key="group.type" :title="t(`memory.type.${group.type}`)" :count="group.nodes.length" :data-group="group.type">
          <ListCard>
            <ListRow v-for="node in group.nodes" :key="node.id" :title="node.title" :to="`/memory/chunks/${node.id}`">
              <template #lead><ChunkType :type="node.type" /></template>
            </ListRow>
          </ListCard>
        </Section>
      </LoadBoundary>
    </template>
  </Page>
</template>
