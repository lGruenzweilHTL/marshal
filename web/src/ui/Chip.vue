<script setup lang="ts">
/** A filter chip; `selected` toggles with v-model. */
defineProps<{ label: string }>()
const selected = defineModel<boolean>({ default: false })
</script>

<template>
  <button type="button" class="chip" :aria-pressed="selected" @click="selected = !selected">{{ label }}</button>
</template>

<style scoped>
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 36px;
  padding: 0 14px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-full);
  background: var(--surface);
  color: var(--ink-2);
  font: 550 14px/1 var(--font-sans);
  cursor: pointer;
  white-space: nowrap;
}
.chip[aria-pressed='true'] {
  border-color: var(--ink);
  background: var(--ink);
  color: var(--bg);
}
.chip:focus-visible {
  outline: none;
  box-shadow: var(--focus-ring);
}
</style>
