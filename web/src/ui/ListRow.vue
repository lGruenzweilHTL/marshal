<script setup lang="ts">
import { ChevronRight } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import AppIcon from './AppIcon.vue'

/** One row of a list card: optional lead (icon tile, avatar), title, subtitle and trailing content. Links show a chevron. */
const props = withDefaults(defineProps<{ title?: string; subtitle?: string; to?: RouteLocationRaw; chevron?: boolean }>(), {
  title: undefined,
  subtitle: undefined,
  to: undefined,
  chevron: undefined,
})
const showChevron = computed(() => props.chevron ?? !!props.to)
</script>

<template>
  <component :is="props.to ? RouterLink : 'div'" :to="props.to" class="row" :class="{ link: !!props.to }">
    <slot name="lead" />
    <span class="text">
      <span class="title"><slot name="title">{{ props.title }}</slot></span>
      <span v-if="props.subtitle || $slots.subtitle" class="subtitle"><slot name="subtitle">{{ props.subtitle }}</slot></span>
      <slot />
    </span>
    <slot name="trail" />
    <AppIcon v-if="showChevron" :icon="ChevronRight" :size="18" class="chevron" />
  </component>
</template>

<style scoped>
.row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: 56px;
  padding: var(--space-3) var(--space-4);
  color: var(--ink);
  text-decoration: none;
}
.link {
  cursor: pointer;
  transition: background var(--transition);
}
.link:hover {
  background: var(--surface-2);
}
.link:focus-visible {
  outline: none;
  box-shadow: inset 0 0 0 2px var(--focus);
}
.text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex-grow: 1;
  min-width: 0;
}
.title {
  font-weight: 600;
  overflow-wrap: anywhere;
}
.subtitle {
  font: var(--text-body-s);
  color: var(--ink-2);
}
.chevron {
  color: var(--ink-3);
}
</style>
