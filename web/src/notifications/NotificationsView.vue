<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'

const pageSize = 50
const outcomes: Schemas['NotificationOutcome'][] = ['pending', 'delivered', 'coalesced', 'held', 'inboxed', 'expired', 'fallback']

const session = useApi()
const route = useRoute()
const router = useRouter()
const notifications = ref<Schemas['NotificationEntry'][]>([])
const outcome = ref('')
const hasMore = ref(false)
const error = ref<string | null>(null)

async function load(before?: string) {
  const { data } = await session.api.GET('/api/v1/notifications', {
    params: { query: { outcome: outcome.value || undefined, before, limit: pageSize } },
  })
  if (!data) {
    error.value = 'The notifications could not be loaded.'
    return
  }
  error.value = null
  notifications.value = before ? [...notifications.value, ...data] : data
  hasMore.value = data.length === pageSize
}

function applyFilter() {
  void router.push({ query: outcome.value ? { outcome: outcome.value } : {} })
}

function loadMore() {
  const last = notifications.value[notifications.value.length - 1]
  if (last) void load(last.createdAt)
}

watch(
  () => route.query.outcome,
  (value) => {
    outcome.value = typeof value === 'string' ? value : ''
    void load()
  },
  { immediate: true },
)

useLiveEvent('notifications', 'notification.created', () => void load())
useLiveEvent('notifications', 'notification.updated', () => void load())
</script>

<template>
  <section class="page">
    <h1>Notifications</h1>
    <form class="filters" @submit.prevent>
      <label
        >Outcome
        <select v-model="outcome" name="outcome" @change="applyFilter">
          <option value="">All</option>
          <option v-for="option in outcomes" :key="option" :value="option">{{ option }}</option>
        </select>
      </label>
    </form>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <div class="table-scroll">
      <table>
        <thead>
          <tr>
            <th>Sent</th>
            <th>Content</th>
            <th>Source</th>
            <th>Priority</th>
            <th>Channel</th>
            <th>Outcome</th>
            <th>Reason</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="notification in notifications" :key="notification.id">
            <td>{{ formatDateTime(notification.createdAt) }}</td>
            <td class="content">{{ notification.content }}</td>
            <td>
              <RouterLink v-if="notification.taskId" :to="`/tasks/${notification.taskId}`">{{ notification.source }}</RouterLink>
              <template v-else>{{ notification.source }}</template>
            </td>
            <td>
              <span :class="['badge', notification.priority === 'critical' ? 'unhealthy' : 'healthy']">{{ notification.priority }}</span>
            </td>
            <td>{{ notification.channel ?? '–' }}</td>
            <td>{{ notification.outcome }}</td>
            <td>{{ notification.reason ?? '' }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-if="notifications.length === 0 && !error">No notifications.</p>
    <button v-if="hasMore" type="button" data-test="more" @click="loadMore">Load older notifications</button>
  </section>
</template>

<style scoped>
.content {
  white-space: pre-wrap;
  min-width: 12rem;
}
</style>
