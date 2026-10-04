<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useFormat } from '../i18n/locale'
import { ProgressBar } from '../ui'

/** Spent against a budget: numbers and a bar that turns warning at 70 % and danger at 90 %. */
const props = withDefaults(defineProps<{ spent: number; budget: number | null; label?: string; kind?: 'cost' | 'count' }>(), { label: undefined, kind: 'cost' })
const { t } = useI18n()
const format = useFormat()
const value = (amount: number) => (props.kind === 'cost' ? format.value.cost(amount) : format.value.number(amount))
const percent = computed(() => (props.budget ? Math.min(100, (props.spent / props.budget) * 100) : 0))
const tone = computed(() => (percent.value >= 90 ? 'danger' : percent.value >= 70 ? 'warning' : 'info'))
</script>

<template>
  <div class="meter">
    <span class="line">
      <span v-if="label" class="label">{{ label }}</span>
      <span class="num t-mono-s">{{ value(spent) }} / {{ budget === null ? t('tasks.unlimited') : value(budget) }}</span>
    </span>
    <ProgressBar v-if="budget !== null" :value="percent" :label="label ?? t('tasks.columns.spent')" :tone="tone" />
  </div>
</template>

<style scoped>
.meter {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 120px;
}
.line {
  display: flex;
  justify-content: space-between;
  gap: var(--space-2);
  color: var(--ink-2);
}
.label {
  font: var(--text-body-s);
}
</style>
