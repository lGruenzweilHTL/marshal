<script setup lang="ts">
import type { Component } from 'vue'
import AppIcon from './AppIcon.vue'
import type { Tone } from './Badge.vue'

withDefaults(defineProps<{ icon: Component; tone?: Tone; size?: number }>(), { tone: 'neutral', size: 36 })
</script>

<template>
  <span class="tile" :class="tone" :style="{ '--size': `${size}px` }" aria-hidden="true"><AppIcon :icon="icon" :size="Math.round(size / 2)" /></span>
</template>

<style scoped>
.tile {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--size);
  height: var(--size);
  flex-shrink: 0;
  border-radius: var(--radius-md);
  background: var(--tone-soft);
  color: var(--tone);
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
