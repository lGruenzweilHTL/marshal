<script setup lang="ts">
import { Activity } from 'lucide-vue-next'
import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { satelliteName } from '../format'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { usePagedLoad } from '../api/usePagedLoad'
import { useFormat } from '../i18n/locale'
import { useLiveEvent } from '../live/liveEvents'
import { AppButton, Badge, Chip, DataTable, ErrorBanner, Field, ListCard, LoadBoundary, Page, PageHeader, useDesktop, type Tone } from '../ui'
import RunRow from './RunRow.vue'
import TaintFlag from './TaintFlag.vue'

const sources: Schemas['SessionSource'][] = ['voice', 'web', 'task', 'api']
const outcomes: Schemas['RunOutcome'][] = ['ok', 'error', 'backgrounded', 'cancelled']
const tones: Record<Schemas['RunOutcome'], Tone> = { ok: 'success', error: 'danger', backgrounded: 'warning', cancelled: 'neutral' }

/** Every run, newest first; source, outcome and minimum cost filter on the server and live in the query. */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const format = useFormat()
const { t } = useI18n()

const text = (key: string) => (typeof route.query[key] === 'string' ? (route.query[key] as string) : '')
const source = computed(() => text('source'))
const outcome = computed(() => text('outcome'))
const sort = computed(() => text('sort'))
const minCost = shallowRef(text('minCost'))

const runs = usePagedLoad(
  (before, limit) =>
    api.call((client) =>
      client.GET('/api/v1/runs', {
        params: {
          query: {
            source: source.value || undefined,
            outcome: outcome.value || undefined,
            minCost: text('minCost') ? Number(text('minCost')) : undefined,
            sort: sort.value || undefined,
            // Paging by start time does not apply to the latency order.
            before: sort.value ? undefined : before,
            limit,
          },
        },
      }),
    ),
  (run) => run.startedAt,
)
watch(
  () => route.query,
  () => {
    minCost.value = text('minCost')
    runs.load.retry()
  },
)
useLiveEvent('runs', 'run.completed', () => runs.load.refresh())

function apply(change: Record<string, string>) {
  const query: Record<string, string> = { source: source.value, outcome: outcome.value, minCost: text('minCost'), sort: sort.value, ...change }
  void router.replace({ query: Object.fromEntries(Object.entries(query).filter(([, value]) => value)) })
}

const columns = computed(() => [
  { key: 'started', label: t('runs.columns.started') },
  { key: 'source', label: t('runs.columns.source') },
  { key: 'trigger', label: t('runs.columns.trigger') },
  { key: 'outcome', label: t('runs.columns.outcome') },
  { key: 'firstDelta', label: t('runs.columns.firstDelta'), align: 'right' as const },
  { key: 'cost', label: t('runs.columns.cost'), align: 'right' as const },
])
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('runs.title')" :subtitle="desktop ? t('runs.subtitle') : undefined" />
    <div class="tools">
      <div class="chips" role="group" :aria-label="t('runs.sources')">
        <Chip :label="t('runs.all')" :model-value="!source" data-source="" @update:model-value="apply({ source: '' })" />
        <Chip v-for="value in sources" :key="value" :label="t(`runs.source.${value}`)" :model-value="source === value" :data-source="value" @update:model-value="apply({ source: source === value ? '' : value })" />
      </div>
      <div class="chips" role="group" :aria-label="t('runs.outcomes')">
        <Chip
          v-for="value in outcomes"
          :key="value"
          :label="t(`runs.outcome.${value}`)"
          :model-value="outcome === value"
          :data-outcome="value"
          @update:model-value="apply({ outcome: outcome === value ? '' : value })"
        />
      </div>
      <Chip :label="t('runs.slowestFirst')" :model-value="sort === 'timeToFirstDelta'" data-sort="timeToFirstDelta" @update:model-value="apply({ sort: sort ? '' : 'timeToFirstDelta' })" />
      <Field v-model="minCost" kind="number" name="minCost" :label="t('runs.minCost')" class="cost" @change="apply({ minCost: String(minCost ?? '') })" />
    </div>

    <LoadBoundary :load="runs.load" size="page" :empty-title="t('runs.empty.title')" :empty-body="t('runs.empty.body')" :empty-icon="Activity">
      <DataTable v-if="desktop" :columns="columns" :rows="runs.items.value" :row-key="(run) => run.id" :label="t('runs.title')" row-attribute="run">
        <template #cell-started="{ row }">
          <RouterLink :to="`/runs/${row.id}`" class="link num">{{ format.dateTime(row.startedAt) }}</RouterLink>
        </template>
        <template #cell-source="{ row }">{{ [t(`runs.source.${row.source}`), row.satelliteId && satelliteName(row.satelliteId)].filter(Boolean).join(' · ') }}</template>
        <template #cell-trigger="{ row }">
          <span class="trigger">{{ row.trigger }}<TaintFlag v-if="row.tainted" /></span>
        </template>
        <template #cell-outcome="{ row }">
          <Badge :tone="tones[row.outcome]" :label="t(`runs.outcome.${row.outcome}`)" />
          <span v-if="row.outcomeReason" class="reason">{{ row.outcomeReason }}</span>
        </template>
        <template #cell-firstDelta="{ row }">
          <span class="num t-mono-s">{{ row.timeToFirstDeltaMs != null ? format.duration(row.timeToFirstDeltaMs) : '–' }}</span>
        </template>
        <template #cell-cost="{ row }"><span class="num t-mono-s">{{ format.cost(row.cost) }}</span></template>
      </DataTable>
      <ListCard v-else><RunRow v-for="run in runs.items.value" :key="run.id" :run="run" dated /></ListCard>
      <ErrorBanner v-if="runs.more.error.value" :error="runs.more.error.value" />
      <AppButton v-if="runs.hasMore.value && !sort" variant="ghost" block data-test="more" :loading="runs.more.busy.value" @click="runs.more.submit()">{{ t('runs.more') }}</AppButton>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.tools {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-4);
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
}
.cost {
  width: 140px;
}
.link {
  color: var(--ink);
  font-weight: 600;
  text-decoration: none;
}
.link:hover {
  text-decoration: underline;
}
.trigger {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}
.reason {
  display: block;
  color: var(--ink-3);
  font: var(--text-body-s);
}
</style>
