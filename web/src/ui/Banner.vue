<script setup lang="ts">
import { AlertCircle, AlertTriangle, Check, Info, WifiOff, X } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import AppIcon from './AppIcon.vue'

export type BannerTone = 'info' | 'success' | 'warning' | 'danger' | 'offline'

const props = withDefaults(defineProps<{ tone?: BannerTone; title: string; body?: string; trace?: string; dismissible?: boolean }>(), {
  tone: 'danger',
  body: undefined,
  trace: undefined,
  dismissible: false,
})
const emit = defineEmits<{ dismiss: [] }>()
const { t } = useI18n()
const icon = computed(() => ({ info: Info, success: Check, warning: AlertTriangle, danger: AlertCircle, offline: WifiOff })[props.tone])
</script>

<template>
  <div class="banner" :class="tone" :role="tone === 'danger' ? 'alert' : 'status'">
    <AppIcon :icon="icon" :stroke="2" class="icon" />
    <span class="text">
      <span class="title">{{ title }}</span>
      <span v-if="body || $slots.default" class="body"><slot>{{ body }}</slot></span>
      <span v-if="trace" class="trace">{{ t('states.traceId', { id: trace }) }}</span>
    </span>
    <span v-if="$slots.action" class="action"><slot name="action" /></span>
    <button v-if="dismissible" type="button" class="dismiss" data-test="dismiss" :aria-label="t('actions.dismiss')" @click="emit('dismiss')">
      <AppIcon :icon="X" :size="18" />
    </button>
  </div>
</template>

<style scoped>
.banner {
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
  padding: 14px 14px 14px var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--tone-soft);
  font: 400 14px/20px var(--font-sans);
}
.icon {
  margin-top: 1px;
  color: var(--tone);
}
.text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex-grow: 1;
  min-width: 0;
}
.title {
  font-weight: 650;
  color: var(--ink);
}
.body {
  color: var(--ink-2);
}
.trace {
  font: var(--text-mono-s);
  color: var(--ink-3);
  overflow-wrap: anywhere;
}
.action,
.dismiss {
  align-self: center;
}
.dismiss {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--target);
  height: var(--target);
  margin: -4px -4px -4px 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--ink-2);
  cursor: pointer;
}
.dismiss:hover {
  background: var(--surface-3);
}
.info {
  --tone-soft: var(--info-soft);
  --tone: var(--info);
}
.success {
  --tone-soft: var(--success-soft);
  --tone: var(--success);
}
.warning {
  --tone-soft: var(--warning-soft);
  --tone: var(--warning);
}
.danger {
  --tone-soft: var(--danger-soft);
  --tone: var(--danger);
}
.offline {
  --tone-soft: var(--surface-3);
  --tone: var(--ink-2);
}
</style>
