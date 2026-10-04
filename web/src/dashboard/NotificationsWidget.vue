<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { formatDateTime } from '../format'
import { useLiveEvent } from '../live/liveEvents'

const session = useApiSession()
const notifications = ref<Schemas['NotificationEntry'][]>([])

async function load() {
  const { data } = await session.raw.GET('/api/v1/notifications', { params: { query: { limit: 5 } } })
  if (data) notifications.value = data
}

onMounted(load)
useLiveEvent('notifications', 'notification.created', () => void load())
useLiveEvent('notifications', 'notification.updated', () => void load())
</script>

<template>
  <h2><RouterLink to="/notifications">Recent notifications</RouterLink></h2>
  <ul class="plain">
    <li v-for="notification in notifications" :key="notification.id">
      {{ notification.content }}
      <span class="muted">· {{ notification.outcome }}<template v-if="notification.channel"> via {{ notification.channel }}</template> · {{ formatDateTime(notification.createdAt) }}</span>
    </li>
  </ul>
  <p v-if="notifications.length === 0" class="muted">No notifications yet.</p>
</template>
