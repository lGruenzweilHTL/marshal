<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'
import { taskLabel } from '../tasks/taskFormat'

const session = useApiSession()
const tasks = ref<Schemas['TaskItem'][]>([])

const running = computed(() => tasks.value.filter((task) => task.status === 'running'))
const next = computed(() =>
  tasks.value
    .filter((task) => task.status !== 'running' && task.nextRunAt)
    .sort((a, b) => a.nextRunAt!.localeCompare(b.nextRunAt!))
    .slice(0, 5),
)

async function load() {
  const { data } = await session.raw.GET('/api/v1/tasks', { params: { query: { status: 'active' } } })
  if (data) tasks.value = data
}

onMounted(load)
useLiveEvent('tasks', 'task.changed', () => void load())
</script>

<template>
  <h2><RouterLink to="/tasks">Tasks</RouterLink></h2>
  <ul data-test="running" class="plain">
    <li v-for="task in running" :key="task.id">
      <RouterLink :to="`/tasks/${task.id}`">{{ taskLabel(task) }}</RouterLink> <span class="badge healthy">running</span>
    </li>
  </ul>
  <p v-if="running.length === 0" class="muted">Nothing running.</p>
  <h3>Next runs</h3>
  <ul data-test="next" class="plain">
    <li v-for="task in next" :key="task.id">
      <RouterLink :to="`/tasks/${task.id}`">{{ taskLabel(task) }}</RouterLink> {{ formatDateTime(task.nextRunAt!) }}
    </li>
  </ul>
  <p v-if="next.length === 0" class="muted">Nothing scheduled.</p>
</template>

<style scoped>
h3 {
  margin: 0.75rem 0 0.25rem;
  font-size: 0.9rem;
}
</style>
