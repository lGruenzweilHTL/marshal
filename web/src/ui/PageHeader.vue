<script setup lang="ts">
import { ChevronLeft } from 'lucide-vue-next'
import type { RouteLocationRaw } from 'vue-router'
import { RouterLink } from 'vue-router'
import AppIcon from './AppIcon.vue'

/**
 * The page title. With `back`, phones get a compact bar with a back link (detail and deep-link screens); desktop shows
 * the back link above the title. Actions go into the `actions` slot.
 */
defineProps<{ title: string; subtitle?: string; eyebrow?: string; back?: RouteLocationRaw; backLabel?: string }>()
</script>

<template>
  <header class="page-header" :class="{ detail: !!back }">
    <RouterLink v-if="back" :to="back" class="back" data-test="back"><AppIcon :icon="ChevronLeft" :size="20" /><span>{{ backLabel }}</span></RouterLink>
    <div class="row">
      <div class="titles">
        <span v-if="eyebrow" class="t-overline">{{ eyebrow }}</span>
        <h1 class="t-title-1">{{ title }}</h1>
        <p v-if="subtitle || $slots.subtitle" class="subtitle"><slot name="subtitle">{{ subtitle }}</slot></p>
      </div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </div>
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}
.row {
  display: flex;
  align-items: flex-end;
  gap: var(--space-3);
  flex-wrap: wrap;
}
.titles {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex-grow: 1;
  min-width: 0;
}
.subtitle {
  color: var(--ink-2);
}
.actions {
  display: flex;
  gap: var(--space-2);
  flex-wrap: wrap;
}
.back {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  align-self: flex-start;
  min-height: var(--target);
  margin-left: -6px;
  padding: 0 var(--space-2) 0 2px;
  border-radius: var(--radius-sm);
  color: var(--ink-2);
  font-weight: 550;
  text-decoration: none;
}
.back:hover {
  background: var(--surface-3);
}
h1 {
  overflow-wrap: anywhere;
}
</style>
