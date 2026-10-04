<script setup lang="ts">
/** The content column of a view: page gutters, maximum width and the vertical rhythm between sections. */
withDefaults(defineProps<{ width?: 'narrow' | 'default' | 'wide' }>(), { width: 'default' })
</script>

<template>
  <div class="page" :class="width"><slot /></div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
  width: 100%;
  max-width: var(--page-max);
  margin: 0 auto;
  padding: var(--space-6) var(--page-gutter) var(--space-12);
}
.narrow {
  max-width: 760px;
}
.wide {
  max-width: none;
}
@media (min-width: 900px) {
  .page {
    gap: var(--space-8);
    padding-top: var(--space-10);
  }
}
</style>
