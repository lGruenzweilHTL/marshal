<script setup lang="ts">
import ChunkType from './ChunkType.vue'
import { chunkTypes } from './memoryLabels'
</script>

<template>
  <div class="legend">
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
</style>
