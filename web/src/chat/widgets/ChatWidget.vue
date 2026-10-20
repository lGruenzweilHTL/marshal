<script setup lang="ts">
import type { TurnWidget } from '../turnEvents'
import MusicWidget, { type MusicWidgetData } from './MusicWidget.vue'
import PrinterWidget from './PrinterWidget.vue'

/** A built-in widget below a reply, by kind; an unknown kind renders nothing. */
defineProps<{ widget: TurnWidget }>()
</script>

<template>
  <PrinterWidget v-if="widget.kind === 'printer'" :data="widget.data as { printer: string; state: null }" />
  <MusicWidget v-else-if="widget.kind === 'music'" :data="widget.data as MusicWidgetData" />
</template>
