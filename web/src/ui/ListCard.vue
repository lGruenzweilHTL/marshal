<script setup lang="ts">
/** A card holding list rows separated by hairlines. Pass rows (ListRow) as children; `as="ul"` wraps nothing extra. */
withDefaults(defineProps<{ title?: string; label?: string }>(), { title: undefined, label: undefined })
</script>

<template>
  <section class="list-card" :aria-label="label">
    <header v-if="title || $slots.header" class="head">
      <h2 v-if="title" class="t-title-3">{{ title }}</h2>
      <slot name="header" />
    </header>
    <div class="rows"><slot /></div>
    <footer v-if="$slots.footer" class="foot"><slot name="footer" /></footer>
  </section>
</template>

<style scoped>
.list-card {
  display: flex;
  flex-direction: column;
  background: var(--surface);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1);
  overflow: hidden;
  min-width: 0;
}
.head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-4) var(--space-4) var(--space-1);
}
.head :deep(h2) {
  flex-grow: 1;
}
.rows {
  display: flex;
  flex-direction: column;
}
.rows > :deep(* + *) {
  border-top: 1px solid var(--line);
}
.foot {
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--line);
}
</style>
