<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'

/** Stacked bars per day: tiers bottom up, the advisor on top; the last day is today. */
const props = defineProps<{ days: Schemas['DailyCost'][]; tiers: string[] }>()
const { t } = useI18n()
const format = useFormat()

const max = computed(() => Math.max(...props.days.map((day) => day.total), 0))
const share = (value: number) => (max.value > 0 ? `${(value / max.value) * 100}%` : '0%')
const label = (date: string) => format.value.date(`${date}T12:00:00Z`)
const dayOfMonth = (date: string) => String(Number(date.slice(8, 10)))
const tierClass = (index: number) => `tier-${Math.min(index, 2)}`
</script>

<template>
  <div class="chart" role="img" :aria-label="t('runs.cost.chart')">
    <div v-for="(day, index) in days" :key="day.date" class="column" :title="`${label(day.date)}: ${format.cost(day.total)}`">
      <div class="bar">
        <span class="segment advisor" :style="{ height: share(day.advisor) }" />
        <span v-for="(tier, tierIndex) in [...tiers].reverse()" :key="tier" class="segment" :class="tierClass(tiers.length - 1 - tierIndex)" :style="{ height: share(day.byTier[tier] ?? 0) }" />
      </div>
      <span class="day num" :class="{ today: index === days.length - 1 }">{{ dayOfMonth(day.date) }}</span>
    </div>
  </div>
</template>

<style scoped>
.chart {
  display: flex;
  align-items: stretch;
  gap: 4px;
  height: 220px;
}
.column {
  display: flex;
  flex: 1;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.bar {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  justify-content: flex-end;
  gap: 1px;
  width: 100%;
  max-width: 28px;
}
.segment {
  display: block;
  min-height: 0;
}
.segment:first-child {
  border-radius: 4px 4px 0 0;
}
.segment:last-child {
  border-radius: 0 0 4px 4px;
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
.day {
  color: var(--ink-3);
  font: 500 11px/14px var(--font-sans);
}
.day.today {
  color: var(--ink);
  font-weight: 700;
}
</style>
