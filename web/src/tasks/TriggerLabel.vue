<script setup lang="ts">
import { CalendarClock, Clock, Eye, Play, Timer } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { AppIcon } from '../ui'

/** A task trigger: an icon for its kind, the spec as written (cron expression, time, watched event) and a marker for once-watches. */
defineProps<{ trigger: Schemas['TaskTrigger'] }>()
const { t } = useI18n()
const icons = { now: Play, at: CalendarClock, cron: Clock, event: Eye, after: Timer }
</script>

<template>
  <span class="trigger" :title="t(`tasks.trigger.${trigger.kind}`)">
    <AppIcon :icon="icons[trigger.kind]" :size="16" />
    <span class="t-mono-s spec">{{ trigger.spec }}</span>
    <span v-if="trigger.kind === 'event' && trigger.once" class="once">{{ t('tasks.trigger.once') }}</span>
  </span>
</template>

<style scoped>
.trigger {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  min-width: 0;
  color: var(--ink-3);
}
.spec {
  overflow: hidden;
  color: var(--ink-2);
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 280px;
}
.once {
  flex: none;
  color: var(--ink-3);
  font: 600 12px/1 var(--font-sans);
}
</style>
