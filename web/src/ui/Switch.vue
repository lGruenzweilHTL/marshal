<script setup lang="ts">
import { useId } from 'vue'

defineProps<{ label: string; description?: string; disabled?: boolean }>()
const model = defineModel<boolean>({ required: true })
const id = useId()
</script>

<template>
  <label class="switch" :for="id" :class="{ disabled }">
    <span class="text">
      <span class="label">{{ label }}</span>
      <span v-if="description" class="description">{{ description }}</span>
    </span>
    <input :id="id" v-model="model" type="checkbox" role="switch" :disabled="disabled" />
    <span class="track" aria-hidden="true"><span class="thumb" /></span>
  </label>
</template>

<style scoped>
.switch {
  position: relative;
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: var(--target);
  cursor: pointer;
}
.text {
  display: flex;
  flex-direction: column;
  flex-grow: 1;
}
.label {
  font-weight: 600;
}
.description {
  font: var(--text-body-s);
  color: var(--ink-2);
}
input {
  position: absolute;
  opacity: 0;
  width: 1px;
  height: 1px;
}
.track {
  position: relative;
  width: 46px;
  height: 26px;
  flex-shrink: 0;
  border-radius: var(--radius-full);
  background: var(--line-strong);
  transition: background var(--transition);
}
.thumb {
  position: absolute;
  top: 2px;
  left: 2px;
  width: 22px;
  height: 22px;
  border-radius: var(--radius-full);
  background: var(--surface);
  box-shadow: var(--shadow-1);
  transition: left var(--transition);
}
input:checked + .track {
  background: var(--accent);
}
input:checked + .track .thumb {
  left: 22px;
}
input:focus-visible + .track {
  box-shadow: var(--focus-ring);
}
.disabled {
  cursor: not-allowed;
  opacity: 0.6;
}
</style>
