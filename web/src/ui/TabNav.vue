<script setup lang="ts">
import { RouterLink } from 'vue-router'

/** Links to the sections of one area, under the page header. The link of the current route is marked active. */
defineProps<{ tabs: { to: string; label: string; count?: number }[]; label: string }>()
</script>

<template>
  <nav class="tabs" :aria-label="label">
    <RouterLink v-for="tab in tabs" :key="tab.to" :to="tab.to" class="tab" exact-active-class="active" :aria-current="undefined">
      {{ tab.label }}
      <span v-if="tab.count" class="count num">{{ tab.count }}</span>
    </RouterLink>
  </nav>
</template>

<style scoped>
.tabs {
  display: flex;
  gap: var(--space-6);
  overflow-x: auto;
  border-bottom: 1px solid var(--line);
}
.tab {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  height: 40px;
  padding: 0 2px;
  border-bottom: 2px solid transparent;
  color: var(--ink-2);
  font: 550 15px/20px var(--font-sans);
  text-decoration: none;
  white-space: nowrap;
}
.tab.active {
  border-bottom-color: var(--accent);
  color: var(--ink);
  font-weight: 650;
}
.tab:focus-visible {
  outline: var(--focus-ring);
}
.count {
  min-width: 20px;
  padding: 0 6px;
  border-radius: var(--radius-full);
  background: var(--warning-soft);
  color: var(--warning);
  font: var(--text-label);
  text-align: center;
}
</style>
