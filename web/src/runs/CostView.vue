<script setup lang="ts">
import { computed, reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useFormat, useLocale } from '../i18n/locale'
import { AppButton, Card, DataTable, Field, ListCard, ListRow, LoadBoundary, Page, PageHeader, Segmented, useDesktop } from '../ui'
import CostChart from './CostChart.vue'

type Preset = '14' | '30' | '90' | 'custom'

/** Model spend per day and tier in the owner's time zone, with the advisor apart; presets or a custom range. */
const api = useApi()
const locale = useLocale()
const desktop = useDesktop()
const format = useFormat()
const { t, te } = useI18n()
/** Interface label of a model tier; tiers without one are shown as configured. */
const tierLabel = (tier: string) => (te(`runs.tier.${tier}`) ? t(`runs.tier.${tier}`) : tier)

const preset = shallowRef<Preset>('14')
const custom = reactive({ from: '', to: '' })

/** Today in the owner's time zone, as `YYYY-MM-DD`. */
function today(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: locale.timeZone.value, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
}
function daysBefore(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number]
  return new Date(Date.UTC(year, month - 1, day - days)).toISOString().slice(0, 10)
}
function range(): { from?: string; to?: string } {
  if (preset.value === 'custom') return { from: custom.from || undefined, to: custom.to || undefined }
  const to = today()
  return { from: daysBefore(to, Number(preset.value) - 1), to }
}

// The first load uses the server's default range (the last 14 days); later ones ask for the chosen range.
let touched = false
const report = useLoad(() => api.call((client) => client.GET('/api/v1/cost', { params: { query: preset.value === '14' && !touched ? {} : range() } })), {
  isEmpty: () => false,
})
function choose(value: Preset) {
  touched = true
  preset.value = value
  if (value !== 'custom') report.retry()
}
function showCustom() {
  touched = true
  preset.value = 'custom'
  report.retry()
}

const presets = computed(() => (['14', '30', '90'] as const).map((value) => ({ value, label: t(`runs.cost.presets.${value}`) })))
const presetModel = computed({ get: () => (preset.value === 'custom' ? '14' : preset.value), set: (value: Exclude<Preset, 'custom'>) => choose(value) })

const tiers = computed(() => Object.keys(report.data.value?.byTier ?? {}).sort())
const days = computed(() => report.data.value?.days ?? [])
const newestFirst = computed(() => [...days.value].reverse())
const last = computed(() => days.value.at(-1) ?? null)
const average = computed(() => (days.value.length ? (report.data.value?.total ?? 0) / days.value.length : 0))
const advisorShare = computed(() => (report.data.value?.total ? report.data.value.advisor / report.data.value.total : 0))
const dayLabel = (date: string) => format.value.date(`${date}T12:00:00Z`)

