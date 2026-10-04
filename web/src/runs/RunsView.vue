<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter, type LocationQuery } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatCost, formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'

const pageSize = 50
const sources: Schemas['SessionSource'][] = ['voice', 'web', 'task', 'api']
const outcomes: Schemas['RunOutcome'][] = ['ok', 'error', 'backgrounded', 'cancelled']

const session = useApiSession()
const route = useRoute()
const router = useRouter()
const runs = ref<Schemas['RunSummary'][]>([])
const hasMore = ref(false)
const error = ref<string | null>(null)
const filters = reactive({ source: '', outcome: '', minCost: '' })

function text(query: LocationQuery, key: string): string {
  const value = query[key]
  return typeof value === 'string' ? value : ''
}

async function load(before?: string) {
  const { data } = await session.raw.GET('/api/v1/runs', {
    params: {
      query: {
        source: filters.source || undefined,
        outcome: filters.outcome || undefined,
        minCost: filters.minCost ? Number(filters.minCost) : undefined,
        before,
        limit: pageSize,
      },
    },
  })
  if (!data) {
    error.value = 'The runs could not be loaded.'
    return
  }
  error.value = null
  runs.value = before ? [...runs.value, ...data] : data
  hasMore.value = data.length === pageSize
}

function applyFilters() {
  const query: Record<string, string> = {}
  for (const [key, value] of Object.entries(filters)) if (value) query[key] = value
  void router.push({ query })
}

function loadMore() {
  const last = runs.value[runs.value.length - 1]
  if (last) void load(last.startedAt)
}

watch(
  () => route.query,
  (query) => {
    filters.source = text(query, 'source')
    filters.outcome = text(query, 'outcome')
    filters.minCost = text(query, 'minCost')
    void load()
  },
  { immediate: true },
)

useLiveEvent('runs', 'run.completed', () => void load())
</script>

<template>
  <section class="page">
    <h1>Runs</h1>
    <form class="filters" @submit.prevent="applyFilters">
      <label
        >Source
        <select v-model="filters.source" name="source">
          <option value="">All</option>
          <option v-for="source in sources" :key="source" :value="source">{{ source }}</option>
        </select>
      </label>
      <label
        >Outcome
        <select v-model="filters.outcome" name="outcome">
          <option value="">All</option>
          <option v-for="outcome in outcomes" :key="outcome" :value="outcome">{{ outcome }}</option>
        </select>
      </label>
      <label>Min. cost <input v-model="filters.minCost" name="minCost" type="number" min="0" step="any" inputmode="decimal" /></label>
      <button type="submit">Filter</button>
    </form>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <div class="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Started</th>
            <th>Source</th>
            <th>Trigger</th>
            <th>Outcome</th>
            <th>Cost</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in runs" :key="run.id">
            <td>
              <RouterLink :to="`/runs/${run.id}`">{{ formatDateTime(run.startedAt) }}</RouterLink>
            </td>
            <td>{{ run.source }}</td>
            <td>
              {{ run.trigger }} <span v-if="run.tainted" class="badge degraded">tainted</span>
            </td>
            <td>
              <span :class="['badge', run.outcome === 'error' ? 'unhealthy' : 'healthy']">{{ run.outcome }}</span>
            </td>
            <td class="number">{{ formatCost(run.cost) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-if="runs.length === 0 && !error">No runs yet.</p>
    <button v-if="hasMore" type="button" data-test="more" @click="loadMore">Load older runs</button>
  </section>
</template>
