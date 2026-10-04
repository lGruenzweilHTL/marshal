<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatCost } from '../format'

const session = useApiSession()
const report = ref<Schemas['CostReport'] | null>(null)
const error = ref<string | null>(null)
const range = reactive({ from: '', to: '' })

/** Non-advisor tiers seen in the range, in a stable order. */
const tiers = computed(() => Object.keys(report.value?.byTier ?? {}).sort())

async function load() {
  const { data, error: problem } = await session.raw.GET('/api/v1/cost', {
    params: { query: { from: range.from || undefined, to: range.to || undefined } },
  })
  if (!data) {
    error.value = (problem as { detail?: string } | undefined)?.detail ?? 'The cost report could not be loaded.'
    return
  }
  error.value = null
  report.value = data
  range.from = data.from
  range.to = data.to
}

onMounted(load)
</script>

<template>
  <section class="page">
    <h1>Cost</h1>
    <form class="filters" @submit.prevent="load">
      <label>From <input v-model="range.from" name="from" type="date" /></label>
      <label>To <input v-model="range.to" name="to" type="date" /></label>
      <button type="submit">Show</button>
    </form>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <template v-if="report">
      <p class="muted">Days in {{ report.timeZone }}. Cost per turn is listed under Runs.</p>
      <div class="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Day</th>
              <th v-for="tier in tiers" :key="tier">{{ tier }}</th>
              <th>Advisor</th>
              <th>Total</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="day in report.days" :key="day.date" :data-day="day.date">
              <td>{{ day.date }}</td>
              <td v-for="tier in tiers" :key="tier" class="number">{{ formatCost(day.byTier[tier] ?? 0) }}</td>
              <td class="number">{{ formatCost(day.advisor) }}</td>
              <td class="number">{{ formatCost(day.total) }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td>Total</td>
              <td v-for="tier in tiers" :key="tier" class="number">{{ formatCost(report.byTier[tier] ?? 0) }}</td>
              <td class="number">{{ formatCost(report.advisor) }}</td>
              <td class="number">{{ formatCost(report.total) }}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </template>
  </section>
</template>
