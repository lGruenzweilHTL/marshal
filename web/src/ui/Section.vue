<script setup lang="ts">
import { ChevronRight } from 'lucide-vue-next'
import type { RouteLocationRaw } from 'vue-router'
import { RouterLink } from 'vue-router'
import AppIcon from './AppIcon.vue'

/** A titled block of a page with an optional count and a link to the full view. */
defineProps<{ title: string; count?: number; action?: string; to?: RouteLocationRaw }>()
</script>

<template>
  <section class="section">
    <header class="head">
      <h2 class="title">{{ title }}</h2>
      <span v-if="count !== undefined" class="count num">{{ count }}</span>
      <span class="spacer" />
      <slot name="actions" />
      <RouterLink v-if="action && to" :to="to" class="action">{{ action }}<AppIcon :icon="ChevronRight" :size="16" /></RouterLink>
    </header>
    <slot />
  </section>
</template>

<style scoped>
.section {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  min-width: 0;
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: 32px;
}
.title {
  font: 620 18px/24px var(--font-display);
  letter-spacing: -0.01em;
}
@media (min-width: 900px) {
  .title {
    font-size: 19px;
    line-height: 26px;
  }
}
.count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 22px;
  height: 22px;
  padding: 0 7px;
  border-radius: var(--radius-full);
  background: var(--surface-3);
  color: var(--ink-2);
  font: 700 12px/1 var(--font-sans);
}
.spacer {
  flex-grow: 1;
}
.action {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  min-height: var(--target);
  padding: 0 var(--space-1);
  border-radius: var(--radius-sm);
  color: var(--ink-2);
  font: 600 14px/1 var(--font-sans);
  text-decoration: none;
}
.action:hover {
  color: var(--ink);
}
</style>
