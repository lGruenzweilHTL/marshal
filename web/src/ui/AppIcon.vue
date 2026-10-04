<script setup lang="ts">
import type { Component } from 'vue'

/** A Lucide icon at the design system's stroke width; decorative unless `label` is set. */
const props = withDefaults(defineProps<{ icon: Component; size?: number; stroke?: number; label?: string }>(), { size: 20, stroke: 1.75, label: undefined })
</script>

<template>
  <component
    :is="props.icon"
    class="icon"
    :size="props.size"
    :stroke-width="props.stroke"
    :aria-hidden="props.label ? undefined : 'true'"
    :aria-label="props.label"
    :role="props.label ? 'img' : undefined"
  />
</template>

<style scoped>
.icon {
  flex-shrink: 0;
}
</style>
