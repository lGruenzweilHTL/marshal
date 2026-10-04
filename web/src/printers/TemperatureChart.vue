<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

/** One reading per live update. */
export interface TemperatureSample {
  extruder: number
  bed: number
}

/** Hotend and bed temperature since the page opened, with guide lines at the current targets. */
const props = defineProps<{ samples: TemperatureSample[]; targets?: { extruder: number; bed: number } }>()
const { t } = useI18n()

const width = 680
const height = 180
const top = 16

const scale = computed(() => Math.max(50, props.targets?.extruder ?? 0, ...props.samples.flatMap((sample) => [sample.extruder, sample.bed])) * 1.06)
const y = (value: number) => height - (value / scale.value) * (height - top)

function path(pick: (sample: TemperatureSample) => number): string {
  const count = props.samples.length
  return props.samples.map((sample, index) => `${index === 0 ? 'M' : 'L'}${count === 1 ? width : (index / (count - 1)) * width},${y(pick(sample)).toFixed(1)}`).join(' ')
}
const guides = computed(() => [props.targets?.bed, props.targets?.extruder].filter((value): value is number => !!value))
</script>

<template>
  <svg data-test="chart" :viewBox="`0 0 ${width} ${height}`" class="chart" role="img" :aria-label="t('printers.temperature')" preserveAspectRatio="none">
    <g v-for="guide in guides" :key="guide">
      <line class="guide" x1="0" :x2="width" :y1="y(guide)" :y2="y(guide)" />
      <text class="label" :x="width - 4" :y="y(guide) - 4" text-anchor="end">{{ Math.round(guide) }}°</text>
    </g>
    <template v-if="samples.length > 0">
      <path class="extruder" :d="path((sample) => sample.extruder)" />
      <path class="bed" :d="path((sample) => sample.bed)" />
    </template>
  </svg>
</template>

<style scoped>
.chart {
  display: block;
  width: 100%;
  height: 160px;
}
.guide {
  stroke: var(--line);
  stroke-dasharray: 3 4;
  vector-effect: non-scaling-stroke;
}
.label {
  fill: var(--ink-3);
  font: 450 11px var(--font-mono);
}
path {
  fill: none;
  stroke-width: 2.25;
  vector-effect: non-scaling-stroke;
}
.extruder {
  stroke: var(--accent);
}
.bed {
  stroke: var(--info);
}
</style>
