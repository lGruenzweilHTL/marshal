<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatDateTime } from '../format'
import { chunkTypes } from './memoryLabels'

const session = useApiSession()
const route = useRoute()
const chunks = ref<Schemas['MemoryChunk'][] | null>(null)
const projects = ref<Schemas['ProjectView'][]>([])
const error = ref<string | null>(null)
// A project card links here with ?project=<id>.
const filter = reactive({ query: '', type: '', project: typeof route.query.project === 'string' ? route.query.project : '', pinned: false })
const projectNames = computed(() => new Map(projects.value.map((project) => [project.id, project.name])))

async function load() {
  const query = {
    query: filter.query.trim() || undefined,
    type: filter.type || undefined,
    projectId: filter.project || undefined,
    pinned: filter.pinned ? true : undefined,
  }
  const { data } = await session.raw.GET('/api/v1/memory/chunks', { params: { query } })
  chunks.value = data ?? null
  error.value = data ? null : 'The memory could not be loaded.'
}

onMounted(async () => {
  const { data } = await session.raw.GET('/api/v1/memory/projects')
  projects.value = data ?? []
  await load()
})
</script>

<template>
  <section class="page">
    <h1>Memory</h1>
    <form class="filters" data-test="search" role="search" @submit.prevent="load">
      <input v-model="filter.query" name="query" type="search" placeholder="Search memory" aria-label="Search memory" />
      <select v-model="filter.type" name="type" aria-label="Type">
        <option value="">All types</option>
        <option v-for="type in chunkTypes" :key="type" :value="type">{{ type }}</option>
      </select>
      <select v-model="filter.project" name="project" aria-label="Project">
        <option value="">All projects</option>
        <option v-for="project in projects" :key="project.id" :value="project.id">{{ project.name }}</option>
      </select>
      <label class="inline"><input v-model="filter.pinned" name="pinned" type="checkbox" /> Pinned only</label>
      <button type="submit">Search</button>
      <RouterLink to="/memory/new" class="button">New</RouterLink>
    </form>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-else-if="chunks && chunks.length === 0" class="muted">No memory matches.</p>
    <ul v-if="chunks" class="plain chunks">
      <li v-for="chunk in chunks" :key="chunk.id" :data-chunk="chunk.id">
        <RouterLink :to="`/memory/chunks/${chunk.id}`">{{ chunk.title }}</RouterLink>
        <span class="badge">{{ chunk.type }}</span>
        <span v-if="chunk.projectId" class="badge">{{ projectNames.get(chunk.projectId) ?? 'deleted project' }}</span>
        <span v-if="chunk.pinned" class="badge healthy">pinned</span>
        <div class="muted">{{ chunk.content }}</div>
        <div class="muted small">updated {{ formatDateTime(chunk.updatedAt) }} · importance {{ chunk.importance }}</div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 1rem;
}

.filters input[type='search'] {
  flex: 1 1 12rem;
  min-width: 0;
}

.chunks li {
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border);
  overflow-wrap: anywhere;
}

.small {
  font-size: 0.85rem;
}
</style>
