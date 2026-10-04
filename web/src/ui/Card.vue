<script setup lang="ts">
/** A surface for one piece of content. `flush` drops the padding for lists and tables that run to the edges. */
withDefaults(defineProps<{ as?: string; flush?: boolean; title?: string }>(), { as: 'section', flush: false, title: undefined })
</script>

<template>
  <component :is="as" class="card" :class="{ flush }">
    <header v-if="title || $slots.header" class="head">
      <h2 v-if="title" class="t-title-3">{{ title }}</h2>
      <slot name="header" />
    </header>
    <slot />
  </component>
</template>

<style scoped>
.card {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  padding: var(--space-5);
  background: var(--surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
  min-width: 0;
}
.flush {
  padding: 0;
  gap: 0;
  overflow: hidden;
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}
.flush .head {
  padding: var(--space-4) var(--space-4) var(--space-2);
}
.head :deep(h2) {
  flex-grow: 1;
}
</style>
