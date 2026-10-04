<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'

/** A chunk type with its map colour, so lists, the editor and the map read the same. */
defineProps<{ type: Schemas['ChunkType'] }>()
const { t } = useI18n()
</script>

<template>
  <span class="type" :class="type"><span class="dot" aria-hidden="true" />{{ t(`memory.type.${type}`) }}</span>
</template>

<style scoped>
.type {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--ink-2);
  font: 550 13px/18px var(--font-sans);
  white-space: nowrap;
}
.dot {
  width: 8px;
  height: 8px;
  border-radius: var(--radius-full);
  background: var(--mem);
}
.fact {
  --mem: var(--mem-fact);
}
.preference {
  --mem: var(--mem-preference);
}
.event {
  --mem: var(--mem-event);
}
.procedure {
  --mem: var(--mem-procedure);
}
.person {
  --mem: var(--mem-person);
}
.device {
  --mem: var(--mem-device);
}
</style>
