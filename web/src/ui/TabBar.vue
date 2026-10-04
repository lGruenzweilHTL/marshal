<script setup lang="ts">
import type { Component } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import AppIcon from './AppIcon.vue'

export interface TabItem {
  id: string
  label: string
  icon: Component
  /** Destination; items without one emit `select` (the More sheet). */
  to?: RouteLocationRaw
  count?: number
  countLabel?: string
}

/** Phone navigation: up to five destinations at the bottom of the screen. */
defineProps<{ items: TabItem[]; active: string; label: string }>()
const emit = defineEmits<{ select: [id: string] }>()
</script>

<template>
  <nav class="tabbar" :aria-label="label">
    <component
      :is="item.to ? RouterLink : 'button'"
      v-for="item in items"
      :key="item.id"
      class="tab"
      :to="item.to"
      :type="item.to ? undefined : 'button'"
      :aria-current="item.id === active ? 'page' : undefined"
      :data-test="`tab-${item.id}`"
      @click="!item.to && emit('select', item.id)"
    >
      <span class="pill">
        <AppIcon :icon="item.icon" :size="22" :stroke="1.9" />
        <span v-if="item.count" class="count" :aria-label="item.countLabel">{{ item.count > 99 ? '99+' : item.count }}</span>
      </span>
      <span class="label">{{ item.label }}</span>
    </component>
  </nav>
</template>

<style scoped>
.tabbar {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  padding: 6px 6px calc(6px + env(safe-area-inset-bottom));
  background: var(--surface);
  border-top: 1px solid var(--line);
  box-shadow: var(--shadow-2);
}
.tab {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--space-1);
  min-height: 56px;
  border: 0;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--ink-3);
  font: 550 11px/14px var(--font-sans);
  text-decoration: none;
  cursor: pointer;
}
.tab:focus-visible {
  outline: none;
  box-shadow: inset 0 0 0 2px var(--focus);
}
.pill {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 30px;
  border-radius: var(--radius-full);
}
.tab[aria-current='page'] {
  color: var(--ink);
  font-weight: 700;
}
.tab[aria-current='page'] .pill {
  background: var(--accent-soft);
  color: var(--accent-text);
}
.count {
  position: absolute;
  top: -3px;
  right: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border: 2px solid var(--surface);
  border-radius: var(--radius-full);
  background: var(--accent);
  color: var(--accent-ink);
  font: 700 10px/1 var(--font-sans);
}
</style>
