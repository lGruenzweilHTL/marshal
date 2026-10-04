<script setup lang="ts">
import { useI18n } from 'vue-i18n'

/** Importance 1–5 as five dots. */
defineProps<{ value: number }>()
const { t } = useI18n()
</script>

<template>
  <span class="dots" role="img" :aria-label="t('memory.importance', { value })">
    <span v-for="index in 5" :key="index" class="dot" :class="{ on: index <= value }" />
  </span>
</template>

<style scoped>
.dots {
  display: inline-flex;
  gap: 3px;
}
.dot {
  width: 6px;
  height: 6px;
  border-radius: var(--radius-full);
  background: var(--line);
}
.dot.on {
  background: var(--ink-2);
}
</style>
