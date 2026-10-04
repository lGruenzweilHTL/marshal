<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{ name: string; size?: number }>(), { size: 32 })
const initials = computed(() => props.name.trim().charAt(0).toUpperCase() || '?')
</script>

<template>
  <span class="avatar" :style="{ '--size': `${size}px` }" aria-hidden="true">{{ initials }}</span>
</template>

<style scoped>
.avatar {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--size);
  height: var(--size);
  flex-shrink: 0;
  border-radius: var(--radius-full);
  background: var(--surface-3);
  color: var(--ink);
  font: 650 calc(var(--size) * 0.4) / 1 var(--font-sans);
}
</style>
