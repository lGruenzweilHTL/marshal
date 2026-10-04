<script setup lang="ts">
import type { Component } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import AppIcon from './AppIcon.vue'

export interface NavItem {
  id: string
  label: string
  icon: Component
  to: RouteLocationRaw
  count?: number
  countLabel?: string
  note?: string
}

export interface NavGroup {
  id: string
  label: string
  items: NavItem[]
}

/** Desktop navigation: grouped destinations, a brand slot on top and an account slot at the bottom. */
defineProps<{ groups: NavGroup[]; active: string; label: string }>()
</script>

<template>
  <nav class="sidenav" :aria-label="label">
    <div class="brand"><slot name="brand" /></div>
    <div class="groups">
      <div v-for="group in groups" :key="group.id" class="group">
        <div class="t-overline group-label">{{ group.label }}</div>
        <RouterLink
          v-for="item in group.items"
          :key="item.id"
          :to="item.to"
          class="item"
          :aria-current="item.id === active ? 'page' : undefined"
          :data-test="`nav-${item.id}`"
        >
          <AppIcon :icon="item.icon" :size="18" class="item-icon" />
          <span class="item-label">{{ item.label }}</span>
          <span v-if="item.count" class="count" :aria-label="item.countLabel">{{ item.count > 99 ? '99+' : item.count }}</span>
          <span v-if="item.note" class="note">{{ item.note }}</span>
        </RouterLink>
      </div>
    </div>
    <div class="account"><slot name="account" /></div>
  </nav>
</template>

<style scoped>
.sidenav {
  display: flex;
  flex-direction: column;
  gap: 18px;
  width: var(--sidenav-width);
  height: 100dvh;
  padding: 18px 14px 14px;
  background: var(--bg);
  border-right: 1px solid var(--line);
}
.brand {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 6px;
  min-height: 32px;
}
.groups {
  display: flex;
  flex-direction: column;
  gap: 14px;
  flex-grow: 1;
  overflow: auto;
}
.group {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.group-label {
  padding: 0 var(--space-3) var(--space-1);
}
.item {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: 36px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-md);
  color: var(--ink-2);
  font: 500 14px/20px var(--font-sans);
  text-decoration: none;
}
.item:hover {
  background: var(--surface-3);
}
.item:focus-visible {
  outline: none;
  box-shadow: var(--focus-ring);
}
.item-icon {
  color: var(--ink-3);
}
.item[aria-current='page'] {
  background: var(--surface);
  box-shadow: var(--shadow-1);
  color: var(--ink);
  font-weight: 650;
}
.item[aria-current='page'] .item-icon {
  color: var(--accent);
}
.item-label {
  flex-grow: 1;
}
.count {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 22px;
  height: 20px;
  padding: 0 6px;
  border-radius: var(--radius-full);
  background: var(--accent);
  color: var(--accent-ink);
  font: 700 12px/1 var(--font-sans);
}
.note {
  font: 600 11px/1 var(--font-sans);
  color: var(--ink-3);
}
.account {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 6px 0;
  border-top: 1px solid var(--line);
}
</style>
