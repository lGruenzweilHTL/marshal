<script setup lang="ts">
import { Clock, Zap } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'
import { taskLabel } from '../tasks/taskFormat'
import { Badge, IconTile, ListCard, ListRow, LoadState } from '../ui'

/** Running tasks first, then the next scheduled runs. */
const props = defineProps<{ tasks: Schemas['TaskItem'][]; limit?: number }>()
const { t } = useI18n()
const format = useFormat()

const running = computed(() => props.tasks.filter((task) => task.status === 'running'))
const next = computed(() =>
  props.tasks
    .filter((task) => task.status !== 'running' && task.nextRunAt)
    .sort((a, b) => a.nextRunAt!.localeCompare(b.nextRunAt!))
    .slice(0, Math.max(0, (props.limit ?? 5) - running.value.length)),
)
</script>

<template>
  <ListCard v-if="running.length || next.length">
    <ListRow v-for="task in running" :key="task.id" :title="taskLabel(task)" :subtitle="task.goal" :to="`/tasks/${task.id}`">
      <template #lead><IconTile :icon="Zap" tone="info" /></template>
      <template #trail><Badge tone="info" dot :label="t('tasks.status.running')" /></template>
    </ListRow>
    <ListRow v-for="task in next" :key="task.id" :title="taskLabel(task)" :to="`/tasks/${task.id}`">
      <template #lead><IconTile :icon="Clock" /></template>
      <template #subtitle><span class="num">{{ t('home.next', { time: format.relative(task.nextRunAt!) }) }}</span></template>
    </ListRow>
  </ListCard>
  <LoadState v-else state="empty" :title="t('home.noTasks')" :icon="Clock" />
</template>
