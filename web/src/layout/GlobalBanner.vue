<script setup lang="ts">
import ErrorBanner from '../ui/ErrorBanner.vue'
import { useErrorBanner } from './banner'

const banner = useErrorBanner()
</script>

<template>
  <div v-if="banner.entries.value.length" class="global-banner">
    <ErrorBanner v-for="entry in banner.entries.value" :key="entry.id" :error="entry.error" dismissible @dismiss="banner.dismiss(entry.id)" />
  </div>
</template>

<style scoped>
.global-banner {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-3) var(--page-gutter) 0;
}
</style>
