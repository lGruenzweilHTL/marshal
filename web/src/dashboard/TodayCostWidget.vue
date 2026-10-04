<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatCost } from '../format'
import { useLiveEvent } from '../live/liveEvents'

const session = useApiSession()
const today = ref<Schemas['DailyCost'] | null>(null)

/** The cost report's default range ends today in the user's time zone. */
async function load() {
  const { data } = await session.raw.GET('/api/v1/cost')
  today.value = data?.days[data.days.length - 1] ?? null
}

onMounted(load)
useLiveEvent('runs', 'run.completed', () => void load())
</script>

<template>
  <h2><RouterLink to="/cost">Today’s cost</RouterLink></h2>
  <p v-if="today" class="figure">{{ formatCost(today.total) }}</p>
  <p v-if="today && today.advisor > 0" class="muted">incl. advisor {{ formatCost(today.advisor) }}</p>
</template>
