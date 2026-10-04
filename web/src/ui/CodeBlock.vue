<script setup lang="ts">
defineProps<{ code: string; label?: string }>()
</script>

<template>
  <pre class="code" :aria-label="label">{{ code }}</pre>
</template>

<style scoped>
.code {
  padding: var(--space-3) 14px;
  background: var(--surface-2);
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  font: var(--text-mono);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