const columns = computed(() => [
  { key: 'date', label: t('runs.cost.day') },
  ...tiers.value.map((tier) => ({ key: `tier:${tier}`, label: tierLabel(tier), align: 'right' as const })),
  { key: 'advisor', label: t('runs.cost.advisor'), align: 'right' as const },
  { key: 'total', label: t('runs.cost.total'), align: 'right' as const },
])
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('runs.cost.title')">
      <template #actions>
        <Segmented v-model="presetModel" :options="presets" :label="t('runs.cost.range')" />
      </template>
    </PageHeader>

    <form class="custom" @submit.prevent="showCustom">
      <Field v-model="custom.from" kind="date" name="from" :label="t('runs.cost.from')" />
      <Field v-model="custom.to" kind="date" name="to" :label="t('runs.cost.to')" />
      <AppButton type="submit">{{ t('runs.cost.show') }}</AppButton>
    </form>

    <LoadBoundary :load="report" size="page">
      <template v-if="report.data.value">
        <p class="zone">{{ t('runs.cost.zone', { zone: report.data.value.timeZone }) }}</p>
        <div class="summary" data-test="summary">
          <div class="tile">
            <span class="t-overline">{{ t('runs.cost.today') }}</span>
            <span class="big num">{{ format.cost(last?.total ?? 0) }}</span>
            <span class="meta num">{{ t('runs.cost.advisorIncl', { cost: format.cost(last?.advisor ?? 0) }) }}</span>
          </div>
          <div class="tile">
            <span class="t-overline">{{ t('runs.cost.total') }}</span>
            <span class="big num">{{ format.cost(report.data.value.total) }}</span>
            <span class="meta num">{{ t('runs.cost.perDayAvg', { cost: format.cost(average) }) }}</span>
          </div>
          <div class="tile">
            <span class="t-overline">{{ t('runs.cost.advisor') }}</span>
            <span class="big num">{{ format.cost(report.data.value.advisor) }}</span>
            <span class="meta num">{{ t('runs.cost.advisorShare', { share: format.percent(advisorShare) }) }}</span>
          </div>
        </div>

        <Card>
          <div class="chart-head">
            <h2 class="t-title-3">{{ t('runs.cost.perDay') }}</h2>
            <span class="legend">
              <span v-for="(tier, index) in tiers" :key="tier" class="key"><span class="swatch" :class="`tier-${Math.min(index, 2)}`" />{{ tierLabel(tier) }}</span>
              <span class="key"><span class="swatch advisor" />{{ t('runs.cost.advisor') }}</span>
            </span>
          </div>
          <CostChart :days="days" :tiers="tiers" />
        </Card>

        <DataTable v-if="desktop" :columns="columns" :rows="newestFirst" :row-key="(day) => day.date" :label="t('runs.cost.perDay')" row-attribute="day">
          <template v-for="tier in tiers" :key="tier" #[`cell-tier:${tier}`]="{ row }"><span class="num t-mono-s">{{ format.cost(row.byTier[tier] ?? 0) }}</span></template>
          <template #cell-date="{ row }">{{ dayLabel(row.date) }}</template>
          <template #cell-advisor="{ row }"><span class="num t-mono-s">{{ format.cost(row.advisor) }}</span></template>
          <template #cell-total="{ row }"><strong class="num t-mono-s">{{ format.cost(row.total) }}</strong></template>
          <template #foot>
            <tr>
              <td>{{ t('runs.cost.total') }}</td>
              <td v-for="tier in tiers" :key="tier" class="right"><span class="num t-mono-s">{{ format.cost(report.data.value.byTier[tier] ?? 0) }}</span></td>
              <td class="right"><span class="num t-mono-s">{{ format.cost(report.data.value.advisor) }}</span></td>
              <td class="right"><strong class="num t-mono-s">{{ format.cost(report.data.value.total) }}</strong></td>
            </tr>
          </template>
        </DataTable>
        <ListCard v-else>
          <ListRow
            v-for="day in newestFirst"
            :key="day.date"
            :data-day="day.date"
            :title="dayLabel(day.date)"
            :subtitle="[...tiers.map((tier) => `${tierLabel(tier)} ${format.cost(day.byTier[tier] ?? 0)}`), `${t('runs.cost.advisor')} ${format.cost(day.advisor)}`].join(' · ')"
            :chevron="false"
          >
            <template #trail><strong class="num t-mono-s">{{ format.cost(day.total) }}</strong></template>
          </ListRow>
        </ListCard>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.custom {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: var(--space-3);
}
.zone {
  margin-top: calc(-1 * var(--space-4));
  color: var(--ink-3);
  font: var(--text-body-s);
}
.summary {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--space-2);
}
@media (min-width: 900px) {
  .summary {
    gap: var(--space-4);
  }
}
.tile {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  padding: var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.big {
  font: 650 28px/32px var(--font-display);
  letter-spacing: -0.02em;
}
@media (min-width: 900px) {
  .big {
    font-size: 34px;
    line-height: 40px;
  }
}
.meta {
  color: var(--ink-3);
  font: var(--text-body-s);
}
.chart-head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  margin-bottom: var(--space-4);
}
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
  color: var(--ink-2);
  font: var(--text-body-s);
}
.key {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.swatch {
  width: 10px;
  height: 10px;
  border-radius: 3px;
}
.tier-0 {
  background: var(--chart-1);
}
.tier-1 {
  background: var(--chart-2);
}
.tier-2 {
  background: var(--ink-3);
}
.advisor {
  background: var(--chart-3);
}
.right {
  text-align: right;
}
</style>
