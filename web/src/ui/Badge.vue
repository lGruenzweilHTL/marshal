<script setup lang="ts">
export type Tone = 'neutral' | 'accent' | 'info' | 'success' | 'warning' | 'danger'

withDefaults(defineProps<{ tone?: Tone; dot?: boolean; label?: string }>(), { tone: 'neutral', dot: false, label: undefined })
</script>

<template>
  <span class="badge" :class="tone">
    <span v-if="dot" class="dot" aria-hidden="true" />
    <span><slot>{{ label }}</slot></span>
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 22px;
  padding: 0 var(--space-2);
  border-radius: var(--radius-full);
  background: var(--tone-soft);
  color: var(--tone);
  font: 600 12px/1 var(--font-sans);
  white-space: nowrap;
}
.dot {
  width: 6px;
  height: 6px;
  border-radius: var(--radius-full);
  background: currentColor;
}
.neutral {
  --tone-soft: var(--surface-3);
  --tone: var(--ink-2);
}
.accent {
  --tone-soft: var(--accent-soft);
  --tone: var(--accent-text);
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
</style>
