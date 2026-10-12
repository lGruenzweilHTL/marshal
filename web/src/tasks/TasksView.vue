<script setup lang="ts">
import { ClipboardList } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat } from '../i18n/locale'
import { useLiveEvent } from '../live/liveEvents'
import { Badge, Chip, DataTable, Field, IconTile, ListCard, ListRow, LoadBoundary, LoadState, Page, PageHeader, useDesktop } from '../ui'
import BudgetMeter from './BudgetMeter.vue'
import KillSwitch from './KillSwitch.vue'
import TriggerLabel from './TriggerLabel.vue'
import { taskLabel } from './taskFormat'
import { statusTone } from './taskTone'

type Filter = 'active' | 'all' | Schemas['AgentTaskStatus']
const filters: Filter[] = ['active', 'all', 'running', 'waiting', 'pending', 'done', 'failed', 'cancelled']
const active: Schemas['AgentTaskStatus'][] = ['pending', 'running', 'waiting']

/** All tasks and watches; status chips (kept in the query) and a search narrow the list on the client. */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const tasks = useLoad(() => api.call((client) => client.GET('/api/v1/tasks')), { isEmpty: () => false })
useLiveEvent('tasks', 'task.changed', () => tasks.refresh())

const search = shallowRef('')
const filter = computed<Filter>(() => {
  const value = route.query.status
  return typeof value === 'string' && (filters as string[]).includes(value) ? (value as Filter) : 'active'
})
const matches = (task: Schemas['TaskItem'], value: Filter) => value === 'all' || (value === 'active' ? active.includes(task.status) : task.status === value)
const count = (value: Filter) => (tasks.data.value ?? []).filter((task) => matches(task, value)).length
const shown = computed(() => {
  const query = search.value.trim().toLowerCase()
  return (tasks.data.value ?? [])
    .filter((task) => matches(task, filter.value))
    .filter((task) => !query || `${task.name ?? ''} ${task.goal}`.toLowerCase().includes(query))
})
const running = computed(() => (tasks.data.value ?? []).filter((task) => task.status === 'running').length)

function choose(value: Filter) {
  void router.replace({ query: value === 'active' ? {} : { status: value } })
}

const columns = computed(() => [
  { key: 'task', label: t('tasks.columns.task') },
  { key: 'trigger', label: t('tasks.columns.trigger') },
  { key: 'next', label: t('tasks.columns.next') },
  { key: 'spent', label: t('tasks.columns.spent'), width: '170px' },
  { key: 'status', label: t('tasks.columns.status') },
])
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('tasks.title')" :subtitle="desktop ? t('tasks.subtitle') : undefined" />
    <KillSwitch :running="running" @changed="tasks.refresh()" />
    <div class="tools">
      <Field v-model="search" kind="search" :label="t('tasks.search')" hide-label :placeholder="t('tasks.search')" class="search" />
      <div class="chips" role="group" :aria-label="t('tasks.columns.status')">
        <Chip
          v-for="value in filters"
          :key="value"
          :data-status="value"
          :label="`${t(`tasks.filter.${value}`)} ${count(value)}`"
          :model-value="filter === value"
          @update:model-value="choose(value)"
        />
      </div>
    </div>

    <LoadBoundary :load="tasks" size="page">
      <LoadState v-if="!tasks.data.value?.length" state="empty" size="page" :icon="ClipboardList" :title="t('tasks.empty.title')" :body="t('tasks.empty.body')" />
      <LoadState v-else-if="!shown.length" state="empty" :title="t('tasks.noMatch')" />
      <DataTable v-else-if="desktop" :columns="columns" :rows="shown" :row-key="(task) => task.id" :label="t('tasks.title')" row-attribute="task">
        <template #cell-task="{ row }">
          <RouterLink :to="`/tasks/${row.id}`" class="name" data-test="name">{{ taskLabel(row) }}</RouterLink>
          <span v-if="row.name" class="goal">{{ row.goal }}</span>
        </template>
        <template #cell-trigger="{ row }"><TriggerLabel :trigger="row.trigger" /></template>
        <template #cell-next="{ row }">
          <span class="num ink-2">{{ row.nextRunAt ? format.dateTime(row.nextRunAt) : '–' }}</span>
        </template>
        <template #cell-spent="{ row }"><BudgetMeter :spent="row.spent.cost" :budget="row.budget.cost" /></template>
        <template #cell-status="{ row }">
          <Badge :tone="statusTone[row.status]" dot :label="t(`tasks.status.${row.status}`)" />
          <span v-if="row.statusReason" class="reason">{{ row.statusReason }}</span>
        </template>
      </DataTable>
      <ListCard v-else>
        <ListRow v-for="task in shown" :key="task.id" :to="`/tasks/${task.id}`" :data-task="task.id">
          <template #lead><IconTile :icon="ClipboardList" :tone="task.status === 'running' ? 'info' : task.status === 'failed' ? 'danger' : 'neutral'" /></template>
          <template #title><span data-test="name">{{ taskLabel(task) }}</span></template>
          <template #subtitle>
            <TriggerLabel :trigger="task.trigger" /><template v-if="task.nextRunAt"> · <span class="num">{{ format.relative(task.nextRunAt) }}</span></template>
          </template>
          <template #trail><Badge :tone="statusTone[task.status]" :label="t(`tasks.status.${task.status}`)" /></template>
        </ListRow>
      </ListCard>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.tools {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-4);
}
.search {
  flex: 0 1 320px;
}
@media (max-width: 899px) {
  .search {
    flex-basis: 100%;
  }
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.name {
  display: block;
  color: var(--ink);
  font: 600 14px/20px var(--font-sans);
  text-decoration: none;
}
.name:hover {
  text-decoration: underline;
}
.goal,
.reason {
  display: block;
  color: var(--ink-3);
  font: var(--text-body-s);
}
</style>
