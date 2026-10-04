<script setup lang="ts">
withDefaults(defineProps<{ value: number; label: string; tone?: 'accent' | 'warning' | 'danger' | 'info' | 'success' }>(), { tone: 'accent' })
</script>

<template>
  <div class="progress" role="progressbar" :aria-label="label" :aria-valuenow="Math.round(value)" aria-valuemin="0" aria-valuemax="100">
    <div class="bar" :class="tone" :style="{ width: `${Math.max(0, Math.min(100, value))}%` }" />
  </div>
</template>

<style scoped>
.progress {
  height: 8px;
  border-radius: var(--radius-full);
  background: var(--surface-3);
  overflow: hidden;
}
.bar {
  height: 100%;
  border-radius: var(--radius-full);
  background: var(--tone);
  transition: width var(--transition);
}
.accent {
  --tone: var(--accent);
}
.warning {
  --tone: var(--warning);
}
.danger {
  --tone: var(--danger);
}
.info {
  --tone: var(--info);
}
.success {
  --tone: var(--success);
}
</style>
