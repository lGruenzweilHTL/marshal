<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'
import { IconTile, ListRow } from '../ui'
import { priorityLook } from './priority'

/** One notification in a list: priority as the icon, the text clamped to two lines, source, time and delivery. */
const props = defineProps<{ notification: Schemas['NotificationEntry'] }>()
const { t } = useI18n()
const format = useFormat()

const look = computed(() => priorityLook[props.notification.priority])
const delivery = computed(() =>
  [props.notification.channel, t(`inbox.notification.outcome.${props.notification.outcome}`)].filter(Boolean).join(' · '),
)
</script>

<template>
  <ListRow :to="`/inbox/notifications/${notification.id}`">
    <template #lead><IconTile :icon="look.icon" :tone="look.tone" /></template>
    <template #title><span class="text">{{ notification.content }}</span></template>
    <template #subtitle>
      <span class="t-mono-s">{{ notification.source }}</span> · <span class="num">{{ format.time(notification.createdAt) }}</span> · {{ delivery }}
    </template>
  </ListRow>
</template>

<style scoped>
.text {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  font-weight: 550;
}
</style>
