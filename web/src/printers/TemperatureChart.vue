<script setup lang="ts">
import { computed } from 'vue'

/** One reading per live update. */
export interface TemperatureSample {
  extruder: number
  bed: number
}

const props = defineProps<{ samples: TemperatureSample[] }>()

const width = 300
const height = 100

const scale = computed(() => Math.max(50, ...props.samples.flatMap((sample) => [sample.extruder, sample.bed])) * 1.1)

function points(pick: (sample: TemperatureSample) => number): string {
  const count = props.samples.length
  return props.samples
    .map((sample, index) => `${count === 1 ? width : (index / (count - 1)) * width},${height - (pick(sample) / scale.value) * height}`)
    .join(' ')
}
</script>

<template>
  <svg data-test="chart" :viewBox="`0 0 ${width} ${height}`" class="chart" role="img" aria-label="Temperature history">
    <template v-if="samples.length > 0">
      <polyline class="extruder" :points="points((sample) => sample.extruder)" />
      <polyline class="bed" :points="points((sample) => sample.bed)" />
    </template>
  </svg>
</template>

<style scoped>
.chart {
  width: 100%;
  height: 6rem;
  border: 1px solid var(--border);
  border-radius: 8px;
}

polyline {
  fill: none;
  stroke-width: 2;
  vector-effect: non-scaling-stroke;
}

.extruder {
  stroke: #e4572e;
}

.bed {
  stroke: #4c78a8;
}
</style>
