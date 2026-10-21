<script setup lang="ts">
import { BookOpen, Pin, Plus } from 'lucide-vue-next'
import { computed, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { AppButton, AppIcon, Badge, Chip, DataTable, Field, ListCard, ListRow, LoadBoundary, Page, PageHeader, useDesktop } from '../ui'
import ChunkType from './ChunkType.vue'
import ImportanceDots from './ImportanceDots.vue'
import MemoryTabs from './MemoryTabs.vue'
import { chunkTypes } from './memoryLabels'
import { isClosed } from './validTime'

/** Memory chunks: full-text search, type, project and closed filters (a project card links here with ?project=). */
const api = useApi()
const route = useRoute()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const filter = reactive({ query: '', type: '', project: typeof route.query.project === 'string' ? route.query.project : '', pinned: false, closed: false })
const projects = useLoad(() => api.call((client) => client.GET('/api/v1/memory/projects')), { isEmpty: () => false })
const chunks = useLoad(() =>
  api.call((client) =>
    client.GET('/api/v1/memory/chunks', {
      params: {
        query: {
          query: filter.query.trim() || undefined,
          type: (filter.type || undefined) as Schemas['ChunkType'] | undefined,
          projectId: filter.project || undefined,
          pinned: filter.pinned ? true : undefined,
          closed: filter.closed ? true : undefined,
        },
      },
    }),
  ),
)
const projectNames = computed(() => new Map((projects.data.value ?? []).map((project) => [project.id, project.name])))
const projectName = (id: string | null) => (id ? (projectNames.value.get(id) ?? t('memory.list.deletedProject')) : t('memory.list.general'))

function setType(type: string) {
  filter.type = filter.type === type ? '' : type
  chunks.retry()
}
function togglePinned() {
  filter.pinned = !filter.pinned
  chunks.retry()
}
function toggleClosed() {
  filter.closed = !filter.closed
  chunks.retry()
}

const columns = computed(() => [
  { key: 'chunk', label: t('memory.list.columns.chunk') },
  { key: 'type', label: t('memory.list.columns.type') },
  { key: 'project', label: t('memory.list.columns.project') },
  { key: 'importance', label: t('memory.list.columns.importance') },
  { key: 'updated', label: t('memory.list.columns.updated') },
])
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('memory.title')" :subtitle="desktop ? t('memory.subtitle') : undefined">
      <template #actions>
        <AppButton variant="primary" :icon="Plus" :icon-only="!desktop" :label="t('memory.list.newChunk')" to="/memory/new">{{ t('memory.list.newChunk') }}</AppButton>
      </template>
    </PageHeader>
    <MemoryTabs />

    <form class="tools" data-test="search" role="search" @submit.prevent="chunks.retry()">
      <Field v-model="filter.query" kind="search" name="query" :label="t('memory.list.search')" hide-label :placeholder="t('memory.list.search')" class="search" />
      <Field v-model="filter.project" kind="select" name="project" :label="t('memory.list.project')" hide-label class="project" @change="chunks.retry()">
        <option value="">{{ t('memory.list.allProjects') }}</option>
        <option v-for="project in projects.data.value ?? []" :key="project.id" :value="project.id">{{ project.name }}</option>
      </Field>
      <div class="chips">
        <Chip :label="t('memory.list.allTypes')" :model-value="!filter.type" data-type="" @update:model-value="setType('')" />
        <Chip v-for="type in chunkTypes" :key="type" :label="t(`memory.type.${type}`)" :model-value="filter.type === type" :data-type="type" @update:model-value="setType(type)" />
        <Chip :label="t('memory.pinned')" :model-value="filter.pinned" data-test="pinned-only" @update:model-value="togglePinned" />
        <Chip :label="t('memory.list.closedOnly')" :model-value="filter.closed" data-test="closed-only" @update:model-value="toggleClosed" />
      </div>
    </form>

    <LoadBoundary :load="chunks" size="page" :empty-title="t('memory.list.empty.title')" :empty-body="t('memory.list.empty.body')" :empty-icon="BookOpen">
      <template #default="{ data }">
        <DataTable v-if="desktop" :columns="columns" :rows="data" :row-key="(chunk) => chunk.id" :label="t('memory.title')" row-attribute="chunk">
          <template #cell-chunk="{ row }">
            <span class="title">
              <AppIcon v-if="row.pinned" :icon="Pin" :size="14" class="pin" data-test="pinned" :aria-label="t('memory.pinned')" />
              <RouterLink :to="`/memory/chunks/${row.id}`" class="link">{{ row.title }}</RouterLink>
              <Badge v-if="row.validTo && isClosed(row.validTo)" data-test="closed" :label="t('memory.list.until', { date: format.date(row.validTo) })" />
            </span>
            <span class="content">{{ row.content }}</span>
          </template>
          <template #cell-type="{ row }"><ChunkType :type="row.type" /></template>
          <template #cell-project="{ row }"><span class="ink-2">{{ projectName(row.projectId) }}</span></template>
          <template #cell-importance="{ row }"><ImportanceDots :value="row.importance" /></template>
          <template #cell-updated="{ row }"><span class="num ink-2">{{ format.dateTime(row.updatedAt) }}</span></template>
        </DataTable>
        <ListCard v-else>
          <ListRow v-for="chunk in data" :key="chunk.id" :to="`/memory/chunks/${chunk.id}`" :data-chunk="chunk.id">
            <template #title>
              <span class="row-title">{{ chunk.title }}<AppIcon v-if="chunk.pinned" :icon="Pin" :size="14" class="pin" data-test="pinned" /></span>
            </template>
            <template #subtitle><span class="content">{{ chunk.content }}</span></template>
            <template #trail>
              <Badge v-if="chunk.validTo && isClosed(chunk.validTo)" data-test="closed" :label="t('memory.list.until', { date: format.date(chunk.validTo) })" />
              <ChunkType :type="chunk.type" />
            </template>
          </ListRow>
        </ListCard>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.tools {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-3);
}
.search {
  flex: 1 1 260px;
  max-width: 360px;
}
.project {
  flex: 0 1 200px;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.title {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.pin {
  color: var(--accent-text);
}
/* The pin follows the title, so wrapped titles line up with the other rows. */
.row-title .pin {
  display: inline;
  margin-left: 6px;
  vertical-align: -1px;
}
.link {
  color: var(--ink);
  font-weight: 600;
  text-decoration: none;
}
.link:hover {
  text-decoration: underline;
}
.content {
  display: -webkit-box;
  overflow: hidden;
  color: var(--ink-2);
  font: var(--text-body-s);
  -webkit-line-clamp: 1;
  line-clamp: 1;
  -webkit-box-orient: vertical;
}
</style>
