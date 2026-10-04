<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatCost, formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'

const session = useApiSession()
const runs = ref<Schemas['RunSummary'][]>([])

async function load() {
  const { data } = await session.raw.GET('/api/v1/runs', { params: { query: { limit: 5 } } })
  if (data) runs.value = data
}

onMounted(load)
useLiveEvent('runs', 'run.completed', () => void load())
</script>

<template>
  <h2><RouterLink to="/runs">Recent runs</RouterLink></h2>
  <ul class="plain">
    <li v-for="run in runs" :key="run.id">
      <RouterLink :to="`/runs/${run.id}`">{{ formatDateTime(run.startedAt) }}</RouterLink>
      {{ run.source }} · {{ run.trigger }} · {{ run.outcome }} · {{ formatCost(run.cost) }}
    </li>
  </ul>
  <p v-if="runs.length === 0" class="muted">No runs yet.</p>
</template>
