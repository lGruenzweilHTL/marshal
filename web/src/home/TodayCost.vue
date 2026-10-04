<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'

/** Today's cost large, with the advisor share and bars for the last days (today highlighted). */
const props = withDefaults(defineProps<{ report: Schemas['CostReport']; days?: number }>(), { days: 14 })
const { t } = useI18n()
const format = useFormat()

const recent = computed(() => props.report.days.slice(-props.days))
const today = computed(() => recent.value.at(-1) ?? null)
const max = computed(() => Math.max(...recent.value.map((day) => day.total), 0))
const height = (total: number) => `${max.value > 0 ? Math.max(6, (total / max.value) * 100) : 6}%`
</script>

<template>
  <div class="today">
    <div class="figure">
      <span class="t-overline">{{ t('home.today') }}</span>
      <span class="big num">{{ format.cost(today?.total ?? 0) }}</span>
      <span v-if="today?.advisor" class="meta num">{{ t('home.advisor', { cost: format.cost(today.advisor) }) }}</span>
    </div>
    <div class="chart">
      <div class="bars" aria-hidden="true">
        <span v-for="(day, index) in recent" :key="day.date" class="bar" :class="{ current: index === recent.length - 1 }" :style="{ height: height(day.total) }" />
      </div>
      <span class="meta">{{ t('home.lastDays', { count: recent.length }) }}</span>
    </div>
  </div>
</template>

<style scoped>
.today {
  display: flex;
  align-items: flex-end;
  gap: var(--space-4);
  padding: 18px;
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
.figure {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}
.big {
  font: 650 40px/44px var(--font-display);
  letter-spacing: -0.03em;
}
.meta {
  color: var(--ink-3);
  font: 400 13px/18px var(--font-sans);
}
.chart {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
  align-items: flex-end;
  gap: 6px;
}
.bars {
  display: flex;
  align-items: flex-end;
  gap: 4px;
  height: 56px;
}
.bar {
  width: 7px;
  border-radius: 3px;
  background: var(--surface-3);
}
.bar.current {
  background: var(--accent);
}
</style>
