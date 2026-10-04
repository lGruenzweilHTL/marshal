<script setup lang="ts" generic="T extends string">
/** A segmented control for 2–4 exclusive options (filters, view toggles). */
defineProps<{ options: { value: T; label: string }[]; label: string; block?: boolean }>()
const model = defineModel<T>({ required: true })
</script>

<template>
  <div class="segmented" :class="{ block }" role="group" :aria-label="label">
    <button v-for="option in options" :key="option.value" type="button" :data-value="option.value" :aria-pressed="model === option.value" @click="model = option.value">
      {{ option.label }}
    </button>
  </div>
</template>

<style scoped>
.segmented {
  display: inline-flex;
  gap: 2px;
  padding: 3px;
  background: var(--surface-3);
  border-radius: calc(var(--radius-md) + 1px);
  max-width: 100%;
  overflow-x: auto;
}
.block {
  display: flex;
}
button {
  flex: 1;
  min-height: 38px;
  padding: 0 14px;
  border: 0;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--ink-2);
  font: 600 14px/1 var(--font-sans);
  white-space: nowrap;
  cursor: pointer;
}
button[aria-pressed='true'] {
  background: var(--surface);
  color: var(--ink);
  box-shadow: var(--shadow-1);
}
button:focus-visible {
  outline: none;
  box-shadow: var(--focus-ring);
}
</style>
