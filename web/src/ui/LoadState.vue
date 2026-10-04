<script setup lang="ts">
import { AlertCircle, Inbox, Plus, RefreshCw, WifiOff } from 'lucide-vue-next'
import { computed, type Component } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ApiError } from '../api/errors'
import { errorMessage } from '../api/errorMessage'
import AppButton from './AppButton.vue'
import AppIcon from './AppIcon.vue'

/**
 * Loading skeleton, empty state or "could not load" with retry. Errors show their message in the interface language
 * and the trace id of a server error.
 */
const props = withDefaults(
  defineProps<{
    state: 'loading' | 'empty' | 'error'
    title?: string
    body?: string
    error?: ApiError | null
    icon?: Component
    size?: 'card' | 'page'
    /** Label of the empty state's action (emits `action`). */
    actionLabel?: string
  }>(),
  { title: undefined, body: undefined, error: null, icon: undefined, size: 'card', actionLabel: undefined },
)
const emit = defineEmits<{ retry: []; action: [] }>()
const i18n = useI18n()
const { t } = i18n
const heading = computed(() => props.title ?? (props.state === 'error' ? t('states.couldNotLoad') : props.state === 'empty' ? t('states.empty') : t('states.loading')))
const text = computed(() => (props.state === 'error' && props.error ? errorMessage(props.error, i18n) : props.body))
const trace = computed(() => (props.error?.kind === 'problem' ? props.error.traceId : undefined))
const tileIcon = computed(() => (props.state === 'error' ? (props.error?.kind === 'network' ? WifiOff : AlertCircle) : (props.icon ?? Inbox)))
const rows = [
  ['58%', '34%'],
  ['72%', '28%'],
  ['46%', '38%'],
]
</script>

<template>
  <div class="load-state" :class="[size, state]" :aria-busy="state === 'loading' || undefined">
    <div v-if="state === 'loading'" class="skeleton" role="status">
      <span class="sr-only">{{ heading }}</span>
      <div v-for="(row, index) in rows" :key="index" class="skeleton-row">
        <span class="block tile" />
        <span class="lines"><span class="block" :style="{ width: row[0] }" /><span class="block short" :style="{ width: row[1] }" /></span>
      </div>
    </div>
    <div v-else class="message" :role="state === 'error' ? 'alert' : 'status'">
      <span class="tile-icon"><AppIcon :icon="tileIcon" :size="24" /></span>
      <span class="title">{{ heading }}</span>
      <span v-if="text" class="body">{{ text }}</span>
      <span v-if="trace" class="trace">{{ t('states.traceId', { id: trace }) }}</span>
      <AppButton v-if="state === 'error'" class="cta" :icon="RefreshCw" data-test="retry" @click="emit('retry')">{{ t('actions.retry') }}</AppButton>
      <AppButton v-else-if="actionLabel" class="cta" :icon="Plus" @click="emit('action')">{{ actionLabel }}</AppButton>
      <slot />
    </div>
  </div>
</template>

<style scoped>
.load-state {
  width: 100%;
}
.message {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: var(--space-8) var(--space-5);
  text-align: center;
}
.page .message {
  padding: var(--space-16) var(--space-6);
}
.tile-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  margin-bottom: var(--space-1);
  border-radius: var(--radius-lg);
  background: var(--surface-3);
  color: var(--ink-2);
}
.page .tile-icon {
  width: 56px;
  height: 56px;
}
.error .tile-icon {
  background: var(--danger-soft);
  color: var(--danger);
}
.title {
  font: var(--text-title-3);
}
.body {
  max-width: 360px;
  font: 400 14px/20px var(--font-sans);
  color: var(--ink-2);
  text-wrap: pretty;
}
.trace {
  font: var(--text-mono-s);
  color: var(--ink-3);
}
.cta {
  margin-top: 6px;
}
.skeleton {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 6px var(--space-4);
}
.page .skeleton {
  padding: var(--space-4) var(--space-6);
}
.skeleton-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: 10px 0;
}
.lines {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  flex-grow: 1;
}
.block {
  display: block;
  height: 12px;
  border-radius: var(--radius-xs);
  background: var(--surface-3);
  animation: pulse 1.4s ease-in-out infinite;
}
.block.short {
  height: 10px;
  opacity: 0.7;
}
.block.tile {
  width: 36px;
  height: 36px;
  flex-shrink: 0;
  border-radius: var(--radius-md);
}
@keyframes pulse {
  50% {
    opacity: 0.55;
  }
}
@media (prefers-reduced-motion: reduce) {
  .block {
    animation: none;
  }
}
</style>
