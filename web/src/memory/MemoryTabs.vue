<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'

/** The memory area's sections; each memory view shows them under its header. */
const { t } = useI18n()
const tabs = [
  { to: '/memory', key: 'chunks' },
  { to: '/memory/projects', key: 'projects' },
  { to: '/memory/map', key: 'map' },
  { to: '/memory/audit', key: 'audit' },
] as const
</script>

<template>
  <nav class="tabs" :aria-label="t('memory.title')">
    <RouterLink v-for="tab in tabs" :key="tab.key" :to="tab.to" class="tab" exact-active-class="active" :aria-current="undefined">{{ t(`memory.tabs.${tab.key}`) }}</RouterLink>
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
</style>
