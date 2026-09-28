<script setup lang="ts">
import { widgets } from './widgets'
</script>

<template>
  <section class="page">
    <h1>Dashboard</h1>
    <div class="widgets">
      <article v-for="widget in widgets" :key="widget.id" class="widget" :data-widget="widget.id">
        <component :is="widget.component" />
      </article>
    </div>
  </section>
</template>

<style scoped>
.widgets {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(min(100%, 18rem), 1fr));
  gap: 1rem;
}

.widget {
  padding: 1rem;
  border: 1px solid var(--border);
  border-radius: 12px;
  min-width: 0;
}

.widget :deep(h2) {
  margin: 0 0 0.5rem;
  font-size: 1rem;
}
</style>
