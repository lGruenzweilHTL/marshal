<script setup lang="ts">
import { ScatterChart } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { LoadBoundary } from '../ui'
import MapLegend from './MapLegend.vue'

const size = 100

/** Chunk embeddings projected to two dimensions: nearby points mean similar memory; a point opens its chunk. */
const api = useApi()
const router = useRouter()
const { t } = useI18n()

const points = useLoad(() => api.call((client) => client.GET('/api/v1/memory/scatter')))
// Scaled into a 100 × 100 box; the y axis points up.
const plotted = computed(() => {
  const data = points.data.value ?? []
  if (!data.length) return []
  const xs = data.map((point) => point.x)
  const ys = data.map((point) => point.y)
  const spanX = Math.max(...xs) - Math.min(...xs) || 1
  const spanY = Math.max(...ys) - Math.min(...ys) || 1
  return data.map((point) => ({
    ...point,
    cx: 5 + ((point.x - Math.min(...xs)) / spanX) * (size - 10),
    cy: size - 5 - ((point.y - Math.min(...ys)) / spanY) * (size - 10),
  }))
})
const open = (id: string) => router.push(`/memory/chunks/${id}`)
</script>

<template>
  <LoadBoundary :load="points" size="page" :empty-title="t('memory.map.empty')" :empty-icon="ScatterChart">
    <div class="canvas">
      <svg class="map" :viewBox="`0 0 ${size} ${size}`" role="img" :aria-label="t('memory.map.scatterLabel')">
        <circle
          v-for="point in plotted"
          :key="point.id"
          :data-point="point.id"
          :data-type="point.type"
          class="point"
          :class="point.type"
          tabindex="0"
          role="link"
          :aria-label="point.title"
          :cx="point.cx"
          :cy="point.cy"
          r="1.5"
          @click="open(point.id)"
          @keydown.enter="open(point.id)"
        >
          <title>{{ point.title }}</title>
        </circle>
      </svg>
      <MapLegend class="legend" />
    </div>
    <p class="hint">{{ t('memory.map.scatterHint') }}</p>
  </LoadBoundary>
</template>

<style scoped>
.canvas {
  position: relative;
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
  overflow: hidden;
}
.map {
  display: block;
  width: 100%;
  max-height: 70vh;
  aspect-ratio: 1;
  margin: 0 auto;
}
.point {
  cursor: pointer;
  fill: var(--mem);
  fill-opacity: 0.85;
}
.point:hover,
.point:focus-visible {
  outline: none;
  stroke: var(--ink);
  stroke-width: 0.4;
}
.legend {
  position: absolute;
  left: var(--space-4);
  bottom: var(--space-4);
}
.hint {
  margin-top: var(--space-3);
  color: var(--ink-3);
  font: var(--text-body-s);
}
.fact {
  --mem: var(--mem-fact);
}
.preference {
  --mem: var(--mem-preference);
}
.event {
  --mem: var(--mem-event);
}
.procedure {
  --mem: var(--mem-procedure);
}
.person {
  --mem: var(--mem-person);
}
.device {
  --mem: var(--mem-device);
}
</style>
