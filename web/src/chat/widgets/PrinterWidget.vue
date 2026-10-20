<script setup lang="ts">
import { computed, onMounted, shallowRef } from 'vue'
import type { Schemas } from '../../api/client'
import { useApi } from '../../api/inject'
import { useLiveEvent } from '../../live/liveEvents'
import PrinterCard from '../../printers/PrinterCard.vue'

/**
 * The `printer` widget: the printer card with pause / resume / cancel, starting from the turn's state, then the
 * printer's current state and its live updates. Controls go through the printer endpoints as widget actions.
 */
const props = defineProps<{ data: { printer: string; state: Schemas['PrinterState'] | null } }>()
const api = useApi()
const state = shallowRef<Schemas['PrinterState'] | null>(props.data.state)

onMounted(async () => {
  const result = await api.call((client) => client.GET('/api/v1/printers/{printer}', { params: { path: { printer: props.data.printer } } }))
  if (result.data) state.value = result.data
})

useLiveEvent('printer', 'printer.state', ({ printer, state: next }) => {
  if (printer === props.data.printer) state.value = next
})

const summary = computed<Schemas['PrinterSummary']>(() => ({
  name: props.data.printer,
  online: state.value !== null,
  state: state.value,
  macros: [],
  webUrl: null,
  babysitter: null,
}))
</script>

<template>
  <div data-widget="printer" class="widget">
    <PrinterCard :printer="summary" actions widget />
  </div>
</template>

<style scoped>
.widget {
  max-width: 480px;
}
</style>
