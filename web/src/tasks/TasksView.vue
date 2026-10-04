<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'
import KillSwitch from './KillSwitch.vue'
import { budgetUse, describeTrigger, taskLabel } from './taskFormat'

const statuses = ['active', 'all', 'pending', 'running', 'waiting', 'done', 'failed', 'cancelled'] as const

const session = useApiSession()
const route = useRoute()
const router = useRouter()
const tasks = ref<Schemas['TaskItem'][]>([])
const status = ref<string>('active')
const error = ref<string | null>(null)

async function load() {
  const { data } = await session.raw.GET('/api/v1/tasks', { params: { query: { status: status.value === 'all' ? undefined : status.value } } })
  if (!data) {
    error.value = 'The tasks could not be loaded.'
    return
  }
  error.value = null
  tasks.value = data
}

function applyFilter() {
  void router.push({ query: status.value === 'active' ? {} : { status: status.value } })
}

watch(
  () => route.query.status,
  (value) => {
    status.value = typeof value === 'string' ? value : 'active'
    void load()
  },
  { immediate: true },
)

useLiveEvent('tasks', 'task.changed', () => void load())
</script>

<template>
  <section class="page">
    <h1>Tasks</h1>
    <KillSwitch @changed="load" />
    <form class="filters" @submit.prevent>
      <label
        >Status
        <select v-model="status" name="status" @change="applyFilter">
          <option v-for="option in statuses" :key="option" :value="option">{{ option }}</option>
        </select>
      </label>
    </form>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <div class="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Task</th>
            <th>Status</th>
            <th>Trigger</th>
            <th>Next run</th>
            <th>Cost / budget</th>
            <th>Latest result</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="task in tasks" :key="task.id">
            <td>
              <RouterLink :to="`/tasks/${task.id}`">{{ taskLabel(task) }}</RouterLink>
            </td>
            <td>
              <span :class="['badge', task.status === 'failed' ? 'unhealthy' : 'healthy']">{{ task.status }}</span>
              <span v-if="task.statusReason" class="muted"> {{ task.statusReason }}</span>
            </td>
            <td><code>{{ describeTrigger(task.trigger) }}</code></td>
            <td>{{ task.nextRunAt ? formatDateTime(task.nextRunAt) : '–' }}</td>
            <td class="number">{{ budgetUse(task) }}</td>
            <td>{{ task.resultSpeech ?? '–' }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-if="tasks.length === 0 && !error">No tasks.</p>
  </section>
</template>
