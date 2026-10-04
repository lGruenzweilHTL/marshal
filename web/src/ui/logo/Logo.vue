<script setup lang="ts">
import { computed } from 'vue'
import mark from './mark.svg?raw'
import tile from './tile.svg?raw'
import wordmark from './wordmark.svg?raw'

/**
 * The Marshal logo. Every place that shows the logo uses this component; the artwork lives only in the SVG files next
 * to it, so a new logo means replacing those files (see ../README.md).
 *
 * - `wordmark`: the name, `size` is the cap height; letters follow the text colour, the A the accent.
 * - `tile`: the app icon (mark on the graphite tile), `size` is the edge length.
 * - `mark`: the mark without the tile, `size` is the edge length.
 */
const props = withDefaults(defineProps<{ variant?: 'wordmark' | 'tile' | 'mark'; size?: number; label?: string }>(), {
  variant: 'wordmark',
  size: 20,
  label: 'Marshal',
})
const svg = computed(() => ({ wordmark, tile, mark })[props.variant].replace(/<!--[\s\S]*?-->/g, ''))
</script>

<template>
  <span class="logo" :class="variant" role="img" :aria-label="label" :style="{ '--size': `${size}px` }" v-html="svg" />
</template>

<style scoped>
.logo {
  display: inline-flex;
  flex-shrink: 0;
  height: var(--size);
  line-height: 0;
}
.tile,
.mark {
  width: var(--size);
}
.logo :deep(svg) {
  width: auto;
  height: 100%;
}
</style>
