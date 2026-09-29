<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { formatDateTime } from '../format'

const session = useApi()
const report = ref<Schemas['MemoryAuditReport'] | null>(null)
const error = ref<string | null>(null)
const clean = computed(
  () => report.value && !report.value.stale.length && !report.value.contradictions.length && !report.value.orphans.length && !report.value.danglingEdges.length,
)

onMounted(async () => {
  const { data } = await session.api.GET('/api/v1/memory/audit')
  report.value = data ?? null
  error.value = data ? null : 'The audit could not be loaded.'
})
</script>

<template>
  <section class="page">
    <h1>Memory audit</h1>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="clean" class="muted">Nothing to review.</p>
    <template v-if="report && !clean">
      <div v-if="report.contradictions.length" data-section="contradictions">
        <h2>Contradictions</h2>
        <p class="muted">Resolve by editing, superseding or deleting one of the two.</p>
        <ul class="plain">
          <li v-for="pair in report.contradictions" :key="`${pair.from.id}/${pair.to.id}`">
            <RouterLink :to="`/memory/chunks/${pair.from.id}`">{{ pair.from.title }}</RouterLink>: {{ pair.from.content }}
            <div class="muted">contradicts</div>
            <RouterLink :to="`/memory/chunks/${pair.to.id}`">{{ pair.to.title }}</RouterLink>: {{ pair.to.content }}
          </li>
        </ul>
      </div>
      <div v-if="report.stale.length" data-section="stale">
        <h2>Stale</h2>
        <p class="muted">Neither updated nor used for a long time.</p>
        <ul class="plain">
          <li v-for="chunk in report.stale" :key="chunk.id">
            <RouterLink :to="`/memory/chunks/${chunk.id}`">{{ chunk.title }}</RouterLink>
            <span class="muted"> · updated {{ formatDateTime(chunk.updatedAt) }}</span>
          </li>
        </ul>
      </div>
      <div v-if="report.orphans.length" data-section="orphans">
        <h2>Orphans</h2>
        <p class="muted">Memory of deleted projects.</p>
        <ul class="plain">
          <li v-for="chunk in report.orphans" :key="chunk.id">
            <RouterLink :to="`/memory/chunks/${chunk.id}`">{{ chunk.title }}</RouterLink>
          </li>
        </ul>
      </div>
      <div v-if="report.danglingEdges.length" data-section="dangling">
        <h2>Links to deleted memory</h2>
        <ul class="plain">
          <li v-for="edge in report.danglingEdges" :key="`${edge.from}/${edge.kind}/${edge.to}`">
            <RouterLink :to="`/memory/chunks/${edge.from}`">{{ edge.from }}</RouterLink> {{ edge.kind }}
            <RouterLink :to="`/memory/chunks/${edge.to}`">{{ edge.to }}</RouterLink>
          </li>
        </ul>
      </div>
    </template>
  </section>
</template>

<style scoped>
li {
  padding: 0.5rem 0;
  overflow-wrap: anywhere;
}
</style>
