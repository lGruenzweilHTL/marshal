<script setup lang="ts">
import { Check } from 'lucide-vue-next'
import AppIcon from './AppIcon.vue'
import { useToasts } from './toast'

const toasts = useToasts()
</script>

<template>
  <div class="toasts" role="status" aria-live="polite">
    <div v-for="toast in toasts.items.value" :key="toast.id" class="toast"><AppIcon :icon="Check" :size="18" :stroke="2.25" />{{ toast.message }}</div>
  </div>
</template>

<style scoped>
.toasts {
  position: fixed;
  left: 50%;
  bottom: calc(var(--tabbar-height) + var(--space-6) + env(safe-area-inset-bottom));
  z-index: 60;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  transform: translateX(-50%);
  pointer-events: none;
}
@media (min-width: 900px) {
  .toasts {
    bottom: var(--space-6);
  }
}
.toast {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--target);
  padding: 0 var(--space-4);
  border-radius: var(--radius-full);
  background: var(--ink);
  color: var(--bg);
  font-weight: 600;
  box-shadow: var(--shadow-3);
  white-space: nowrap;
}
</style>
