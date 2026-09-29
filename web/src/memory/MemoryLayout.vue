<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'

const tabs = [
  { to: '/memory', label: 'List' },
  { to: '/memory/projects', label: 'Projects' },
  { to: '/memory/audit', label: 'Audit' },
  { to: '/memory/graph', label: 'Graph' },
  { to: '/memory/scatter', label: 'Scatter' },
]
</script>

<template>
  <div>
    <nav class="tabs" aria-label="Memory">
      <RouterLink v-for="tab in tabs" :key="tab.to" :to="tab.to" exact-active-class="active">{{ tab.label }}</RouterLink>
    </nav>
    <RouterView />
  </div>
</template>

<style scoped>
.tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem 1rem;
  padding: 0.5rem 1rem 0;
}

.tabs a.active {
  font-weight: 600;
  text-decoration: none;
}
</style>
