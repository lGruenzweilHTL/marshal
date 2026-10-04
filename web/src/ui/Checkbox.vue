<script setup lang="ts">
import { Check } from 'lucide-vue-next'
import { useId } from 'vue'
import AppIcon from './AppIcon.vue'

defineProps<{ label: string; description?: string; mono?: boolean; disabled?: boolean; value?: string }>()
const model = defineModel<boolean | string[]>({ required: true })
const id = useId()
</script>

<template>
  <label class="checkbox" :for="id" :class="{ disabled }">
    <input :id="id" v-model="model" type="checkbox" :value="value" :disabled="disabled" />
    <span class="box" aria-hidden="true"><AppIcon :icon="Check" :size="14" :stroke="2.5" /></span>
    <span class="text">
      <span :class="{ 't-mono': mono }">{{ label }}</span>
      <span v-if="description" class="description">{{ description }}</span>
    </span>
  </label>
</template>

<style scoped>
.checkbox {
  position: relative;
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: var(--target);
  cursor: pointer;
}
input {
  position: absolute;
  opacity: 0;
  width: 1px;
  height: 1px;
}
.box {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  flex-shrink: 0;
  border: 1.5px solid var(--line-strong);
  border-radius: var(--radius-xs);
  background: var(--surface);
  color: transparent;
}
input:checked + .box {
  border-color: var(--accent);
  background: var(--accent);
  color: var(--accent-ink);
}
input:focus-visible + .box {
  box-shadow: var(--focus-ring);
}
.text {
  display: flex;
  flex-direction: column;
}
.description {
  font: var(--text-body-s);
  color: var(--ink-3);
}
.disabled {
  cursor: not-allowed;
  opacity: 0.6;
}
</style>
