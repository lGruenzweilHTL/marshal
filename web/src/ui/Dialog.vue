<script setup lang="ts">
import { useDesktop } from './useMedia'
import Overlay from './Overlay.vue'

/** A modal confirmation: centred on desktop, a bottom sheet on phones. */
defineProps<{ open: boolean; title: string; description?: string }>()
const emit = defineEmits<{ close: [] }>()
const desktop = useDesktop()
</script>

<template>
  <Overlay :kind="desktop ? 'dialog' : 'sheet'" :open="open" :title="title" :description="description" @close="emit('close')">
    <slot />
    <template v-if="$slots.actions" #actions><slot name="actions" /></template>
  </Overlay>
</template>
