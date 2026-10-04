<script setup lang="ts">
import Overlay from './Overlay.vue'

/** A bottom sheet: phone menus and confirmations. */
defineProps<{ open: boolean; title: string; description?: string }>()
const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <Overlay kind="sheet" :open="open" :title="title" :description="description" @close="emit('close')">
    <slot />
    <template v-if="$slots.actions" #actions><slot name="actions" /></template>
  </Overlay>
</template>
