<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import ChunkType from './ChunkType.vue'
import { chunkTypes } from './memoryLabels'

type Type = Schemas['ChunkType']

/**
 * The chunk types with their map colours. With `hidden`, each type is a toggle that hides or shows its chunks; the last
 * shown type cannot be turned off.
 */
const props = defineProps<{ hidden?: ReadonlySet<Type> }>()
const emit = defineEmits<{ toggle: [type: Type] }>()
const { t } = useI18n()
const shownCount = computed(() => chunkTypes.filter((type) => !props.hidden?.has(type)).length)
</script>

<template>
  <div v-if="hidden" class="legend" role="group" :aria-label="t('memory.graph.filter')">
    <button
      v-for="type in chunkTypes"
      :key="type"
      type="button"
      class="toggle"
      :class="{ off: hidden.has(type) }"
      :data-type-toggle="type"
      :aria-pressed="!hidden.has(type)"
      :disabled="!hidden.has(type) && shownCount === 1"
      @click="emit('toggle', type)"
    >
      <ChunkType :type="type" />
    </button>
  </div>
  <div v-else class="legend">
    <ChunkType v-for="type in chunkTypes" :key="type" :type="type" />
  </div>
</template>

<style scoped>
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2) 14px;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.legend:has(.toggle) {
  gap: 2px;
  padding: 2px;
}
.toggle {
  display: inline-flex;
  align-items: center;
  min-height: 32px;
  padding: 0 var(--space-2);
  border: 0;
  border-radius: var(--radius-sm);
  background: none;
  cursor: pointer;
  transition: background-color var(--transition), opacity var(--transition);
}
.toggle:hover:not(:disabled) {
  background: var(--surface-2);
}
.toggle:focus-visible {
  outline: 2px solid var(--focus);
  outline-offset: 1px;
}
.toggle.off {
  opacity: 0.45;
}
.toggle.off :deep(.dot) {
  background: none;
  box-shadow: inset 0 0 0 1.5px var(--mem);
}
.toggle:disabled {
  cursor: default;
}
</style>
