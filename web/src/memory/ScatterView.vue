<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { chunkTypes, typeColors } from './memoryLabels'

const size = 100
const session = useApiSession()
const router = useRouter()
const points = ref<Schemas['MemoryScatterPoint'][] | null>(null)
const error = ref<string | null>(null)

// Scaled into a 100 × 100 box; the y axis points up.
const plotted = computed(() => {
  if (!points.value?.length) return []
  const xs = points.value.map((point) => point.x)
  const ys = points.value.map((point) => point.y)
  const spanX = Math.max(...xs) - Math.min(...xs) || 1
  const spanY = Math.max(...ys) - Math.min(...ys) || 1
  return points.value.map((point) => ({
    ...point,
    cx: 5 + ((point.x - Math.min(...xs)) / spanX) * (size - 10),
    cy: size - 5 - ((point.y - Math.min(...ys)) / spanY) * (size - 10),
  }))
})

onMounted(async () => {
  const { data } = await session.raw.GET('/api/v1/memory/scatter')
  points.value = data ?? null
  error.value = data ? null : 'The scatter could not be loaded.'
})
</script>

<template>
  <section class="page">
    <h1>Memory scatter</h1>
    <p class="muted">Embeddings projected onto their first two principal components: nearby points mean similar memory.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="points && !points.length" class="muted">No memory yet.</p>
    <template v-if="plotted.length">
      <ul class="legend plain">
        <li v-for="type in chunkTypes" :key="type"><span class="swatch" :style="{ background: typeColors[type] }"></span>{{ type }}</li>
      </ul>
      <svg class="map" :viewBox="`0 0 ${size} ${size}`" role="img" aria-label="Memory embeddings in two dimensions">
        <circle
          v-for="point in plotted"
          :key="point.id"
          :data-point="point.id"
          :data-type="point.type"
          class="point"
          tabindex="0"
          role="link"
          :cx="point.cx"
          :cy="point.cy"
          r="1.5"
          :fill="typeColors[point.type]"
          @click="router.push(`/memory/chunks/${point.id}`)"
          @keydown.enter="router.push(`/memory/chunks/${point.id}`)"
        >
          <title>{{ point.title }}</title>
        </circle>
      </svg>
    </template>
  </section>
</template>

<style scoped>
.map {
  display: block;
  width: 100%;
  max-width: 40rem;
  border: 1px solid var(--border);
}

.point {
  cursor: pointer;
}

.legend {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem 1rem;
  margin-bottom: 0.5rem;
}

.swatch {
  display: inline-block;
  width: 0.75rem;
  height: 0.75rem;
  margin-right: 0.25rem;
  border-radius: 50%;
  vertical-align: middle;
}
</style>
